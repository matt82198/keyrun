using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// tests/sim/encounter/clauses.test.ts ported 1:1. Clause params mirror content/data/clauses.json (Echo 600/40,
/// Tide 50, Hush 900/40, Pact 2000/-1000), plus Echo realisation (sim/encounter/echo-spawns.ts).
/// </summary>
public sealed class ClausesTests
{
    private static ClauseDef Def(string id, ClauseKind kind, params (string, long)[] ps)
    {
        var d = new Dictionary<string, long>();
        foreach (var (k, v) in ps)
        {
            d[k] = v;
        }
        return new ClauseDef(id, id, kind, d, id);
    }

    private static readonly ClauseDef Echo = Def("echo", ClauseKind.Echo, ("addMaxHealth", 600), ("spawnDelayTicks", 40));
    private static readonly ClauseDef Tide = Def("tide", ClauseKind.Tide, ("driftMmPerTick", 50));
    private static readonly ClauseDef Hush = Def("hush", ClauseKind.Hush, ("intervalTicks", 900), ("silenceDurationTicks", 40));
    private static readonly ClauseDef Pact = Def("pact", ClauseKind.Pact, ("comboDamageBp", 2000), ("soloDamageBp", -1000));

    private static readonly Telegraph CircleTelegraph = new(1, 99, "wave", "circle", Vec3.Zero, 0, 100) { Radius = 1000 };
    private static readonly Vec3 StandingActor = new(1200, 0, 0);

    private static SimEvent Interrupt(int tick, int source, int target) =>
        new(tick, "interrupt", ("sourceId", JsValue.Int(source)), ("targetId", JsValue.Int(target)));

    [Fact]
    public void DefaultEncounterRules_carries_no_active_clause_and_zeroed_clause_knobs()
    {
        var rules = Clauses.DefaultEncounterRules(5);
        Assert.Null(rules.ActiveClauseId);
        Assert.Null(rules.ActiveClauseKind);
        Assert.Equal(0, rules.DriftMmPerTick);
        Assert.Equal(0, rules.SilenceIntervalTicks);
        Assert.Equal(0, rules.ComboDamageBp);
        Assert.Equal(0, rules.EchoAddMaxHealth);
        Assert.Equal(5, rules.KeyLevel);
        Assert.Equal(Tempo.TempoBpPerKey, rules.TempoBpPerKey);
        Assert.Equal(Timer.WipeSlackCostTicks, rules.WipeSlackCostTicks);
    }

    [Fact]
    public void No_op_clause_effects_leave_stepped_pull_data_unchanged()
    {
        var rules = Clauses.DefaultEncounterRules(1);
        Assert.Equal(CircleTelegraph, Clauses.DriftTelegraphOrigin(CircleTelegraph, rules));
        Assert.False(Clauses.ShouldSilence(900, rules));
        Assert.Equal(1000, Clauses.ApplyPactDamageBp(1000, true, rules));
        Assert.Empty(Clauses.PendingEchoSpawns(new[] { Interrupt(100, 1, 2) }, rules));
    }

