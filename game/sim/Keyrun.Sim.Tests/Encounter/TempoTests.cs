using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// tests/sim/encounter/tempo.test.ts ported 1:1, plus oracle parity: the scripts and spawn health the frozen TS
/// put into the key-2 and key-10 Hollow Vault setups (client/src/sim-host/content-dungeon.ts:48-63 applied to
/// content/data/enemies/*.json) must equal <see cref="Tempo.ScaleScript"/> and <see cref="Tempo.ScaleHealth"/>
/// over the same raw content.
/// </summary>
public sealed class TempoTests
{
    [Fact]
    public void Key_1_applies_no_scaling()
    {
        Assert.Equal(Tempo.BpDenom, Tempo.TempoMultiplierBp(1));
        Assert.Equal(100, Tempo.ScaleCastEvery(100, 1));
        Assert.Equal(1000, Tempo.ScaleHealth(1000, 1));
    }

    [Fact]
    public void Key_15_shrinks_every_by_the_documented_basis_point_formula()
    {
        var mult = Tempo.BpDenom + (Tempo.TempoBpPerKey * 14);
        var expected = System.Math.Max(Tempo.EveryFloorTicks, (100 * Tempo.BpDenom) / mult);
        Assert.Equal(expected, Tempo.ScaleCastEvery(100, 15));
        Assert.True(Tempo.ScaleCastEvery(100, 15) < 100);
        Assert.Equal(78, Tempo.ScaleCastEvery(100, 15));
    }

    [Fact]
    public void Every_never_shrinks_below_its_documented_floor_however_high_the_key() =>
        Assert.Equal(Tempo.EveryFloorTicks, Tempo.ScaleCastEvery(40, 100));

    [Fact]
    public void Key_15_shrinks_telegraph_leadTicks_by_the_documented_basis_point_formula()
    {
        var shrinkBp = Tempo.LeadShrinkBpPerKey * 14;
        var expected = System.Math.Max(Tempo.LeadFloorTicks, (40 * (Tempo.BpDenom - shrinkBp)) / Tempo.BpDenom);
        Assert.Equal(expected, Tempo.ScaleLeadTicks(40, 15));
        Assert.True(Tempo.ScaleLeadTicks(40, 15) < 40);
    }

    [Fact]
    public void LeadTicks_never_shrinks_below_its_documented_floor_however_high_the_key() =>
        Assert.Equal(Tempo.LeadFloorTicks, Tempo.ScaleLeadTicks(40, 60));

    [Fact]
    public void Health_scales_gently_by_the_documented_basis_point_formula()
    {
        var bp = Tempo.BpDenom + (Tempo.HealthBpPerKey * 9);
        Assert.Equal((1000 * bp) / Tempo.BpDenom, Tempo.ScaleHealth(1000, 10));
    }

    [Fact]
    public void Fortified_and_Tyrannical_supersede_the_key_5_10_15_mechanic_unlocks()
    {
        // Plan M5: the extra mechanic at keys 5, 10 and 15 is gone; from key 5 one affix rotates, from key 10 both.
        var table = AffixesTests.BibleTable;
        Assert.Equal(KeyAffixes.None, Affixes.AffixesFor(table, 4, 0));
        Assert.Single(Affixes.AffixesFor(table, 5, 0).Names);
        Assert.Single(Affixes.AffixesFor(table, 9, 1).Names);
        Assert.Equal(2, Affixes.AffixesFor(table, 10, 0).Names.Count);
        Assert.Equal(2, Affixes.AffixesFor(table, 15, 1).Names.Count);
        Assert.Equal(2, Affixes.AffixesFor(table, 20, 0).Names.Count);
    }

