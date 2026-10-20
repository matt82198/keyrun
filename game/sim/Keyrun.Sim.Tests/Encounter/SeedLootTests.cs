using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// Seed Window loot (BIBLE s9 item 3, ENGINE-PLAN s5 lane 2C): the Seed elite drops a Window on death; a Combo
/// landed in that Window sets a bonus-loot entry in the run's rewards. This is NEW behaviour (the frozen TS has no
/// bonus loot), so it has no oracle trace of its own. It is checked against the real key-2 oracle trace's pull 3
/// (the Seed pull: the Seed's onDeath deathBloom opens the "seedbloom" Window): the oracle stream as recorded has no
/// Combo in that Window, so the entry is absent; the same stream with one field Combo injected, shaped exactly as
/// sim/combat/payoff.ts:118-129 emits it, has the entry.
///
/// The Window's open and end ticks are READ from the trace's own <c>window_opened</c> event (<see cref="SeedWindow"/>),
/// never hard-coded: every bot or content change that re-records the trace moves the Seed's death tick (t2706, t1440,
/// t1447, t1469 and, since SLAM-TELEGRAPH's Pair leadTicks 140, t1447 again), and each move used to break this file
/// without the rule changing. The injected Combos sit at fixed offsets from those ticks, including both edges of the
/// Window (its end tick counts, the tick after it does not), so the rule is still pinned: a Combo outside the Window
/// that granted the entry fails <see cref="Combos_outside_the_Seed_Window_never_grant_the_entry"/>.
/// </summary>
public sealed class SeedLootTests
{
    private const int SeedActorId = 108;

    /// <summary>The Seed Window as the oracle recorded it: the window_opened tick and its endsTick.</summary>
    private sealed record Window(int OpenTick, int EndsTick);

    private static (SeedLootRule Rule, List<OracleRecord> Records) Pull3()
    {
        var setup = EncounterTestKit.Setup("hollow-vault-k2.setup.json");
        var scripts = EncounterJson.ParseScripts(((JsObject)setup.Get("content")!).Get("enemyScripts")!);
        var rule = SeedLoot.RuleFor(SeedActorId, scripts[SeedActorId], OracleRun.WindowIdOf(setup));
        Assert.NotNull(rule);
        return (rule!, OracleRun.Records("bots-hv-k2-s7").ToList());
    }

    /// <summary>
    /// The one window_opened event in the trace for the rule's Window id and the Seed as setup actor. Read straight
    /// from the oracle events, independent of <see cref="SeedLoot.Observe"/>, so the expectation does not come from
    /// the code under test.
    /// </summary>
    private static Window SeedWindow(SeedLootRule rule, IEnumerable<OracleRecord> records)
    {
        var opened = records.SelectMany(r => r.Events)
            .Where(e => e.Type == "window_opened"
                && e.Get("windowId") is JsString w && w.Value == rule.WindowId
                && e.Get("setupActorId") is JsInt a && a.Value == rule.SeedActorId)
            .ToList();
        var open = Assert.Single(opened);
        var ends = Assert.IsType<JsInt>(open.Get("endsTick"));
        Assert.True(ends.Value > open.Tick, $"seedbloom endsTick {ends.Value} is not after its open tick {open.Tick}");
        return new Window(open.Tick, (int)ends.Value);
    }

    /// <summary>Injection map entry: a Combo event for <paramref name="tick"/> rides the record of the step into tick + 1.</summary>
    private static KeyValuePair<int, SimEvent[]> At(int tick, params SimEvent[] events) => new(tick + 1, events);

    private static Dictionary<int, SimEvent[]> Inject(params KeyValuePair<int, SimEvent[]>[] entries) =>
        entries.ToDictionary(kv => kv.Key, kv => kv.Value);

