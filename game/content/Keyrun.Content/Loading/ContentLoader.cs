using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keyrun.Content;

/// <summary>
/// Loads every content kind from the verbatim content/data JSON into a typed, validated
/// <see cref="ContentRegistry"/>: the C# mirror of content/index.ts <c>loadContent()</c>, reading
/// the same 19 files in the same order, plus the C#-only run.json, theme.json (theme.json is read by ThemeLoader), combo-riders.json,
/// hazards.json, the trash enemies (enemies/hall.json) and the BIBLE 9.3 vault kits (abilities/vault.json, effects/vault.json,
/// enemies/vault.json), and the per-kind directories (ruling R-LOADER-1): layout/hollow-vault/ (dungeon.json plus one file
/// per room, pack and patrol; packs live in the layout, ruling R7) and forces/ (hollow-vault.json plus one file per enemy
/// value), read in ordinal path order by <see cref="PerKindContent"/>. Fails closed with <see cref="ContentLoadException"/> on
/// any unknown property, missing required field, null in a non-optional field, non-integer
/// number, unknown enum or op, duplicate id, unresolved cross-reference, or a missing file or one neither listed in
/// <see cref="Files"/> nor under one of <see cref="Directories"/>.
/// </summary>
public static class ContentLoader
{
    private const string Abilities = "abilities/";
    private const string Effects = "effects/";
    private const string Classes = "classes/";
    private const string Enemies = "enemies/";
    private const string Facets = "facets/";

    /// <summary>The files read, in TS loadContent order, relative to content/data.</summary>
    public static IReadOnlyList<string> Files { get; } = new[]
    {
        Abilities + "warden.json", Abilities + "cinder.json", Abilities + "chanter.json", Abilities + "enemies.json",
        Abilities + PatrolAbilitiesFile,
        Effects + "player.json", Effects + "enemy.json",
        Classes + "warden.json", Classes + "cinder.json", Classes + "chanter.json",
        "combos.json",
        Enemies + "choir.json", Enemies + "pair.json", Enemies + "seed.json", Enemies + "hollow-warden.json",
        "dungeons/hollow-vault.json",
        "clauses.json",
        Facets + "warden.json", Facets + "cinder.json", Facets + "chanter.json",
        RunFile,
        LayoutFile,
        ThemeFile,
        PaletteFile,
        ComboRidersFile,
        ForcesFile,
        HazardsFile,
        GearFile,
        TrashEnemiesFile,
        MeleeAbilitiesFile,
        VaultAbilitiesFile,
        VaultEffectsFile,
        VaultEnemiesFile,
        FoundryAbilitiesFile,
        FoundryEffectsFile,
        FoundryEnemiesFile,
        FoundryDungeonFile,
    };

    /// <summary>
    /// D66 (the second dungeon, the Sunken Bellfoundry): its five new trash kinds' and three bosses' casts
    /// (game/content/data/abilities/bellfoundry.json; C#-only, the root abilities JSON is a frozen oracle).
    /// </summary>
    public const string FoundryAbilitiesFile = Abilities + "bellfoundry.json";

    /// <summary>D66: the Sunken Bellfoundry's auras (game/content/data/effects/bellfoundry.json).</summary>
    public const string FoundryEffectsFile = Effects + "bellfoundry.json";

    /// <summary>D66: the Sunken Bellfoundry's enemies and bosses (game/content/data/enemies/bellfoundry.json).</summary>
    public const string FoundryEnemiesFile = Enemies + "bellfoundry.json";

    /// <summary>D65/D66: the Sunken Bellfoundry's dungeon definition (game/content/data/dungeons/sunken-bellfoundry.json).</summary>
    public const string FoundryDungeonFile = "dungeons/sunken-bellfoundry.json";

    /// <summary>
    /// The seeded dungeons' pack rosters (D46), one <see cref="RosterPack"/> per file, read whole (R-LOADER-1): a new
    /// pack is a new file, never a list edit.
    /// </summary>
    public const string RosterRoot = "roster/";

    /// <summary>
    /// C#-only vault casts (game/content/data/abilities/vault.json, same logical root; FORCES-ROUTE-PLAN slice S1
    /// VAULT-KITS, BIBLE section 9.3): Searing Hex, Cinders, Stoke, Undertow and Crushing Slam. The root abilities JSON is
    /// a frozen oracle, so the bible 9.2 pack casters' kits live here.
    /// </summary>
    public const string VaultAbilitiesFile = Abilities + "vault.json";

    /// <summary>
    /// C#-only vault auras (game/content/data/effects/vault.json; BIBLE sections 9.3 and 9.4): the Searing Hex burn,
    /// Cinders, Stoked, the Undertow root and Drenched, in the shapes the CASTER-OPS-SIM tests drive (#302).
    /// </summary>
    public const string VaultEffectsFile = Effects + "vault.json";

    /// <summary>
    /// C#-only vault enemies (game/content/data/enemies/vault.json; BIBLE section 9.3): Hollow Hound, Ash Hexer, Vault
    /// Brute, Bellows Keeper and Drowned Chorister, the members of the bible 9.2 packs W1, W2 and E1-E3.
    /// </summary>
    public const string VaultEnemiesFile = Enemies + "vault.json";

