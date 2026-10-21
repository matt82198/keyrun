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

        Assert.Equal(40, reg.Abilities.Count); // 40 since SEED-LOCK-TEACHES added seed.lock (was 39)
        Assert.Equal(23, reg.Effects.Count); // 23 since R-PILE-7e added chorusEcho (Encore's echo; was 22)
        Assert.Equal(3, reg.Classes.Count);
        Assert.Equal(13, reg.Combos.Count);
        Assert.Equal(8, reg.Enemies.Count); // 8 since PATH-PACKS added hall.sentry (was 7; STATE.md D49)
        Assert.Single(reg.Dungeons);
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
        // melee set, ruling R9/R10) is C#-only too, so 27 -> 28.
        string[] csharpOnly =
        {
            "run.json", "layout.json", "theme.json", "combo-riders.json", "forces.json", "hazards.json",
            "gear.json", "enemies/hall.json", "melee-abilities.json",
        };
        Assert.Equal(28, embedded.Length);
        Assert.Equal(19, embedded.Count(p => !csharpOnly.Contains(p)));
        Assert.Equal(embedded, loaded);
    }

    [Fact]
    public void Registry_is_keyed_by_each_item_id_in_ts_file_order()
    {
        ContentRegistry reg = ContentLoader.Load();

        foreach (var pair in reg.Abilities) Assert.Equal(pair.Key, pair.Value.Id);
        foreach (var pair in reg.Facets) Assert.Equal(pair.Key, pair.Value.Id);
        // TS order: classes warden, cinder, chanter; dungeon hollow-vault.
        Assert.Equal(new[] { "warden", "cinder", "chanter" }, reg.Classes.Keys.ToArray());
        Assert.Equal(new[] { "hollow-vault" }, reg.Dungeons.Keys.ToArray());
        Assert.Equal("warden.strike", reg.Abilities.Keys.First());
        Assert.Equal(14400, reg.Dungeons["hollow-vault"].ParTicks);
    }

    [Fact]
    public void Known_values_round_trip_from_json()
    {
        ContentRegistry reg = ContentLoader.Load();

        // The 1E telegraph reads hollowWarden.boss's cleave script step (ENGINE-PLAN section 5).
        EnemyDef boss = reg.Enemies["hollowWarden.boss"];
        Assert.Equal(5000, boss.MaxHealth); // tuned 2026-10-18 per Matt (STATE.md D14), was 60000
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
