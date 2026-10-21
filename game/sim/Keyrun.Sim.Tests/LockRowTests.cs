using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using static Keyrun.Sim.Tests.BossTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// LOCK-ROW (finding f-8566b4f0; BIBLE s3 "Cast bars are the enemy's language ... Shape plus colour, never colour
/// alone"; BIBLE s9 "The Lock"; ruling R-LOCK-1): during the Hollow Warden's Lock the HUD showed only the purple floor
/// ring and the Twins' "BREAK 0%", so a player could not read whether the Lock was being cast, was open or was broken,
/// nor what failing it costs. The boss frame's Lock row (<see cref="LockRowTracker"/>, fed the sim's events) names the
/// state in words and the Tempo on the line from the R-LOCK-1 ladder (11666, 13333, 15000 bp), and clears once the
/// Lock has resolved.
/// </summary>
public sealed class LockRowTests
{
    private const int Flash = LockRowTracker.DefaultFlashTicks;

    private static SimEvent Cast(int tick, int index) => new(tick, "lock_cast",
        ("actorId", JsValue.Int(Boss)), ("lockIndex", JsValue.Int(index)), ("landsTick", JsValue.Int(tick + 20)));

    private static SimEvent Open(int tick, int index) => new(tick, "lock_open",
        ("actorId", JsValue.Int(Boss)), ("lockIndex", JsValue.Int(index)), ("endsTick", JsValue.Int(tick + 60)));

    private static SimEvent Broken(int tick, int index) => new(tick, "lock_broken",
        ("actorId", JsValue.Int(Boss)), ("lockIndex", JsValue.Int(index)));

    private static SimEvent Failed(int tick, int index, int fails, int tempoBp) => new(tick, "lock_failed",
        ("actorId", JsValue.Int(Boss)), ("lockIndex", JsValue.Int(index)), ("fails", JsValue.Int(fails)), ("tempoBp", JsValue.Int(tempoBp)));

    private static SimEvent Enrage(int tick) => new(tick, "boss_enrage", ("actorId", JsValue.Int(Boss)), ("tempoBp", JsValue.Int(15000)));

    /// <summary>Feeds each event on its own tick (as SimDriver.Stepped does after the step) and reads the row on every state tick.</summary>
    private static Dictionary<int, BossLockRow?> Feed(IEnumerable<SimEvent> events, int untilTick)
    {
        var tracker = new LockRowTracker();
        var byTick = events.GroupBy(e => e.Tick).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new Dictionary<int, BossLockRow?>();
        for (var tick = 0; tick < untilTick; tick++)
        {
            tracker.Observe(byTick.TryGetValue(tick, out var step) ? step : new List<SimEvent>());
            // The events of the step from tick T are read when the state is at T + 1.
            rows[tick + 1] = tracker.Row(Config(), tick + 1);
        }
        return rows;
    }

    [Fact(DisplayName = "Lock row: cast, open and broken each name the state and the R-LOCK-1 stake, then the row clears")]
    public void Cast_open_broken_names_each_state_and_the_stake_then_clears()
    {
        // The D15 capture's run log: lock_cast 305, lock_open 325 (ends 385), lock_broken 364 (steam combo).
        var rows = Feed(new[] { Cast(305, 1), Open(325, 1), Broken(364, 1) }, 420);

        Assert.Null(rows[305]);
        for (var t = 306; t <= 325; t++)
        {
            var row = rows[t];
            Assert.True(row is not null, $"no Lock row at tick {t} while the boss casts the Lock");
            Assert.Equal(LockRowState.Casting, row!.State);
            Assert.StartsWith("LOCK casting", row.Text, StringComparison.Ordinal);
            Assert.Equal(1, row.LockIndex);
            Assert.Equal(11666, row.StakeTempoBp);
            Assert.Equal(1, row.Stacks);
            Assert.Equal(3, row.MaxStacks);
            Assert.Contains("Tempo +16%", row.Text, StringComparison.Ordinal);
        }
        for (var t = 326; t <= 364; t++)
        {
            var row = rows[t];
            Assert.True(row is not null, $"no Lock row at tick {t} while the Lock is open");
            Assert.Equal(LockRowState.Open, row!.State);
            Assert.StartsWith("LOCK open", row.Text, StringComparison.Ordinal);
            Assert.Equal(11666, row.StakeTempoBp);
            Assert.Contains("Tempo +16%", row.Text, StringComparison.Ordinal);
            Assert.Contains("x1/3", row.Text, StringComparison.Ordinal);
        }
        // The ring's green flash: LockView reads ticks since the lock_broken tick, [0, 30).
        for (var t = 365; t < 364 + Flash; t++)
        {
            var row = rows[t];
            Assert.True(row is not null, $"no Lock row at tick {t} just after the Lock broke");
            Assert.Equal(LockRowState.Broken, row!.State);
            Assert.StartsWith("LOCK broken", row.Text, StringComparison.Ordinal);
            Assert.Equal(11666, row.StakeTempoBp);
            Assert.Contains("Tempo +16%", row.Text, StringComparison.Ordinal);
        }
        for (var t = 364 + Flash; t <= 420; t++)
        {
            Assert.True(rows[t] is null, $"the Lock row is still up at tick {t}, after the Lock resolved: {rows[t]?.Text}");
        }
    }

