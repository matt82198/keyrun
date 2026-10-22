using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The C# loader must see exactly what the TS loader (content/index.ts loadContent) sees.
/// Expected counts are the TS loadContent registry sizes on main.
/// </summary>
public sealed class ContentCountTests
{
    [Fact]
    public void Loaded_counts_match_the_ts_loader()
    {
        ContentRegistry reg = ContentLoader.Load();

        Assert.Equal(65, reg.Abilities.Count); // 65 since R-SORROW-1 swapped Still the Bell for Resonance and added the five Bellfoundry-owned mob casts (was 60); 60 since D66 added the Sunken Bellfoundry's 14 casts in abilities/bellfoundry.json (was 46; 46 since R-PATROL-3b added choir.patrolSwing to abilities/patrol.json (was 45 with VAULT-KITS (S1) and its five BIBLE 9.3 vault casts in abilities/vault.json, was 40; 40 since SEED-LOCK-TEACHES added seed.lock, was 39))
        Assert.Equal(36, reg.Effects.Count); // 36 since R-SORROW-1 added moltenBrand and fannedCoals in effects/bellfoundry.json (was 34); 34 since R-BOSS-4 added cleaved in effects/vault.json and D66 added resonance, overheatedCore, slagBurn, boilingBlood and bronzeWard in effects/bellfoundry.json (was 29 with cleaved; was 28; 28 since VAULT-KITS (S1) added searingHex, cinders, stoked, rooted and drenched in effects/vault.json (was 23; 23 since R-PILE-7e added chorusEcho, Encore's echo, was 22))
        Assert.Equal(3, reg.Classes.Count);
        Assert.Equal(13, reg.Combos.Count);
        Assert.Equal(28, reg.Enemies.Count); // 28 since R-SORROW-1 added the five Bellfoundry-owned mob kinds (was 23); 23 since D66 added the Sunken Bellfoundry's five trash kinds, three bosses and two Clappers in enemies/bellfoundry.json (was 13; 13 since VAULT-KITS (S1) added the five BIBLE 9.3 vault enemies in enemies/vault.json, was 8; 8 since PATH-PACKS added hall.sentry, was 7; STATE.md D49)
        Assert.Equal(2, reg.Dungeons.Count); // 2 since D66: the Hollow Vault (tutorial) and the Sunken Bellfoundry
        Assert.Equal(11, reg.Roster.Count); // D66: the Sunken Bellfoundry's 11 roster packs, one file each under roster/
        Assert.Equal(4, reg.Clauses.Count);
        Assert.Equal(60, reg.Facets.Count);
    }

    [Fact]
    public void All_nineteen_ts_json_files_plus_run_json_layout_json_and_theme_json_are_embedded_and_each_is_listed()
    {
        var embedded = EmbeddedContentSource.Instance.Paths.OrderBy(p => p, System.StringComparer.Ordinal).ToArray();
        var loaded = ContentLoader.Files.OrderBy(p => p, System.StringComparer.Ordinal).ToArray();

        // 19 TS loadContent files from content/data plus the C#-only game/content/data/run.json, layout.json, theme.json,
        // combo-riders.json and the forces + route files forces.json and hazards.json (FORCES-ROUTE-PLAN increment 2),
        // gear.json (five gear slots, BIBLE section 7), and enemies/hall.json (the C#-only Mythic+ trash only a layout
        // pack fields, PATH-PACKS: 27 -> 28, an inventory raise for a real 8th enemy, STATE.md D49). FOLD-U3 retired
        // packs.json (R7: one pack layer, the packs live in layout.json), so 28 -> 27. melee-abilities.json (KF1 strike-cue
        // melee set, ruling R9/R10) is C#-only too, so 27 -> 28. palette.json (the render palette for telegraph and floor
        // colours, feat-telegraph-palette) is C#-only too, so 28 -> 29. VAULT-KITS (FORCES-ROUTE-PLAN slice S1, BIBLE 9.3)
        // adds the C#-only abilities/vault.json, effects/vault.json and enemies/vault.json, so 29 -> 32. R-PATROL-3b adds
        // abilities/patrol.json, so 32 -> 33. Merged history: 33 listed files, 19 TS-loaded.
        // R-LOADER-1: layout.json and forces.json became the per-kind heads layout/hollow-vault/dungeon.json and
        // forces/hollow-vault.json (still listed, so 33 stays 33); the rooms, packs, patrols and forces values are one file
        // each under ContentLoader.Directories and are loaded without a list entry, so they are not in this inventory.
        // D66 (the Sunken Bellfoundry) adds the C#-only abilities/, effects/ and enemies/bellfoundry.json and its dungeon
        // definition dungeons/sunken-bellfoundry.json, so 33 -> 37; its roster packs live under roster/ (a directory).
        string[] csharpOnly =
        {
            "run.json", "layout/hollow-vault/dungeon.json", "theme.json", "combo-riders.json", "forces/hollow-vault.json", "hazards.json",
            "gear.json", "enemies/hall.json", "melee-abilities.json", "palette.json",
            "abilities/vault.json", "effects/vault.json", "enemies/vault.json", "abilities/patrol.json",
            "abilities/bellfoundry.json", "effects/bellfoundry.json", "enemies/bellfoundry.json", "dungeons/sunken-bellfoundry.json",
        };
        bool UnderDirectory(string p) => ContentLoader.Directories.Any(d => p.StartsWith(d, System.StringComparison.Ordinal));
        string[] listedEmbedded = embedded.Where(p => !UnderDirectory(p)).ToArray();
        Assert.Equal(37, listedEmbedded.Length);
        Assert.Equal(19, listedEmbedded.Count(p => !csharpOnly.Contains(p)));
        Assert.Equal(listedEmbedded, loaded);
        Assert.Contains(embedded, UnderDirectory);
    }