    /// <summary>
    /// C#-only melee ability ids (game/content/data/melee-abilities.json, same logical root; KF1 strike cue, ruling R9).
    /// The root abilities JSON is a frozen oracle and is not edited to tag melee, so the list lives here.
    /// </summary>
    public const string MeleeAbilitiesFile = "melee-abilities.json";

    /// <summary>
    /// R-PATROL-3b (C#-only, game/content/data/abilities/patrol.json): the corridor patrol's own swing, choir.patrolSwing,
    /// a WoW-trash swing at the choir.meleeSwing cadence with the patrol's own raw damage. choir.meleeSwing (frozen in
    /// content/data) stays as it is for the Choir pull.
    /// </summary>
    public const string PatrolAbilitiesFile = "patrol.json";

    /// <summary>
    /// C#-only trash enemies (game/content/data/enemies/hall.json, same logical root; PATH-PACKS): enemies only a layout
    /// pack fields, never a teaching pull, so the frozen TS loadContent has no counterpart. Today the Hall Sentry
    /// (<c>hall.sentry</c>), the Mythic+ trash the required Hall Sentries pack is built from.
    /// </summary>
    public const string TrashEnemiesFile = Enemies + "hall.json";

    /// <summary>C#-only gear table (game/content/data/gear.json; BIBLE section 7 "Five slots", "Tier is power").</summary>
    public const string GearFile = "gear.json";

    /// <summary>The dungeon whose per-kind layout and forces directories the loader assembles (ruling R-LOADER-1).</summary>
    public const string LayoutDungeon = "hollow-vault";

    /// <summary>
    /// C#-only forces bar size and route (game/content/data/forces/hollow-vault.json; BIBLE section 9.2): the bar's size
    /// and the fork and rejoin pulls. Every enemy's points live one per file in forces/values/ (R-LOADER-1), so a new
    /// forces value is a new file, never a list edit.
    /// </summary>
    public const string ForcesFile = PerKindContent.ForcesRoot + LayoutDungeon + ".json";

    /// <summary>C#-only room hazards (game/content/data/hazards.json; BIBLE section 9.4).</summary>
    public const string HazardsFile = "hazards.json";

    /// <summary>
    /// C#-only combo riders (game/content/data/combo-riders.json, same logical root): replacement effects for combos
    /// whose BIBLE section 4 effect the frozen combos.json cannot express (Anthem, Steam). Not read by the TS loadContent.
    /// </summary>
    public const string ComboRidersFile = "combo-riders.json";

    /// <summary>
    /// C#-only per-room dressing (game/content/data/theme.json, same logical root; ROOM-THEMING-1). Listed so the file
    /// set check accepts it, but not parsed into the registry: <see cref="ThemeLoader"/> reads it for the view only.
    /// </summary>
    public const string ThemeFile = "theme.json";

    /// <summary>
    /// C#-only render palette (game/content/data/palette.json, same logical root; design/BIBLE.md section 3 telegraph contrast).
    /// Listed so the file set check accepts it, but not parsed into the registry: <see cref="PaletteLoader"/> reads it for the view only.
    /// </summary>
    public const string PaletteFile = "palette.json";

    /// <summary>
    /// C#-only dungeon layout head (game/content/data/layout/hollow-vault/dungeon.json, same logical root): the start,
    /// route and formation (STATE.md D18). The rooms, packs and patrols live one per file beside it in rooms/, packs/ and
    /// patrols/ (R-LOADER-1), so a new room, pack or patrol is a new file, never a list edit.
    /// </summary>
    public const string LayoutFile = PerKindContent.LayoutRoot + LayoutDungeon + "/dungeon.json";

    /// <summary>
    /// The directories read whole (<see cref="PerKindContent.DirectoriesFor"/>): any JSON file in them loads without a
    /// <see cref="Files"/> entry. Every other file must be listed (the frozen content/data entries stay an explicit list).
    /// </summary>
    public static IReadOnlyList<string> Directories { get; } = PerKindContent.DirectoriesFor(LayoutDungeon).Append(RosterRoot).ToArray();