    [Fact(DisplayName = "Lock row: the stake climbs the R-LOCK-1 ladder 11666, 13333, 15000 and holds at x3/3 once enraged")]
    public void Stake_climbs_the_ladder_and_holds_once_enraged()
    {
        var events = new List<SimEvent>
        {
            Open(0, 1), Failed(60, 1, 1, 11666),
            Cast(580, 2), Open(600, 2), Failed(660, 2, 2, 13333),
            Cast(1180, 3), Open(1200, 3), Failed(1260, 3, 3, 15000), Enrage(1260),
            Cast(1780, 4), Open(1800, 4),
        };
        var rows = Feed(events, 1830);

        Assert.Equal((LockRowState.Open, 11666, 1), Of(rows[30]));
        Assert.Equal((LockRowState.Failed, 11666, 1), Of(rows[61]));
        Assert.Contains("LOCK failed", rows[61]!.Text, StringComparison.Ordinal);
        Assert.Equal((LockRowState.Failed, 11666, 1), Of(rows[59 + Flash]));
        Assert.Null(rows[60 + Flash]);
        Assert.Equal((LockRowState.Casting, 13333, 2), Of(rows[590]));
        Assert.Contains("Tempo +33%", rows[590]!.Text, StringComparison.Ordinal);
        Assert.Equal((LockRowState.Open, 13333, 2), Of(rows[630]));
        Assert.Equal((LockRowState.Failed, 13333, 2), Of(rows[661]));
        Assert.Equal((LockRowState.Open, 15000, 3), Of(rows[1230]));
        Assert.Contains("Tempo +50%", rows[1230]!.Text, StringComparison.Ordinal);
        Assert.Equal((LockRowState.Failed, 15000, 3), Of(rows[1261]));
        // Enraged, the dim red ring stays down under everything: the row names it, never the colour alone.
        Assert.Equal((LockRowState.Enraged, 15000, 3), Of(rows[1260 + Flash]));
        Assert.StartsWith("LOCK enraged", rows[1260 + Flash]!.Text, StringComparison.Ordinal);
        Assert.Equal((LockRowState.Enraged, 15000, 3), Of(rows[1780]));
        // A fourth Lock stays x3/3 (the enrage is the ceiling).
        Assert.Equal((LockRowState.Casting, 15000, 3), Of(rows[1790]));
        Assert.Equal((LockRowState.Open, 15000, 3), Of(rows[1810]));
        Assert.Contains("x3/3", rows[1810]!.Text, StringComparison.Ordinal);
    }

    private static (LockRowState, int, int)? Of(BossLockRow? row) =>
        row is null ? null : (row.State, row.StakeTempoBp, row.Stacks);

    private static IReadOnlyList<ScriptStep> ContentBossScript()
    {
        var path = Path.Combine(ContentRoots.RepoRootAbove(OracleFixtures.Directory), "content", "data", "enemies", "hollow-warden.json");
        var boss = ((JsArray)JsValue.Parse(File.ReadAllText(path))).Items.Cast<JsObject>()
            .Single(e => ((JsString)e.Get("id")!).Value == "hollowWarden.boss");
        return Tempo.ScaleScript(EncounterJson.ParseScript(boss.Get("script")!), 2);
    }

    [Fact(DisplayName = "Lock row: whenever the Lock ring is drawn the row is present and names the ring's state in words")]
    public void Row_is_present_in_words_whenever_the_ring_is_drawn()
    {
        // The real armed run with the Lock cast (as the host arms it), boss at 50% and no party play: every Lock is cast,
        // opens and fails, and the third fail enrages the boss.
        var run = BuildRun(bossHealth: 30000, armed: false);
        run = run with
        {
            Content = run.Content with { EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Boss] = ContentBossScript() } },
        };
        run = run with { Encounter = BossRules.Arm(run.Encounter, Config() with { CastLock = true }) };

        var tracker = new LockRowTracker();
        int brokenTick = -1;
        int failedTick = -1;
        var seen = new HashSet<LockRowState>();
        while (run.State.Tick < 1400)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            tracker.Observe(stepped.Events);
            foreach (var e in stepped.Events)
            {
                brokenTick = e.Type == "lock_broken" ? e.Tick : brokenTick;
                failedTick = e.Type == "lock_failed" ? e.Tick : failedTick;
            }
            var boss = run.Encounter.Boss!;
            var tick = run.State.Tick;
            var row = tracker.Row(boss.Config, tick);

            // The ring as src/View/LockView.cs draws it (LockRule.For: open, then a broken flash, then a failed flash,
            // then the enrage ring; each flash lasts LockRule.FlashTicks = 30).
            LockRowState? ring = boss.Active is not null ? LockRowState.Open
                : boss.LocksBroken > 0 && InFlash(tick, brokenTick) ? LockRowState.Broken
                : boss.LockFails > 0 && InFlash(tick, failedTick) ? LockRowState.Failed
                : boss.Enraged ? LockRowState.Enraged
                : null;
            if (boss.Casting is not null)
            {
                Assert.True(row is { State: LockRowState.Casting }, $"tick {tick}: the boss casts the Lock and the row reads {row?.Text ?? "nothing"}");
            }
            if (ring is { } drawn)
            {
                Assert.True(row is not null, $"tick {tick}: the Lock ring is drawn ({drawn}) with no Lock row: colour alone");
                Assert.Equal(drawn, row!.State);
                Assert.StartsWith("LOCK " + drawn.ToString().ToLowerInvariant(), row.Text, StringComparison.Ordinal);
                Assert.Contains("Tempo +", row.Text, StringComparison.Ordinal);
            }
            if (row is not null)
            {
                seen.Add(row.State);
            }
        }
        Assert.Equal(new[] { LockRowState.Casting, LockRowState.Open, LockRowState.Failed, LockRowState.Enraged }, seen.OrderBy(s => s).ToArray());
    }

    private static bool InFlash(int tick, int at) => at >= 0 && tick - at >= 0 && tick - at < Flash;
}
