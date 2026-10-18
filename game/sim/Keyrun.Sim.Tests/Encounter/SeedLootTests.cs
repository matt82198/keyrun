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
/// (the Seed pull: deathBloom opens "seedbloom" at t1447, ends t1547 since the 2026-10-18 content tuning, STATE.md D14; was t2706 to t2806): the oracle stream as recorded has no Combo in
/// that Window, so the entry is absent; the same stream with one field Combo injected, shaped exactly as
/// sim/combat/payoff.ts:118-129 emits it, has the entry.
/// </summary>
public sealed class SeedLootTests
{
    private const int SeedActorId = 108;

    private static (SeedLootRule Rule, List<OracleRecord> Records) Pull3()
    {
        var setup = EncounterTestKit.Setup("hollow-vault-k2.setup.json");
        var scripts = EncounterJson.ParseScripts(((JsObject)setup.Get("content")!).Get("enemyScripts")!);
        var rule = SeedLoot.RuleFor(SeedActorId, scripts[SeedActorId], OracleRun.WindowIdOf(setup));
        Assert.NotNull(rule);
        return (rule!, OracleRun.Records("bots-hv-k2-s7").ToList());
    }

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
        var rewards = Observe(rule, records);
        Assert.Empty(rewards.BonusLoot);
        // The rule did see the oracle's Seed Window open (window_opened seedbloom, setupActorId 108, endsTick 1547).
        Assert.Equal(1547, rewards.SeedWindowEndsTick);
    }

    [Fact]
    public void A_Combo_landed_in_the_Seed_Window_sets_one_bonus_entry()
    {
        var (rule, records) = Pull3();
        var rewards = Observe(rule, records, new Dictionary<int, SimEvent[]> { [1462] = new[] { FieldCombo(1461) } });
        var entry = Assert.Single(rewards.BonusLoot);
        Assert.Equal(new BonusLootEntry("seed", "seedbloom", "seedbloom-field", 1461, SeedActorId, 1), entry);
    }

    [Fact]
    public void A_second_Combo_does_not_stack_a_second_entry()
    {
        var (rule, records) = Pull3();
        var rewards = Observe(rule, records, new Dictionary<int, SimEvent[]>
        {
            [1462] = new[] { FieldCombo(1461) },
            [1492] = new[] { FieldCombo(1491) },
        });
        Assert.Equal(1461, Assert.Single(rewards.BonusLoot).Tick);
    }

    [Fact]
    public void Combos_outside_the_Seed_Window_never_grant_the_entry()
    {
        var (rule, records) = Pull3();
        // Before the Window opened, after it ended, in another actor's Window, and in another Window id.
        var rewards = Observe(rule, records, new Dictionary<int, SimEvent[]>
        {
            [1441] = new[] { FieldCombo(1440) },
            [1549] = new[] { FieldCombo(1548) },
            [1463] = new[] { FieldCombo(1462, setupActorId: 3), FieldCombo(1462, windowId: "kindled") },
        });
        Assert.Empty(rewards.BonusLoot);
    }
}
