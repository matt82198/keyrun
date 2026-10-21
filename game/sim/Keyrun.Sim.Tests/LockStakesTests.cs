using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.BossTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// LOCK-STAKES (finding f-c25cb122, ruling R-LOCK-1, BIBLE s9 "The Lock"): a failed Lock costs the party at once and
/// visibly, as WoW does with a stacking boss buff. Each unbroken Lock adds one Tempo stack to the boss: its loops re-pace
/// to <c>10000 + (EnrageTempoBp - 10000) * stacks / FailsToEnrage</c> bp (11666, 13333, then 15000 = the enrage, which
/// stays the ceiling); <c>lock_failed</c> carries that new effective tempo; the boss frame shows <c>LOCK xN/3</c>.
/// </summary>
public sealed class LockStakesTests
{
    [Fact(DisplayName = "Lock stakes: one failed Lock speeds the boss's casts at once and lock_failed carries the new tempo")]
    public void One_failed_lock_speeds_the_boss_at_once()
    {
        // BuildRun(30000) opens Lock 1 on tick 0; it fails on tick 60 (BossRulesTests.Lock_fails_at_window_end_and_repeats).
        var (beforeFail, _) = StepUntil(BuildRun(bossHealth: 30000), 59);
        var scripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Boss] = beforeFail.Content.EnemyScripts[Boss] };
        var calm = BossRules.EffectiveScripts(beforeFail.Encounter.Boss!, scripts)[Boss].Select(s => s.Every).ToArray();

        var (afterFail, events) = StepUntil(beforeFail, 400);
        var failed = Assert.Single(events, e => e.Type == "lock_failed");
        Assert.Equal(60, failed.Tick);
        Assert.Equal(1, failed.IntField("fails"));
        // 10000 + (15000 - 10000) * 1 / 3 = 11666 (integer division).
        Assert.Equal(11666, failed.IntField("tempoBp"));
        Assert.DoesNotContain(events, e => e.Type == "boss_enrage");
        Assert.False(afterFail.Encounter.Boss!.Enraged);

        // The sim state changed: the stepped boss state re-paces the boss's loops. slam 294 -> trunc(294*10000/11666) =
        // 252, cleave 156 -> trunc(156*10000/11666) = 133.
        var stacked = BossRules.EffectiveScripts(afterFail.Encounter.Boss!, scripts)[Boss].Select(s => s.Every).ToArray();
        Assert.Equal(new int?[] { 294, 156 }, calm);
        Assert.Equal(new int?[] { 252, 133 }, stacked);

        // And the boss really casts sooner: its first slam after the fail resolves on tick 252, not the calm 294.
        var slam = events.First(e => e.Type == "telegraph" && e.Tick > 60 &&
            e.Get("cast") is JsString { Value: "hollowWarden.slam" });
        Assert.Equal(252, slam.IntField("resolvesTick"));
    }

    [Fact(DisplayName = "Lock stakes: stacks climb 11666, 13333, 15000; the third is the enrage, once, and the ceiling")]
    public void Stacks_climb_to_the_enrage_ceiling()
    {
        // Locks open every 600 ticks from tick 0 and fail 60 ticks later: 60, 660, 1260, 1860.
        var (run, events) = StepUntil(BuildRun(bossHealth: 30000), 1900);
        var failed = events.OfType("lock_failed").ToList();
        Assert.Equal(new[] { 60, 660, 1260, 1860 }, failed.Select(e => e.Tick).ToArray());
        Assert.Equal(new long[] { 11666, 13333, 15000, 15000 }, failed.Select(e => e.IntField("tempoBp")).ToArray());
        var enrage = Assert.Single(events, e => e.Type == "boss_enrage");
        Assert.Equal(1260, enrage.Tick);
        Assert.Equal(15000, enrage.IntField("tempoBp"));

        var scripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Boss] = run.Content.EnemyScripts[Boss] };
        // Four fails pace exactly as the enrage alone (BossRulesTests.Enrage_is_a_tempo_spike: 196, 104).
        Assert.Equal(new int?[] { 196, 104 }, BossRules.EffectiveScripts(run.Encounter.Boss!, scripts)[Boss].Select(s => s.Every).ToArray());
    }

    [Fact(DisplayName = "Lock stakes: the boss frame reads LOCK x1/3 after one fail, nothing before, never on another frame")]
    public void Boss_frame_shows_the_stack_readout()
    {
        var (before, _) = StepUntil(BuildRun(bossHealth: 30000), 59);
        Assert.Null(BossLockReadout.For(before.Encounter, Boss));

        var (one, _) = StepUntil(before, 61);
        var readout = BossLockReadout.For(one.Encounter, Boss);
        Assert.NotNull(readout);
        Assert.Equal(1, readout!.Stacks);
        Assert.Equal(3, readout.MaxStacks);
        Assert.Equal(11666, readout.TempoBp);
        Assert.Equal("LOCK x1/3", readout.Text);
        Assert.Null(BossLockReadout.For(one.Encounter, TwinA));

        var (three, _) = StepUntil(one, 1261);
        Assert.Equal("LOCK x3/3", BossLockReadout.For(three.Encounter, Boss)!.Text);
        var (four, _) = StepUntil(three, 1861);
        Assert.Equal("LOCK x3/3", BossLockReadout.For(four.Encounter, Boss)!.Text);

        var (unarmed, _) = StepUntil(BuildRun(bossHealth: 30000, armed: false), 61);
        Assert.Null(BossLockReadout.For(unarmed.Encounter, Boss));
    }
}