    [Fact]
    public void Registry_is_keyed_by_each_item_id_in_ts_file_order()
    {
        ContentRegistry reg = ContentLoader.Load();

        foreach (var pair in reg.Abilities) Assert.Equal(pair.Key, pair.Value.Id);
        foreach (var pair in reg.Facets) Assert.Equal(pair.Key, pair.Value.Id);
        // TS order: classes warden, cinder, chanter; dungeon hollow-vault, then the C#-only sunken-bellfoundry (D66).
        Assert.Equal(new[] { "warden", "cinder", "chanter" }, reg.Classes.Keys.ToArray());
        Assert.Equal(new[] { "hollow-vault", "sunken-bellfoundry" }, reg.Dungeons.Keys.ToArray());
        Assert.Equal(24000, reg.Dungeons["sunken-bellfoundry"].ParTicks);
        Assert.Equal("warden.strike", reg.Abilities.Keys.First());
        Assert.Equal(14400, reg.Dungeons["hollow-vault"].ParTicks);
    }

    [Fact]
    public void Known_values_round_trip_from_json()
    {
        ContentRegistry reg = ContentLoader.Load();

        // The 1E telegraph reads hollowWarden.boss's cleave script step (ENGINE-PLAN section 5).
        EnemyDef boss = reg.Enemies["hollowWarden.boss"];
        Assert.Equal(27000, boss.MaxHealth); // R-BOSS-1 2026-10-21 (BossTeachesTests: a 90-180 s fight at key 2), was 5000 (STATE.md D14), was 60000
        Assert.Equal(EnemyRole.Boss, boss.Role);
        ScriptStep cleave = boss.Script.Single(s => s.Cast == "hollowWarden.cleave");
        Assert.Equal(ScriptAtKind.Loop, cleave.At.Kind);
        Assert.Equal(160, cleave.Every);
        Assert.NotNull(cleave.Telegraph);
        Assert.Equal(TelegraphShape.Cone, cleave.Telegraph!.Shape);
        Assert.Equal(4000, cleave.Telegraph.Radius);
        Assert.Equal(200, cleave.Telegraph.Angle1024);
        Assert.Equal(40, cleave.Telegraph.LeadTicks);

        Assert.Equal(LoopKind.BuilderSpender, reg.Classes["warden"].Loop.Kind);
        Assert.Equal(-1000, reg.Clauses["pact"].Params["soloDamageBp"]);
        Assert.Null(reg.Effects.Values.First(e => e.Periodic is null).Periodic);
        Assert.Contains(reg.Effects.Values, e => e.Periodic is not null);
        Assert.Contains(reg.Abilities.Values, a => a.Variants is not null && a.Variants.ContainsKey("mend"));
    }
}
