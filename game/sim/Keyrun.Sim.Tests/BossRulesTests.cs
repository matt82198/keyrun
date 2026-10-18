using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.BossTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane 3F: one sim test per boss rule (BIBLE s9, ENGINE-PLAN 3F). The Sundered Twins spawn with the boss and share one
/// Break bar fed from both; at 50% health the Lock shields the boss and opens a 3 s Window, repeating every 30 s; a
/// party Setup then a Payoff from another class inside the Window breaks it; three unbroken Locks enrage the boss into
/// a Tempo spike. An unarmed run carries none of it (the oracle traces stay byte-equal).
/// </summary>
public sealed class BossRulesTests
{
    [Fact(DisplayName = "Twins: both spawn once, flanking the boss, when the boss pull is live")]
    public void Twins_spawn_once_with_the_boss()
    {
        var (run, events) = StepUntil(BuildRun(), 40);

        var spawns = events.OfType("boss_add_spawn").ToList();
        Assert.Equal(new long[] { TwinA, TwinB }, spawns.Select(e => e.IntField("actorId")).ToArray());
        // Post-combat, like pull_start and echo_spawn: the run root's post-step tick (tick 0's step emits at 1).
        Assert.All(spawns, e => Assert.Equal(1, e.Tick));
        Assert.Equal(new Vec3(2000, 0, 0), run.State.Actors[TwinA].Pos);
        Assert.Equal(new Vec3(-2000, 0, 0), run.State.Actors[TwinB].Pos);
        Assert.Equal("hollowWarden.twinA", run.State.Actors[TwinA].ClassId);
        Assert.Equal(6000, run.State.Actors[TwinB].MaxHealth);
        Assert.True(run.Encounter.Boss!.TwinsSpawned);
    }

    [Fact(DisplayName = "Twins: one shared Break bar is fed by tagged casts on either twin")]
    public void Twins_share_one_bar_fed_from_both()
    {
        var commands = new Command[]
        {
            new CastCommand(2, Warden, "warden.shieldBash", TwinA), // interrupt-tagged: 34
            new CastCommand(4, Cinder, "cinder.coldSnap", TwinB), // control-tagged: 20
        };
        var (run, events) = StepUntil(BuildRun(), 10, commands);

        var bar = Assert.Single(run.Encounter.BreakBars);
        Assert.Equal("twinsBreak", bar.Key);
        Assert.Equal(100 - 34 - 20, bar.Value.Current);
        Assert.Equal(new[] { TwinA, TwinB }, bar.Value.ActorIds);
        Assert.DoesNotContain(events, e => e.Type == "stagger");
    }

    [Fact(DisplayName = "Twins: the bar empties off casts on both twins and stuns both")]
    public void Twins_bar_empties_and_stuns_both()
    {
        var commands = new Command[]
        {
            new CastCommand(2, Warden, "warden.shieldBash", TwinA),
            new CastCommand(4, Chanter, "chanter.rebuke", TwinB),
            new CastCommand(6, Cinder, "cinder.quench", TwinA),
        };
        var (run, events) = StepUntil(BuildRun(), 10, commands);

        var stagger = Assert.Single(events, e => e.Type == "stagger");
        Assert.Contains("\"actorIds\":[901,902]", stagger.ToCanonicalJson());
        Assert.True(run.State.Actors[TwinA].HasEffect("stunned", run.State.Tick));
        Assert.True(run.State.Actors[TwinB].HasEffect("stunned", run.State.Tick));
    }

    [Fact(DisplayName = "Lock: never opens above 50% health, and the content's 30 s lock loop is suppressed")]
    public void Lock_stays_shut_above_half()
    {
        var (run, events) = StepUntil(BuildRun(bossHealth: 30001), 700);

        Assert.DoesNotContain(events, e => e.Type == "lock_open");
        Assert.DoesNotContain(run.State.Actors[Boss].Effects, e => e.EffectId == "lockShield");
        Assert.DoesNotContain(events, e => e.Type == "window_opened" && e.Get("windowId") is JsString { Value: "lock" });
    }

    [Fact(DisplayName = "Lock: at 50% health the boss shields and opens a 3 s Window on itself")]
    public void Lock_opens_at_half()
    {
        var (run, _) = StepUntil(BuildRun(bossHealth: 30001), 20);
        run.State.Actors[Boss].Health = 30000;
        var (after, events) = StepUntil(run, 25);

        var open = Assert.Single(events, e => e.Type == "lock_open");
        Assert.Equal(20, open.Tick);
        Assert.Equal(20 + 60, open.IntField("endsTick"));
        Assert.Equal(Boss, open.IntField("actorId"));
        Assert.True(after.State.Actors[Boss].HasEffect("lockShield", after.State.Tick));
        Assert.Equal(new LockWindow(1, 20, 80), after.Encounter.Boss!.Active);
    }

    [Fact(DisplayName = "Lock: an unbroken Window fails at 3 s and the Lock repeats every 30 s")]
    public void Lock_fails_at_window_end_and_repeats()
    {
        var (_, events) = StepUntil(BuildRun(bossHealth: 30000), 700);

        Assert.Equal(new[] { 0, 600 }, events.OfType("lock_open").Select(e => e.Tick).ToArray());
        var failed = events.OfType("lock_failed").ToList();
        Assert.Equal(new[] { 60, 660 }, failed.Select(e => e.Tick).ToArray());
        Assert.Equal(new long[] { 1, 2 }, failed.Select(e => e.IntField("fails")).ToArray());
    }

