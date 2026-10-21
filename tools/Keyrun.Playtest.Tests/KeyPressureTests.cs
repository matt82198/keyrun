using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// A higher key is never easier on the tank (design/BIBLE.md section 0, "keys that are genuinely hard because of what
/// enemies do"; section 8, the key scales the pace). A Warden built exactly as the host builds it
/// (HostRunSetup.Build, real content), standing alone in the Choir pull with no healer and Brace pressed whenever
/// ready, must die no later at key N+1 than at key N. Before f-57c04b4a's fix the Tempo-scaled swing loop fired
/// before the swing's cooldown, so every other swing was refused and time-to-death rose with the key.
/// </summary>
public sealed class KeyPressureTests
{
    private const int MaxTicks = 4000;

    /// <summary>The tick the lone Warden dies at <paramref name="key"/> (null if alive at <see cref="MaxTicks"/>) and the health it lost.</summary>
    private static (int? DeathTick, long Taken) TimeToDeath(ContentRegistry registry, int key)
    {
        var setup = HostRunSetup.Build(registry, "hollow-vault", key, "warden", withBots: false);
        var choir = setup.Dungeon.Pulls[0];
        var acolyte = choir.Spawns.First(s => s.ClassId == "choir.melee");
        var party = setup.Party.Select(p => p with { Pos = acolyte.Pos }).ToList();
        var run = RunRoot.CreateRun(1, party, setup.Dungeon, setup.Content with { Layout = null }, null);
        var warden = party.Single(p => p.ClassId == "warden").Id;
        long taken = 0;
        while (run.State.Tick < MaxTicks)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            if (Readiness.AbilityReady(run.State.Actors[warden], "warden.brace", tick))
            {
                commands.Add(new CastCommand(tick, warden, "warden.brace"));
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            taken += stepped.Events
                .Where(e => e.Type == "damage" && e.Get("targetId") is JsInt t && t.Value == warden)
                .Sum(e => e.Get("healthDamage") is JsInt hd ? hd.Value : 0);
            if (!run.State.Actors[warden].Alive)
            {
                return (tick, taken);
            }
        }
        return (null, taken);
    }

    [Fact(DisplayName = "Key 1 to 10: a lone unhealed Warden in the Choir pull dies no later as the key rises")]
    public void Time_to_death_is_non_increasing_from_key_1_to_key_10()
    {
        var registry = ContentLoader.Load();
        var results = Enumerable.Range(1, 10).Select(k => (Key: k, Result: TimeToDeath(registry, k))).ToList();
        var table = string.Join(", ", results.Select(r => $"key {r.Key}: tick {r.Result.DeathTick?.ToString() ?? "alive"}"));
        foreach (var (key, result) in results)
        {
            Assert.True(result.Taken > 0, $"the Choir never hit the Warden at key {key}: the test is vacuous ({table})");
            Assert.True(result.DeathTick is not null, $"the Warden outlived {MaxTicks} ticks at key {key} ({table})");
        }
        for (var i = 1; i < results.Count; i++)
        {
            Assert.True(
                results[i].Result.DeathTick <= results[i - 1].Result.DeathTick,
                $"key {results[i].Key} is easier on the tank than key {results[i - 1].Key}: {table}");
        }
        Assert.True(results[^1].Result.DeathTick < results[0].Result.DeathTick, $"key 10 is no harder than key 1: {table}");
    }
}
