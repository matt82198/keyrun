using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// TEMPO-CAST-FLOOR (finding f-57c04b4a; BIBLE s3 "Cast bars are the enemy's language"): Tempo shrinks a step's
/// <c>every</c>, but never below its cast's cooldown or cast time. Below the cooldown the cast is refused
/// <c>not_ready</c> on the fire tick (its telegraph announced a cast that never comes, f-9b46b30c) and the real cadence
/// becomes an irregular two-period alias; below the cast time a new cast would start before the last one lands.
/// </summary>
public sealed class TempoCastFloorTests
{
    private const int TopKey = 30;

    private static Dictionary<string, (int Cooldown, int Cast)> EnemyAbilityTicks()
    {
        var path = Path.Combine(EncounterTestKit.RepoRoot(), "content", "data", "abilities", "enemies.json");
        return ((JsArray)JsValue.Parse(File.ReadAllText(path))).Items.Cast<JsObject>().ToDictionary(
            a => ((JsString)a.Get("id")!).Value,
            a => ((int)((JsInt)a.Get("cooldownTicks")!).Value, (int)((JsInt)a.Get("castTicks")!).Value));
    }

    private static List<(string EnemyId, IReadOnlyList<ScriptStep> Script)> RawScripts()
    {
        var dir = Path.Combine(EncounterTestKit.RepoRoot(), "content", "data", "enemies");
        var scripts = new List<(string, IReadOnlyList<ScriptStep>)>();
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, System.StringComparer.Ordinal))
        {
            foreach (var e in ((JsArray)JsValue.Parse(File.ReadAllText(file))).Items.Cast<JsObject>())
            {
                scripts.Add((((JsString)e.Get("id")!).Value, EncounterJson.ParseScript(e.Get("script")!)));
            }
        }
        return scripts;
    }

    [Fact]
    public void Scaled_every_never_drops_below_the_scaled_cooldown_or_cast_time_at_any_key()
    {
        var ticks = EnemyAbilityTicks();
        var checkedSteps = 0;
        var failures = new List<string>();
        foreach (var (enemyId, raw) in RawScripts())
        {
            for (var key = 1; key <= System.Math.Max(TopKey, ScreenState.MaxKey); key++)
            {
                var k = key;
                var scaled = Tempo.ScaleScript(raw, key, id => Tempo.ScaledCastFloorTicks(ticks[id].Cooldown, ticks[id].Cast, k));
                Assert.Equal(raw.Count, scaled.Count);
                foreach (var step in scaled.Where(s => s.Every is not null))
                {
                    var (cooldown, cast) = ticks[step.Cast];
                    var scaledCooldown = Tempo.ScaleCooldownTicks(cooldown, key);
                    checkedSteps++;
                    if (step.Every < scaledCooldown || step.Every < cast || step.Every < Tempo.EveryFloorTicks)
                    {
                        failures.Add($"{enemyId} {step.Cast} key {key}: every {step.Every} < scaled cooldown {scaledCooldown} / cast {cast} / 20");
                    }
                }
            }
        }
        Assert.True(checkedSteps >= 12 * TopKey, $"only {checkedSteps} steps checked");
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)) + $"\n({failures.Count} total)");
    }

    [Fact]
    public void Casts_still_speed_up_with_the_key_the_cleave_loop_at_key_10_is_faster_than_at_key_1()
    {
        // BIBLE s8: "enemies cast more often". Cleave is authored every 160 == cooldownTicks 160 (castTicks 40).
        var ticks = EnemyAbilityTicks();
        var (cooldown, cast) = ticks["hollowWarden.cleave"];
        var cleave = ScriptStep.Loop(160, "hollowWarden.cleave");
        var key1 = Tempo.ScaleStep(cleave, 1, _ => Tempo.ScaledCastFloorTicks(cooldown, cast, 1)).Every!.Value;
        var key10 = Tempo.ScaleStep(cleave, 10, _ => Tempo.ScaledCastFloorTicks(cooldown, cast, 10)).Every!.Value;
        Assert.Equal(160, key1);
        Assert.True(key10 < key1, $"key 10 cleave every {key10} is not faster than key 1 every {key1}");
        Assert.Equal(135, key10); // 160 * 10000 / 11800 = 135, and the cooldown scales to the same 135.
        Assert.Equal(key10, Tempo.ScaleCooldownTicks(cooldown, 10));
    }

    [Fact]
    public void Cooldown_scales_by_the_tempo_factor_with_no_floor()
    {
        Assert.Equal(160, Tempo.ScaleCooldownTicks(160, 1));
        Assert.Equal(156, Tempo.ScaleCooldownTicks(160, 2));
        Assert.Equal(Tempo.ScaleCastEvery(160, 15), Tempo.ScaleCooldownTicks(160, 15));
        Assert.Equal(0, Tempo.ScaleCooldownTicks(0, 20));
        // Below the 20-tick every floor the cooldown is not raised: it only ever gets shorter.
        Assert.Equal(15, Tempo.ScaleCooldownTicks(20, 15)); // 20 * 10000 / 12800
    }

    [Fact]
    public void ScaleEnemyCooldowns_scales_only_scripted_casts_and_the_scaled_loop_finds_its_cast_ready_every_fire()
    {
        var cleave = RunTestKit.Ability("hollowWarden.cleave", System.Array.Empty<Keyrun.Sim.Combat.EffectOp>(), cooldownTicks: 160);
        var kick = RunTestKit.Ability("warden.kick", System.Array.Empty<Keyrun.Sim.Combat.EffectOp>(), cooldownTicks: 480);
        var table = new Dictionary<string, Keyrun.Sim.Combat.AbilityDef> { [cleave.Id] = cleave, [kick.Id] = kick };
        var script = new[] { ScriptStep.Loop(160, cleave.Id) };
        const int key = 10;

        var scaledTable = Tempo.ScaleEnemyCooldowns(table, new[] { (IReadOnlyList<ScriptStep>)script }, key);
        Assert.Equal(135, scaledTable[cleave.Id].CooldownTicks);
        Assert.Equal(480, scaledTable[kick.Id].CooldownTicks); // a party ability is not an enemy cast
        Assert.Equal(160, table[cleave.Id].CooldownTicks); // input untouched

        // Drive the real cooldown rules: the loop fires at every, 2*every, ... and each cast must be ready.
        var every = Tempo.ScaleScript(script, key, _ => Tempo.ScaledCastFloorTicks(160, 0, key))[0].Every!.Value;
        Assert.True(every < 160);
        var actor = CombatTestKit.Actor(2);
        var def = scaledTable[cleave.Id];
        for (var fire = every; fire <= every * 6; fire += every)
        {
            Assert.True(Keyrun.Sim.Combat.Cooldowns.IsAbilityReady(actor, def, fire), $"cast not ready on fire tick {fire}");
            Keyrun.Sim.Combat.Cooldowns.ConsumeCharge(actor, def, fire);
        }
    }

    [Fact]
    public void Cast_floor_is_the_larger_of_cooldown_and_cast_time()
    {
        Assert.Equal(160, Tempo.CastFloorTicks(160, 40));
        Assert.Equal(80, Tempo.CastFloorTicks(0, 80));
        Assert.Equal(0, Tempo.CastFloorTicks(0, 0));
    }

    [Fact]
    public void ScaleCastEvery_clamps_to_the_floor_only_when_tempo_would_go_below_it()
    {
        // Key 2 Cleave: 160 * 10000 / 10200 = 156, below its 160 cooldown -> 160.
        Assert.Equal(160, Tempo.ScaleCastEvery(160, 2, 160));
        // Key 10 boss slam: 300 * 10000 / 11800 = 254, above its 60 cooldown -> unchanged.
        Assert.Equal(254, Tempo.ScaleCastEvery(300, 10, 60));
        Assert.Equal(Tempo.ScaleCastEvery(300, 10), Tempo.ScaleCastEvery(300, 10, 60));
        // The 20-tick floor still holds with no cast floor.
        Assert.Equal(Tempo.EveryFloorTicks, Tempo.ScaleCastEvery(40, 100, 0));
        // A cast time longer than the scaled period wins too.
        Assert.Equal(80, Tempo.ScaleCastEvery(70, 5, Tempo.CastFloorTicks(0, 80)));
    }

    [Fact]
    public void ScaleStep_with_a_floor_keeps_trigger_cast_and_telegraph_and_clamps_only_every()
    {
        var loop = ScriptStep.Loop(160, "hollowWarden.cleave") with
        {
            Telegraph = new TelegraphSpec("cone", 40) { Radius = 4000, Angle1024 = 200 },
        };
        Assert.Equal(ScriptStep.Loop(160, "hollowWarden.cleave") with
        {
            Telegraph = new TelegraphSpec("cone", 39) { Radius = 4000, Angle1024 = 200 },
        }, Tempo.ScaleStep(loop, 2, _ => 160));

        // A numeric at with every keeps its tick; its period is clamped. No every stays no every.
        Assert.Equal(ScriptStep.At(50, "seed.lash", 200), Tempo.ScaleStep(ScriptStep.At(50, "seed.lash", 200), 2, _ => 200));
        Assert.Equal(ScriptStep.At(250, "r"), Tempo.ScaleStep(ScriptStep.At(250, "r"), 10, _ => 500));
        Assert.Equal(ScriptStep.OnDeath("seed.deathBloom"), Tempo.ScaleStep(ScriptStep.OnDeath("seed.deathBloom"), 10, _ => 500));
        // A loop with no every scales the 20-tick default, then the floor applies.
        Assert.Equal(ScriptStep.Loop(50, "x"), Tempo.ScaleStep(ScriptStep.Loop(null, "x"), 2, _ => 50));
        // Above the floor nothing changes versus the TS-parity overload.
        Assert.Equal(Tempo.ScaleStep(ScriptStep.Loop(300, "s"), 10), Tempo.ScaleStep(ScriptStep.Loop(300, "s"), 10, _ => 60));
    }
}