    [Fact(DisplayName = "Lock: a party Setup then another class's Payoff inside the Window breaks it and drops the shield")]
    public void Lock_breaks_on_setup_then_payoff()
    {
        var commands = new Command[]
        {
            new CastCommand(5, Warden, "warden.strike", Boss), // Setup: sundered on the boss
            new CastCommand(10, Cinder, "cinder.coldSnap", Boss), // Payoff from cinder: Shatter
        };
        var (run, events) = StepUntil(BuildRun(bossHealth: 30000), 70, commands);

        var broken = Assert.Single(events, e => e.Type == "lock_broken");
        Assert.Equal(11, broken.Tick); // emitted post-combat of the tick-10 combo
        Assert.Equal(10, broken.IntField("comboTick"));
        Assert.Equal(Warden, broken.IntField("setupActorId"));
        Assert.Equal(Cinder, broken.IntField("payoffActorId"));
        Assert.DoesNotContain(events, e => e.Type == "lock_failed");
        Assert.False(run.State.Actors[Boss].HasEffect("lockShield", 15));
        Assert.Null(run.Encounter.Boss!.Active);
        Assert.Equal(1, run.Encounter.Boss.LocksBroken);
        Assert.Equal(0, run.Encounter.Boss.LockFails);
    }

    [Fact(DisplayName = "Lock: a Payoff on a Setup made before the Window opened does not break it")]
    public void Lock_needs_its_setup_inside_the_window()
    {
        var setupFirst = new Command[] { new CastCommand(2, Warden, "warden.strike", Boss) };
        // Full health first, so the Setup's own damage cannot cross 50% and open the Lock early.
        var (run, earlyEvents) = StepUntil(BuildRun(), 20, setupFirst);
        Assert.Contains(earlyEvents, e => e.Type == "window_opened" && e.Tick == 2);
        Assert.DoesNotContain(earlyEvents, e => e.Type == "lock_open");
        run.State.Actors[Boss].Health = 30000;
        var payoff = new Command[] { new CastCommand(25, Cinder, "cinder.coldSnap", Boss) };
        var (_, events) = StepUntil(run, 90, payoff);

        Assert.Contains(events, e => e.Type == "combo" && e.Tick == 25);
        Assert.DoesNotContain(events, e => e.Type == "lock_broken");
        Assert.Single(events, e => e.Type == "lock_failed" && e.Tick == 80);
    }

    [Fact(DisplayName = "Enrage: two unbroken Locks do not enrage; the third enrages the boss once")]
    public void Third_failed_lock_enrages()
    {
        var (atTwo, early) = StepUntil(BuildRun(bossHealth: 30000), 1200);
        Assert.DoesNotContain(early, e => e.Type == "boss_enrage");
        Assert.False(atTwo.Encounter.Boss!.Enraged);

        var (run, events) = StepUntil(atTwo, 1900);
        var enrage = Assert.Single(events, e => e.Type == "boss_enrage");
        Assert.Equal(1260, enrage.Tick);
        Assert.Equal(15000, enrage.IntField("tempoBp"));
        Assert.True(run.Encounter.Boss!.Enraged);
        Assert.Equal(4, events.OfType("lock_open").Count() + early.OfType("lock_open").Count());
    }

    [Fact(DisplayName = "Enrage: the Tempo spike speeds the boss's loops; the lock loop stays suppressed")]
    public void Enrage_is_a_tempo_spike()
    {
        var run = BuildRun();
        var scripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Boss] = run.Content.EnemyScripts[Boss] };
        var calm = BossRules.EffectiveScripts(run.Encounter.Boss!, scripts);
        var enraged = BossRules.EffectiveScripts(run.Encounter.Boss! with { Enraged = true }, scripts);

        Assert.Equal(new[] { "hollowWarden.slam", "hollowWarden.cleave" }, calm[Boss].Select(s => s.Cast).ToArray());
        Assert.Equal(new int?[] { 58, 156 }, calm[Boss].Select(s => s.Every).ToArray());
        Assert.Equal(new int?[] { 38, 104 }, enraged[Boss].Select(s => s.Every).ToArray());
        Assert.Equal(new int?[] { 60, 60 }, new[] { calm[TwinA], calm[TwinB] }.Select(s => s.Single().Every).ToArray());
    }

    [Fact(DisplayName = "Unarmed: a run without BossRules.Arm has no boss rules (the oracle path)")]
    public void Unarmed_run_has_no_boss_rules()
    {
        var (run, events) = StepUntil(BuildRun(bossHealth: 30000, armed: false), 700);

        Assert.Null(run.Encounter.Boss);
        Assert.DoesNotContain(events, e => e.Type is "boss_add_spawn" or "lock_open" or "lock_failed" or "boss_enrage");
        Assert.False(run.State.Actors.ContainsKey(TwinA));
        Assert.Contains(events, e => e.Type == "window_opened" && e.Get("windowId") is JsString { Value: "lock" });
    }
}