    private static SimEvent FieldCombo(int tick, int setupActorId = SeedActorId, string windowId = "seedbloom") => new(tick, "combo",
        ("comboId", JsValue.Str("seedbloom-field")),
        ("windowId", JsValue.Str(windowId)),
        ("setupActorId", JsValue.Int(setupActorId)),
        ("setupClass", JsValue.Str("boss")),
        ("payoffActorId", JsValue.Int(1)),
        ("payoffClass", JsValue.Str("warden")),
        ("field", JsValue.Bool(true)));

    private static RunRewards Observe(SeedLootRule rule, IEnumerable<OracleRecord> records, Dictionary<int, SimEvent[]>? inject = null)
    {
        var rewards = RunRewards.Empty;
        foreach (var r in records)
        {
            var events = r.Events.AsEnumerable();
            if (inject is not null && inject.TryGetValue(r.T, out var extra))
            {
                events = events.Concat(extra);
            }
            rewards = SeedLoot.Observe(rewards, rule, events.ToList());
        }
        return rewards;
    }

    [Fact]
    public void The_rule_comes_from_the_Seeds_onDeath_step_and_its_window_op()
    {
        var (rule, _) = Pull3();
        Assert.Equal(new SeedLootRule(SeedActorId, "seed.deathBloom", "seedbloom"), rule);
        // An enemy with no onDeath window has no rule.
        Assert.Null(SeedLoot.RuleFor(900, new[] { ScriptStep.Loop(60, "hollowWarden.slam") }, _ => null));
    }

    [Fact]
    public void Without_a_Combo_in_the_Seed_Window_the_oracle_run_has_no_bonus_entry()
    {
        var (rule, records) = Pull3();
        var window = SeedWindow(rule, records);
        var rewards = Observe(rule, records);
        Assert.Empty(rewards.BonusLoot);
        // The rule did see the oracle's Seed Window open, with the end tick the oracle recorded for it.
        Assert.Equal(window.EndsTick, rewards.SeedWindowEndsTick);
    }

    [Fact]
    public void A_Combo_landed_in_the_Seed_Window_sets_one_bonus_entry()
    {
        var (rule, records) = Pull3();
        var window = SeedWindow(rule, records);
        var tick = window.OpenTick + 21;
        var rewards = Observe(rule, records, Inject(At(tick, FieldCombo(tick))));
        var entry = Assert.Single(rewards.BonusLoot);
        Assert.Equal(new BonusLootEntry("seed", "seedbloom", "seedbloom-field", tick, SeedActorId, 1), entry);
    }

    [Fact]
    public void A_Combo_on_the_Windows_end_tick_still_counts()
    {
        var (rule, records) = Pull3();
        var window = SeedWindow(rule, records);
        var rewards = Observe(rule, records, Inject(At(window.EndsTick, FieldCombo(window.EndsTick))));
        Assert.Equal(window.EndsTick, Assert.Single(rewards.BonusLoot).Tick);
    }

    [Fact]
    public void A_second_Combo_does_not_stack_a_second_entry()
    {
        var (rule, records) = Pull3();
        var window = SeedWindow(rule, records);
        var first = window.OpenTick + 21;
        var second = window.OpenTick + 51;
        var rewards = Observe(rule, records, Inject(At(first, FieldCombo(first)), At(second, FieldCombo(second))));
        Assert.Equal(first, Assert.Single(rewards.BonusLoot).Tick);
    }

    [Fact]
    public void Combos_outside_the_Seed_Window_never_grant_the_entry()
    {
        var (rule, records) = Pull3();
        var window = SeedWindow(rule, records);
        var before = window.OpenTick - 7;
        var after = window.EndsTick + 1;
        var inside = window.OpenTick + 22;
        // Before the Window opened, the tick after it ended, in another actor's Window, and in another Window id.
        var rewards = Observe(rule, records, Inject(
            At(before, FieldCombo(before)),
            At(after, FieldCombo(after)),
            At(inside, FieldCombo(inside, setupActorId: 3), FieldCombo(inside, windowId: "kindled"))));
        Assert.Empty(rewards.BonusLoot);
    }
}