    [Fact]
    public void ApplyClause_sets_activeClauseId_and_activeClauseKind()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Echo);
        Assert.Equal("echo", rules.ActiveClauseId);
        Assert.Equal(ClauseKind.Echo, rules.ActiveClauseKind);
    }

    [Fact]
    public void Applying_a_second_clause_throws_ClauseAlreadyActiveError()
    {
        var withEcho = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Echo);
        var ex = Assert.Throws<ClauseAlreadyActiveException>(() => Clauses.ApplyClause(withEcho, Tide));
        Assert.Matches("echo.*tide", ex.Message);
    }

    [Fact]
    public void Is_pure_the_input_rules_object_is_never_mutated()
    {
        var b = Clauses.DefaultEncounterRules(1);
        Clauses.ApplyClause(b, Pact);
        Assert.Null(b.ActiveClauseId);
        Assert.Equal(0, b.ComboDamageBp);
    }

    [Fact]
    public void Emits_one_delayed_spawn_request_per_interrupt_event()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Echo);
        var events = new[]
        {
            new SimEvent(100, "interrupt", ("sourceId", JsValue.Int(1)), ("targetId", JsValue.Int(2)), ("abilityId", JsValue.Str("cast-a")), ("school", JsValue.Int(0))),
            new SimEvent(100, "damage", ("sourceId", JsValue.Int(1)), ("targetId", JsValue.Int(2)), ("amount", JsValue.Int(50))),
        };
        Assert.Equal(new[] { new EchoSpawnRequest(140, 2, 600) }, Clauses.PendingEchoSpawns(events, rules));
    }

    [Fact]
    public void A_non_echo_active_clause_produces_no_spawns_even_on_an_interrupt_event()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Tide);
        Assert.Empty(Clauses.PendingEchoSpawns(new[] { Interrupt(100, 1, 2) }, rules));
    }

    [Fact]
    public void Drifts_a_telegraphs_origin_by_driftMmPerTick_per_call_changing_a_stepped_pulls_hit_test()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Tide);
        Assert.False(Telegraphs.PointInCircle(CircleTelegraph.Origin, CircleTelegraph.Radius ?? 0, StandingActor));
        var drifted = CircleTelegraph;
        for (var i = 0; i < 4; i++)
        {
            drifted = Clauses.DriftTelegraphOrigin(drifted, rules);
        }
        Assert.Equal(new Vec3(200, 0, 0), drifted.Origin);
        Assert.True(Telegraphs.PointInCircle(drifted.Origin, drifted.Radius ?? 0, StandingActor));
    }

    [Fact]
    public void DriftTelegraphs_maps_a_whole_registry_and_leaves_shape_radius_untouched()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Tide);
        var drifted = Assert.Single(Clauses.DriftTelegraphs(new[] { CircleTelegraph }, rules));
        Assert.Equal(50, drifted.Origin.X);
        Assert.Equal(1000, drifted.Radius);
    }

    [Fact]
    public void Is_silent_from_tick_900_through_939_not_before_or_after()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Hush);
        Assert.False(Clauses.ShouldSilence(899, rules));
        Assert.True(Clauses.ShouldSilence(900, rules));
        Assert.True(Clauses.ShouldSilence(939, rules));
        Assert.False(Clauses.ShouldSilence(940, rules));
    }

    [Fact]
    public void Never_fires_on_tick_0_and_repeats_on_the_second_pulse_at_tick_1800()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Hush);
        Assert.False(Clauses.ShouldSilence(0, rules));
        Assert.False(Clauses.ShouldSilence(1799, rules));
        Assert.True(Clauses.ShouldSilence(1800, rules));
        Assert.True(Clauses.ShouldSilence(1839, rules));
        Assert.False(Clauses.ShouldSilence(1840, rules));
    }

    [Fact]
    public void Scales_a_combo_hit_up_20_percent_and_a_solo_hit_down_10_percent_integer_truncated()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Pact);
        Assert.Equal(1200, Clauses.ApplyPactDamageBp(1000, true, rules));
        Assert.Equal(900, Clauses.ApplyPactDamageBp(1000, false, rules));
        Assert.Equal(399, Clauses.ApplyPactDamageBp(333, true, rules));
        // clauses.ts:145 Math.trunc: -333 * 12000 / 10000 = -399.6 truncates toward zero.
        Assert.Equal(-399, Clauses.ApplyPactDamageBp(-333, true, rules));
    }

    [Fact]
    public void Is_deterministic_same_rules_same_hash_repeated_calls()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(3), Pact);
        var h1 = Clauses.HashEncounterRules(rules);
        Assert.Equal(h1, Clauses.HashEncounterRules(rules));
        Assert.Equal(h1, Clauses.HashEncounterRules(Clauses.ApplyClause(Clauses.DefaultEncounterRules(3), Pact)));
        // FNV-1a 32 over "1|0|0|0|0|0|0|0|none|none" (clauses.ts:171), the no-clause key-1 field list.
        Assert.Equal(Fnv1a.Hex(Fnv1a.Hash("1|0|0|0|0|0|0|0|none|none")), Clauses.HashEncounterRules(Clauses.DefaultEncounterRules(1)));
    }

    [Fact]
    public void Differs_across_no_clause_and_each_of_the_four_clauses()
    {
        var b = Clauses.DefaultEncounterRules(1);
        var hashes = new HashSet<string>
        {
            Clauses.HashEncounterRules(b),
            Clauses.HashEncounterRules(Clauses.ApplyClause(b, Echo)),
            Clauses.HashEncounterRules(Clauses.ApplyClause(b, Tide)),
            Clauses.HashEncounterRules(Clauses.ApplyClause(b, Hush)),
            Clauses.HashEncounterRules(Clauses.ApplyClause(b, Pact)),
        };
        Assert.Equal(5, hashes.Count);
    }

    [Fact]
    public void Echo_realises_a_due_request_as_an_enemy_add_at_the_source_position()
    {
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), Echo);
        var encounter = EncounterTestKit.BaseEncounter() with { Rules = rules };
        var source = EncounterTestKit.Actor(2, ActorKind.Enemy, new Vec3(300, 0, -40));
        var state = EncounterTestKit.State(100, source);
        var emitted = new List<SimEvent>();

        var queued = EchoSpawns.Step(encounter, state, new[] { Interrupt(100, 1, 2) }, 100, emitted.Add);
        Assert.Empty(queued.Spawned);
        Assert.Equal(new[] { new EchoSpawnRequest(140, 2, 600) }, queued.Encounter.PendingEchoSpawns);

        var early = EchoSpawns.Step(queued.Encounter, state, Array.Empty<SimEvent>(), 139, emitted.Add);
        Assert.Empty(early.Spawned);

        var due = EchoSpawns.Step(early.Encounter, state, Array.Empty<SimEvent>(), 140, emitted.Add);
        var add = Assert.Single(due.Spawned);
        Assert.Equal(EchoSpawns.EchoActorIdBase, add.Id);
        Assert.Equal(ActorKind.Enemy, add.Kind);
        Assert.Equal("echo-add", add.ClassId);
        Assert.Equal(new Vec3(300, 0, -40), add.Pos);
        Assert.Equal(600, add.Health);
        Assert.Equal(600, add.MaxHealth);
        Assert.Empty(due.Encounter.PendingEchoSpawns);
        Assert.Equal(EchoSpawns.EchoActorIdBase + 1, due.Encounter.NextEchoActorId);
        Assert.Equal(
            new[] { EncounterTestKit.Ev("{\"tick\":140,\"type\":\"echo_spawn\",\"actorId\":1000000,\"sourceActorId\":2,\"maxHealth\":600}") },
            EncounterTestKit.Json(emitted));

        // A missing source spawns at the origin.
        var gone = EchoSpawns.Step(queued.Encounter, EncounterTestKit.State(140), Array.Empty<SimEvent>(), 140, _ => { });
        Assert.Equal(Vec3.Zero, Assert.Single(gone.Spawned).Pos);
    }
}
