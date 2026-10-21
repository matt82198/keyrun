using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keyrun.Content;

/// <summary>
/// Loads every content kind from the verbatim content/data JSON into a typed, validated
/// <see cref="ContentRegistry"/>: the C# mirror of content/index.ts <c>loadContent()</c>, reading
/// the same 19 files in the same order, plus the C#-only run.json, layout.json, theme.json (theme.json is read by ThemeLoader), combo-riders.json
/// and the forces + route files (forces.json, hazards.json; packs live in layout.json, ruling R7), and the trash enemies (enemies/hall.json). Fails closed with <see cref="ContentLoadException"/> on
/// any unknown property, missing required field, null in a non-optional field, non-integer
/// number, unknown enum or op, duplicate id, unresolved cross-reference, or unlisted/missing file.
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
        ComboRidersFile,
        ForcesFile,
        HazardsFile,
        GearFile,
        TrashEnemiesFile,
        MeleeAbilitiesFile,
    };

    /// <summary>
    /// C#-only melee ability ids (game/content/data/melee-abilities.json, same logical root; KF1 strike cue, ruling R9).
    /// The root abilities JSON is a frozen oracle and is not edited to tag melee, so the list lives here.
    /// </summary>
    public const string MeleeAbilitiesFile = "melee-abilities.json";

    /// <summary>
    /// C#-only trash enemies (game/content/data/enemies/hall.json, same logical root; PATH-PACKS): enemies only a layout
    /// pack fields, never a teaching pull, so the frozen TS loadContent has no counterpart. Today the Hall Sentry
    /// (<c>hall.sentry</c>), the Mythic+ trash the required Hall Sentries pack is built from.
    /// </summary>
    public const string TrashEnemiesFile = Enemies + "hall.json";

    /// <summary>C#-only gear table (game/content/data/gear.json; BIBLE section 7 "Five slots", "Tier is power").</summary>
    public const string GearFile = "gear.json";

    /// <summary>
    /// C#-only forces table and route (game/content/data/forces.json; BIBLE section 9.2): the bar's size, every
    /// enemy's points and the fork and rejoin pulls.
    /// </summary>
    public const string ForcesFile = "forces.json";

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
    /// C#-only dungeon layout (game/content/data/layout.json, same logical root): rooms, corridors, pull triggers,
    /// gates and the route (STATE.md D18). Not read by the frozen TS loadContent.
    /// </summary>
    public const string LayoutFile = "layout.json";

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
                ReadArray<AbilityDef>(source, Abilities + "enemies.json")),
            Effects = ById("effect",
                ReadArray<EffectDef>(source, Effects + "player.json"),
                ReadArray<EffectDef>(source, Effects + "enemy.json")),
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
                ReadArray<EnemyDef>(source, TrashEnemiesFile)),
            Dungeons = ById("dungeon", ReadOne<DungeonDef>(source, "dungeons/hollow-vault.json")),
            Clauses = ById("clause", ReadArray<ClauseDef>(source, "clauses.json")),
            Facets = ById("facet",
                ReadArray<FacetDef>(source, Facets + "warden.json"),
                ReadArray<FacetDef>(source, Facets + "cinder.json"),
                ReadArray<FacetDef>(source, Facets + "chanter.json")),
            Run = ReadOne<RunConfig>(source, RunFile)[0],
            Layout = ReadOne<LayoutDef>(source, LayoutFile)[0],
            ComboRiders = ById("combo rider", ReadArray<ComboRider>(source, ComboRidersFile)),
            Forces = ReadOne<ForcesTable>(source, ForcesFile)[0],
            Hazards = ById("hazard", ReadArray<HazardDef>(source, HazardsFile)),
            Gear = ById("gear item", ReadArray<GearItemDef>(source, GearFile)),
            MeleeAbilities = new ValueList<string>(ReadArray<string>(source, MeleeAbilitiesFile)),
        };

        ContentValidator.Validate(registry);
        return registry;
    }

    private static void CheckFileSet(IContentSource source)
    {
        var present = new HashSet<string>(source.Paths.Where(p => p.EndsWith(".json", StringComparison.Ordinal)), StringComparer.Ordinal);
        foreach (string file in Files)
            if (!present.Contains(file)) throw new ContentLoadException("content: missing file \"" + file + "\"");
        foreach (string file in present.OrderBy(p => p, StringComparer.Ordinal))
            if (!Files.Contains(file, StringComparer.Ordinal))
                throw new ContentLoadException("content: unlisted file \"" + file + "\" (add it to ContentLoader.Files and content/index.ts)");
    }

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
