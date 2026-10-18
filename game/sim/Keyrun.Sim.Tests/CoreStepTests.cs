using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Ports of tests/sim/core/{state,tick,replay,prng}.test.ts. Where the TS asserted <c>hashState</c> (FNV over
/// JS JSON text, which ORACLE.md section 2 says is NOT reproduced in C#), these assert the oracle projection
/// hash plus the fields the projection omits (seed, prng, runTimerTicks), so equality is still checked on
/// real state rather than dropped.
/// </summary>
public sealed class CoreStepTests
{
    private static ActorState MakeActor(int id) => new()
    {
        Id = id,
        Kind = ActorKind.Enemy,
        ClassId = "grunt",
        Role = ActorRole.None,
        Pos = new Vec3(id, 0, 0),
        Health = 50,
        MaxHealth = 50,
    };

    /// <summary>State fingerprint used in place of the TS hashState.</summary>
    private static string Fingerprint(SimState s) =>
        $"{Projection.Line(s.Tick, 0, 0, s.Actors.Values)}|seed={s.Seed}|prng={s.Prng}|timer={s.RunTimerTicks}|phase={s.Phase}";

    // ---- state.test.ts ----

    [Fact]
    public void CreateState_builds_the_initial_shape()
    {
        var s = SimState.Create(123);
        Assert.Equal(0, s.Tick);
        Assert.Equal(123, s.Seed);
        Assert.Equal(Prng.SeedState(123L), s.Prng);
        Assert.Empty(s.Actors);
        Assert.Equal(0, s.RunTimerTicks);
        Assert.Equal("init", s.Phase);
        Assert.Equal(0, s.FloorWindows.Count);
    }

    [Fact]
    public void CreateState_is_deterministic_for_the_same_seed()
    {
        Assert.Equal(Fingerprint(SimState.Create(5)), Fingerprint(SimState.Create(5)));
        Assert.NotEqual(Fingerprint(SimState.Create(5)), Fingerprint(SimState.Create(6)));
    }

    [Fact]
    public void CloneState_produces_a_deep_independent_copy()
    {
        var s = SimState.Create(1);
        var a = MakeActor(1);
        a.Effects.Add(new EffectInstance { EffectId = "buff", SourceId = 1, Stacks = 1, EndsTick = 10 });
        a.Cooldowns["warden.brace"] = 30;
        a.Threat[2] = 5;
        s.Actors[1] = a;
        s.FloorWindows["bulwark"] = new FloorWindow { WindowId = "bulwark", SetupClass = "warden", SetupActorId = 1, Pos = new Vec3(1, 0, 2), EndsTick = 9, RadiusMm = 2500 };

        var clone = s.Clone();
        Assert.Equal(Fingerprint(s), Fingerprint(clone));
        Assert.NotSame(s, clone);
        Assert.NotSame(s.Actors[1], clone.Actors[1]);

        clone.Actors[1].Health = 1;
        clone.Actors[1].Effects.Add(new EffectInstance { EffectId = "dot", SourceId = 2, Stacks = 1, EndsTick = 5, NextPeriodicTick = 1 });
        clone.Actors[1].Effects[0].Stacks = 9;
        clone.Actors[1].Cooldowns["warden.brace"] = 99;
        clone.Actors[1].Threat[2] = 77;
        clone.FloorWindows["bulwark"].EndsTick = 1000;
        clone.Actors[2] = MakeActor(2);

        Assert.Equal(50, s.Actors[1].Health);
        Assert.Single(s.Actors[1].Effects);
        Assert.Equal(1, s.Actors[1].Effects[0].Stacks);
        Assert.Equal(30, s.Actors[1].Cooldowns["warden.brace"]);
        Assert.Equal(5, s.Actors[1].Threat[2]);
        Assert.Equal(9, s.FloorWindows["bulwark"].EndsTick);
        Assert.Single(s.Actors);
    }

    [Fact]
    public void Actor_iteration_is_ascending_by_id_regardless_of_insertion_order()
    {
        var a = SimState.Create(9);
        a.Actors[2] = MakeActor(2);
        a.Actors[1] = MakeActor(1);
        a.Actors[1000] = MakeActor(1000);
        Assert.Equal(new[] { 1, 2, 1000 }, a.Actors.Keys.ToArray());
        var b = SimState.Create(9);
        b.Actors[1] = MakeActor(1);
        b.Actors[1000] = MakeActor(1000);
        b.Actors[2] = MakeActor(2);
        Assert.Equal(Fingerprint(a), Fingerprint(b));
    }

    // ---- tick.test.ts ----

    [Fact]
    public void Tick_constants_derive_ms_from_hz()
    {
        Assert.Equal(20, Tick.TickHz);
        Assert.Equal(50, Tick.TickMs);
    }

    [Fact]
    public void Step_advances_tick_and_timer_and_does_not_mutate_the_input()
    {
        var s0 = SimState.Create(1);
        s0.Actors[1] = MakeActor(1);
        var before = Fingerprint(s0);
        var result = Tick.Step(s0, new List<Command>(), (s, _, _) => s.Actors[1].Health -= 1);
        Assert.Equal(before, Fingerprint(s0));
        Assert.Equal(1, result.State.Tick);
        Assert.Equal(1, result.State.RunTimerTicks);
        Assert.Equal(49, result.State.Actors[1].Health);
        Assert.NotSame(s0, result.State);
    }

    [Fact]
    public void Step_drops_commands_whose_tick_does_not_match()
    {
        var seen = new List<Command>();
        Tick.Step(SimState.Create(1), new Command[] { new StopCastCommand(1, 1) }, (_, cmds, _) => seen.AddRange(cmds));
        Assert.Empty(seen);
    }

    [Fact]
    public void Step_drops_invalid_commands_instead_of_throwing()
    {
        var seen = new List<Command>();
        Tick.Step(SimState.Create(1), new Command[] { new MoveCommand(0, 1, new Dir2(5000, 0), 0), new CastCommand(0, 1, string.Empty) }, (_, cmds, _) => seen.AddRange(cmds));
        Assert.Empty(seen);
    }

    [Fact]
    public void Step_sorts_commands_by_actor_then_kind_then_insertion_order()
    {
        var seen = new List<string>();
        var cmds = new Command[]
        {
            new MoveCommand(0, 2, new Dir2(0, 0), 0),
            new StopCastCommand(0, 1),
            new CastCommand(0, 1, "a"),
            new TargetCommand(0, 1, null),
            new CastCommand(0, 2, "b"),
            new DashCommand(0, 2, new Dir2(0, 0), 0),
            new CastCommand(0, 1, "c"),
        };
        Tick.Step(SimState.Create(1), cmds, (_, cs, _) => seen.AddRange(cs.Select(c => c is CastCommand cc ? $"{c.ActorId}:cast:{cc.AbilityId}" : $"{c.ActorId}:{c.Kind}")));
        Assert.Equal(new[] { "1:cast:a", "1:cast:c", "1:stopcast", "1:target", "2:cast:b", "2:move", "2:dash" }, seen);
    }

    [Fact]
    public void Step_collects_emitted_events_in_order_and_is_deterministic()
    {
        StepRules rules = (s, cmds, emit) =>
        {
            foreach (var c in cmds)
            {
                if (c is StopCastCommand)
                {
                    s.RunTimerTicks += 1;
                }
                emit(new SimEvent(s.Tick, "seen", ("actorId", JsValue.Int(c.ActorId))));
            }
        };
        var cmds = new Command[] { new StopCastCommand(0, 1), new StopCastCommand(0, 3) };
        var runs = Enumerable.Range(0, 3).Select(_ => Tick.Step(SimState.Create(42), cmds, rules)).ToList();
        Assert.Equal(Fingerprint(runs[0].State), Fingerprint(runs[1].State));
        Assert.Equal(Fingerprint(runs[1].State), Fingerprint(runs[2].State));
        Assert.Equal(3, runs[0].State.RunTimerTicks);
        Assert.Equal(new[] { "{\"actorId\":1,\"tick\":0,\"type\":\"seen\"}", "{\"actorId\":3,\"tick\":0,\"type\":\"seen\"}" },
            runs[0].Events.Select(e => e.ToCanonicalJson()));
    }

    // ---- replay.test.ts ----

    private static readonly StepRules SpawnAndPoke = (s, cmds, emit) =>
    {
        if (s.Tick == 0)
        {
            s.Actors[1] = new ActorState { Id = 1, Kind = ActorKind.Player, ClassId = "warrior", Role = ActorRole.Tank, Health = 100, MaxHealth = 100, MaxResource = 100 };
        }
        foreach (var c in cmds)
        {
            if (s.Actors.TryGetValue(c.ActorId, out var actor) && c is StopCastCommand)
            {
                actor.Health -= 1;
            }
            emit(new SimEvent(s.Tick, "command", ("kind", JsValue.Str(c.Kind))));
        }
    };

    [Fact]
    public void Record_returns_a_tick_sorted_independent_copy()
    {
        var cmds = new List<Command> { new StopCastCommand(2, 1), new StopCastCommand(0, 1), new TargetCommand(0, 5, 3) };
        var rec = Replay.Record(cmds);
        Assert.Equal(new[] { 0, 0, 2 }, rec.Select(c => c.Tick));
        Assert.Equal(new[] { 1, 5, 1 }, rec.Select(c => c.ActorId)); // stable within a tick
        Assert.NotSame(cmds, rec);
        cmds.Clear();
        Assert.Equal(3, rec.Count);
    }

    [Fact]
    public void Apply_runs_from_tick_zero_through_the_last_command_tick()
    {
        var cmds = new Command[] { new StopCastCommand(0, 1), new StopCastCommand(2, 1) };
        var result = Replay.Apply(1, cmds, SpawnAndPoke);
        Assert.Equal(3, result.State.Tick);
        Assert.Equal(98, result.State.Actors[1].Health);
        Assert.Equal(2, result.Events.Count);
    }

    [Fact]
    public void Apply_is_deterministic_and_sensitive_to_the_command_stream()
    {
        var a = Replay.Apply(7, new Command[] { new StopCastCommand(0, 1), new StopCastCommand(1, 1) }, SpawnAndPoke);
        var b = Replay.Apply(7, new Command[] { new StopCastCommand(0, 1), new StopCastCommand(1, 1) }, SpawnAndPoke);
        var c = Replay.Apply(7, new Command[] { new StopCastCommand(0, 1), new TargetCommand(1, 1, null) }, SpawnAndPoke);
        Assert.Equal(Fingerprint(a.State), Fingerprint(b.State));
        Assert.NotEqual(Fingerprint(a.State), Fingerprint(c.State));
    }

    [Fact]
    public void Apply_with_no_commands_stays_at_tick_zero()
    {
        Assert.Equal(0, Replay.Apply(1, new Command[0], SpawnAndPoke).State.Tick);
    }

    // ---- prng.test.ts ----

    [Fact]
    public void Prng_known_answer_for_the_first_draws_of_seed_1()
    {
        var s = Prng.SeedState(1L);
        var draws = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            var (v, next) = Prng.NextInt(s, 1000);
            draws.Add(v);
            s = next;
        }
        Assert.Equal(new long[] { 148, 13, 91, 82, 206 }, draws);
    }

    [Fact]
    public void Prng_different_seeds_differ_and_bound_is_respected()
    {
        Assert.NotEqual(Prng.SeedState(1L), Prng.SeedState(2L));
        var s = Prng.SeedState(99L);
        var (v, _) = Prng.NextInt(s, 6);
        Assert.InRange(v, 0, 5);
        Assert.Equal(Prng.SeedState(99L), s);
    }

    [Fact]
    public void Prng_rejects_non_positive_bounds_and_bounds_above_2_pow_32()
    {
        var s = Prng.SeedState(1L);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => Prng.NextInt(s, 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => Prng.NextInt(s, -3));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => Prng.NextInt(s, 0x1_0000_0001L));
    }
}