    /// <summary>
    /// C#-only run config (game/content/data/run.json, embedded under the same logical root): walk
    /// speed, actor radius and arena bounds. Not read by the frozen TS loadContent.
    /// </summary>
    public const string RunFile = "run.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new ValueListConverterFactory(),
            new ValueMapConverterFactory(),
            new StrictEnumConverterFactory(),
            new EffectOpConverter(),
            new ScriptAtConverter(),
        },
    };

    /// <summary>Loads and validates the content embedded in Keyrun.Content.dll.</summary>
    public static ContentRegistry Load() => Load(EmbeddedContentSource.Instance);

    /// <summary>Loads and validates content from <paramref name="source"/>.</summary>
    /// <param name="source">Must hold exactly the JSON files in <see cref="Files"/>.</param>
    /// <exception cref="ContentLoadException">Any file or invariant fails.</exception>
    public static ContentRegistry Load(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        CheckFileSet(source);

        var registry = new ContentRegistry
        {
            Abilities = ById("ability",
                ReadArray<AbilityDef>(source, Abilities + "warden.json"),
                ReadArray<AbilityDef>(source, Abilities + "cinder.json"),
                ReadArray<AbilityDef>(source, Abilities + "chanter.json"),
                ReadArray<AbilityDef>(source, Abilities + "enemies.json"),
                ReadArray<AbilityDef>(source, Abilities + PatrolAbilitiesFile),
                ReadArray<AbilityDef>(source, VaultAbilitiesFile),
                ReadArray<AbilityDef>(source, FoundryAbilitiesFile)),
            Effects = ById("effect",
                ReadArray<EffectDef>(source, Effects + "player.json"),
                ReadArray<EffectDef>(source, Effects + "enemy.json"),
                ReadArray<EffectDef>(source, VaultEffectsFile),
                ReadArray<EffectDef>(source, FoundryEffectsFile)),
            Classes = ById("class",
                ReadOne<ClassDef>(source, Classes + "warden.json"),
                ReadOne<ClassDef>(source, Classes + "cinder.json"),
                ReadOne<ClassDef>(source, Classes + "chanter.json")),
            Combos = ById("combo", ReadArray<ComboEntry>(source, "combos.json")),
            Enemies = ById("enemy",
                ReadArray<EnemyDef>(source, Enemies + "choir.json"),
                ReadArray<EnemyDef>(source, Enemies + "pair.json"),
                ReadArray<EnemyDef>(source, Enemies + "seed.json"),
                ReadArray<EnemyDef>(source, Enemies + "hollow-warden.json"),
                ReadArray<EnemyDef>(source, TrashEnemiesFile),
                ReadArray<EnemyDef>(source, VaultEnemiesFile),
                ReadArray<EnemyDef>(source, FoundryEnemiesFile)),
            Dungeons = ById("dungeon",
                ReadOne<DungeonDef>(source, "dungeons/hollow-vault.json"),
                ReadOne<DungeonDef>(source, FoundryDungeonFile)),
            Roster = ById("roster pack", ContentDirectory.Under(source, RosterRoot).Select(p => ReadOne<RosterPack>(source, p)[0]).ToArray()),
            Clauses = ById("clause", ReadArray<ClauseDef>(source, "clauses.json")),
            Facets = ById("facet",
                ReadArray<FacetDef>(source, Facets + "warden.json"),
                ReadArray<FacetDef>(source, Facets + "cinder.json"),
                ReadArray<FacetDef>(source, Facets + "chanter.json")),
            Run = ReadOne<RunConfig>(source, RunFile)[0],
            Layout = PerKindContent.AssembleLayout(source, LayoutDungeon),
            ComboRiders = ById("combo rider", ReadArray<ComboRider>(source, ComboRidersFile)),
            Forces = PerKindContent.AssembleForces(source, LayoutDungeon),
            Hazards = ById("hazard", ReadArray<HazardDef>(source, HazardsFile)),
            Gear = ById("gear item", ReadArray<GearItemDef>(source, GearFile)),
            MeleeAbilities = new ValueList<string>(ReadArray<string>(source, MeleeAbilitiesFile)),
        };

        ContentValidator.Validate(registry);
        return registry;
    }

    private static void CheckFileSet(IContentSource source) => ContentFileSet.Check(source, Files, Directories);

    private static T[] ReadArray<T>(IContentSource source, string path)
        where T : class
    {
        T[] items = Parse<T[]>(source, path);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is null) throw new ContentLoadException("content: " + path + "[" + i + "] is null");
            NullGuard.Check(items[i], "content: " + path + "[" + i + "]");
        }
        return items;
    }

    internal static T[] ReadOne<T>(IContentSource source, string path)
        where T : class
    {
        T item = Parse<T>(source, path);
        NullGuard.Check(item, "content: " + path);
        return new[] { item };
    }

    private static T Parse<T>(IContentSource source, string path)
        where T : class
    {
        ReadOnlySpan<byte> bytes = source.Read(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes.Slice(3);
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Options)
                ?? throw new ContentLoadException("content: " + path + " is JSON null");
        }
        catch (JsonException ex)
        {
            throw new ContentLoadException("content: " + path + " " + (ex.Path ?? "$") + ": " + ex.Message, ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or FormatException)
        {
            throw new ContentLoadException("content: " + path + ": " + ex.Message, ex);
        }
    }

    private static ValueMap<T> ById<T>(string kind, params T[][] parts)
        where T : IContentItem
    {
        var entries = new List<KeyValuePair<string, T>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (T item in parts.SelectMany(p => p))
        {
            if (!seen.Add(item.Id)) throw new ContentLoadException("content: duplicate " + kind + " id \"" + item.Id + "\"");
            entries.Add(new KeyValuePair<string, T>(item.Id, item));
        }
        return new ValueMap<T>(entries);
    }
}