    [Fact]
    public void ScaleStep_keeps_trigger_and_cast_and_scales_every_and_lead()
    {
        var loop = ScriptStep.Loop(160, "hollowWarden.cleave") with
        {
            Telegraph = new TelegraphSpec("cone", 40) { Radius = 4000, Angle1024 = 200 },
        };
        var scaled = Tempo.ScaleStep(loop, 2);
        Assert.Equal(ScriptStep.Loop(156, "hollowWarden.cleave") with
        {
            Telegraph = new TelegraphSpec("cone", 39) { Radius = 4000, Angle1024 = 200 },
        }, scaled);

        // content-dungeon.ts:51: a loop with no every scales the floor (20), not 0.
        Assert.Equal(ScriptStep.Loop(20, "x"), Tempo.ScaleStep(ScriptStep.Loop(null, "x"), 2));
        // :53: a numeric at keeps at and scales every only when present; onDeath is untouched.
        Assert.Equal(ScriptStep.At(600, "s", 392), Tempo.ScaleStep(ScriptStep.At(600, "s", 400), 2));
        Assert.Equal(ScriptStep.At(250, "r"), Tempo.ScaleStep(ScriptStep.At(250, "r"), 10));
        Assert.Equal(ScriptStep.OnDeath("seed.deathBloom"), Tempo.ScaleStep(ScriptStep.OnDeath("seed.deathBloom"), 10));
    }

    private static Dictionary<string, JsObject> RawEnemies()
    {
        var dir = Path.Combine(EncounterTestKit.RepoRoot(), "content", "data", "enemies");
        var byId = new Dictionary<string, JsObject>();
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, System.StringComparer.Ordinal))
        {
            foreach (var e in ((JsArray)JsValue.Parse(File.ReadAllText(file))).Items.Cast<JsObject>())
            {
                byId.Add(((JsString)e.Get("id")!).Value, e);
            }
        }
        return byId;
    }

    [Theory]
    [InlineData("hollow-vault-k2.setup.json", 2)]
    [InlineData("hollow-vault-k10.setup.json", 10)]
    public void Oracle_setup_scripts_and_health_equal_tempo_scaled_raw_content(string setupFile, int keyLevel)
    {
        var setup = EncounterTestKit.Setup(setupFile);
        var content = (JsObject)setup.Get("content")!;
        Assert.Equal(keyLevel, ((JsInt)content.Get("keyLevel")!).Value);
        var dungeon = EncounterJson.ParseDungeon(setup.Get("dungeon")!);
        var scripts = EncounterJson.ParseScripts(content.Get("enemyScripts")!);
        var raw = RawEnemies();

        var compared = 0;
        foreach (var spawn in dungeon.Pulls.SelectMany(p => p.Spawns))
        {
            var def = raw[spawn.ClassId];
            var rawScript = EncounterJson.ParseScript(def.Get("script")!);
            Assert.Equal(Tempo.ScaleScript(rawScript, keyLevel), scripts[spawn.ActorId]);
            Assert.Equal(Tempo.ScaleHealth(((JsInt)def.Get("maxHealth")!).Value, keyLevel), spawn.Health);
            compared++;
        }
        Assert.Equal(10, compared); // 3 choir + 5 pair + seed + boss
        Assert.Equal(scripts.Count, compared);
    }

    [Theory]
    [InlineData("content-pull", 1)]
    [InlineData("scripted-pull", 1)]
    [InlineData("bots-hv-k2-s7", 2)]
    [InlineData("bots-hv-k10-s7", 10)]
    public void Oracle_initial_slack_is_the_key_level_bank(string trace, int keyLevel)
    {
        var first = OracleFixtures.LoadTrace(trace).RecordLines[0];
        var p = System.Text.Json.JsonDocument.Parse(first).RootElement.GetProperty("p").GetString()!;
        var slack = p.Split(';').Single(f => f.StartsWith("slack=", System.StringComparison.Ordinal))["slack=".Length..];
        Assert.Equal(Timer.InitialSlackTicks(keyLevel).ToString(System.Globalization.CultureInfo.InvariantCulture), slack);
    }
}
