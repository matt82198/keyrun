using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Finding f-50d8099e: <c>--write-movie</c> captured its three proof frames at fixed ticks (8 / 220 / 246), so when
/// pull timing drifted the "lieutenant cast bar" frame showed no cast bar. <see cref="MovieDirector"/> (game/src/Host,
/// Godot-free, linked into this project) must pick each frame's tick from the trace instead: the first CHASE for the
/// chase frame, a few ticks into the first must-kick cast that is still being cast for the cast-bar frame, and the
/// step after the kick (interrupt or cast_stopped) for the after-kick frame. These tests drive it step by step over
/// hand-built states whose events land on ticks far from 8 / 220 / 246.
/// </summary>
public sealed class MovieDirectorTests
{
    private const int Warden = 1;
    private const int Lieutenant = 5;
    private const int Choir = 6;
    private const string Mend = "choir.mend";
    private const string Bolt = "choir.bolt";

    private static readonly IReadOnlyDictionary<string, AbilityDef> Abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
    {
        [Mend] = Ability(Mend, castTicks: 80, interruptible: true),
        [Bolt] = Ability(Bolt, castTicks: 40, interruptible: false),
    };

    private static AbilityDef Ability(string id, int castTicks, bool interruptible) => new()
    {
        Id = id,
        CastTicks = castTicks,
        GcdTicks = 0,
        CooldownTicks = 0,
        Charges = 1,
        Cost = new AbilityCost("none", 0),
        Range = 30000,
        Shape = new AbilityShape(ShapeKind.Target),
        Interruptible = interruptible,
        Effects = Array.Empty<EffectOp>(),
    };

    private static SimState Initial()
    {
        var s = SimState.Create(1);
        s.Actors[Warden] = new ActorState { Id = Warden, Kind = ActorKind.Player, ClassId = "warden", Pos = new Vec3(0, 0, 0), Health = 100, MaxHealth = 100 };
        s.Actors[Lieutenant] = new ActorState { Id = Lieutenant, Kind = ActorKind.Enemy, ClassId = "choir.lieutenant", Pos = new Vec3(20000, 0, 0), Health = 100, MaxHealth = 100 };
        s.Actors[Choir] = new ActorState { Id = Choir, Kind = ActorKind.Enemy, ClassId = "choir.chanter", Pos = new Vec3(0, 0, 15000), Health = 100, MaxHealth = 100 };
        return s;
    }

    private static void StartCast(SimState s, int actor, string ability, int start, int length)
    {
        var a = s.Actors[actor];
        a.CastId = ability;
        a.CastStartTick = start;
        a.CastEndsTick = start + length;
    }

    private static void ClearCast(SimState s, int actor)
    {
        var a = s.Actors[actor];
        a.CastId = null;
        a.CastStartTick = null;
        a.CastEndsTick = null;
    }

    private static SimEvent Interrupt(int tick, int target, string ability) =>
        new(tick, "interrupt", ("sourceId", JsValue.Int(Warden)), ("targetId", JsValue.Int(target)), ("abilityId", JsValue.Str(ability)));

    private static SimEvent CastStopped(int tick, int actor, string ability) =>
        new(tick, "cast_stopped", ("actorId", JsValue.Int(actor)), ("abilityId", JsValue.Str(ability)));

    /// <summary>Steps <paramref name="ticks"/> ticks; <paramref name="script"/> mutates the next state for pre-step
    /// tick t and returns the step's events. Returns every shot with the post-step tick it was due on.</summary>
    private static List<(int Tick, MovieShot Shot)> Drive(int ticks, Func<int, SimState, IReadOnlyList<SimEvent>> script)
    {
        var director = new MovieDirector(Warden, Abilities);
        var state = Initial();
        var shots = new List<(int, MovieShot)>();
        for (var t = 0; t < ticks; t++)
        {
            var next = state.Clone();
            var events = script(t, next);
            next.Tick = t + 1;
            foreach (var shot in director.Observe(state, next, events))
            {
                shots.Add((next.Tick, shot));
            }
            state = next;
        }
        return shots;
    }

    private static int TickOf(List<(int Tick, MovieShot Shot)> shots, string name) =>
        Assert.Single(shots, s => s.Shot.Name == name).Tick;

    // A full scripted pull: the Choir chanter closes in from tick 30, the non-kickable bolt is cast at tick 50, the
    // lieutenant's kickable mend starts at tick 117 and is kicked at tick 151.
    private static IReadOnlyList<SimEvent> Pull(int t, SimState next)
    {
        var events = new List<SimEvent>();
        if (t >= 30 && t < 40)
        {
            var c = next.Actors[Choir];
            c.Pos = c.Pos with { Z = c.Pos.Z - 500 };
        }
        if (t == 50)
        {
            StartCast(next, Choir, Bolt, 50, 40);
        }
        if (t == 90)
        {
            ClearCast(next, Choir);
            events.Add(new SimEvent(90, "cast_complete", ("actorId", JsValue.Int(Choir)), ("abilityId", JsValue.Str(Bolt))));
        }
        if (t == 117)
        {
            StartCast(next, Lieutenant, Mend, 117, 80);
        }
        if (t == 151)
        {
            ClearCast(next, Lieutenant);
            events.Add(Interrupt(151, Lieutenant, Mend));
        }
        return events;
    }

    [Fact(DisplayName = "Movie: the chase frame is taken on the first CHASE step, not a fixed tick")]
    public void Chase_frame_lands_on_the_first_chase()
    {
        var shots = Drive(300, Pull);
        Assert.Equal(31, TickOf(shots, MovieDirector.ChaseFrame));
        var chase = shots.Single(s => s.Shot.Name == MovieDirector.ChaseFrame).Shot;
        Assert.Equal(30, chase.EventTick);
        Assert.StartsWith($"CHASE actor={Choir}", chase.Event, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Movie: the cast-bar frame is taken a few ticks into the first must-kick cast, while it is still cast")]
    public void Cast_bar_frame_lands_inside_the_must_kick_cast()
    {
        var shots = Drive(300, Pull);
        var tick = TickOf(shots, MovieDirector.CastBarFrame);
        Assert.Equal(117 + MovieDirector.CastBarLeadTicks, tick);
        Assert.InRange(tick, 118, 151);
        var bar = shots.Single(s => s.Shot.Name == MovieDirector.CastBarFrame).Shot;
        Assert.Equal(117, bar.EventTick);
        Assert.Equal($"CAST start actor={Lieutenant} ability={Mend}", bar.Event);
    }

    [Fact(DisplayName = "Movie: the after-kick frame is taken on the step that carried the kick")]
    public void After_kick_frame_lands_on_the_tick_after_the_interrupt()
    {
        var shots = Drive(300, Pull);
        Assert.Equal(152, TickOf(shots, MovieDirector.AfterKickFrame));
        var kick = shots.Single(s => s.Shot.Name == MovieDirector.AfterKickFrame).Shot;
        Assert.Equal(151, kick.EventTick);
        Assert.Equal($"interrupt target={Lieutenant} ability={Mend} by={Warden}", kick.Event);
    }

    [Fact(DisplayName = "Movie: a must-kick cast stopped before its bar is captured re-arms on the next one")]
    public void A_cast_kicked_before_the_lead_does_not_take_the_bar_frame()
    {
        var shots = Drive(300, (t, next) =>
        {
            if (t == 40)
            {
                StartCast(next, Lieutenant, Mend, 40, 80);
            }
            if (t == 41)
            {
                ClearCast(next, Lieutenant);
                return new[] { Interrupt(41, Lieutenant, Mend) };
            }
            if (t == 133)
            {
                StartCast(next, Lieutenant, Mend, 133, 80);
            }
            if (t == 177)
            {
                ClearCast(next, Lieutenant);
                return new[] { CastStopped(177, Lieutenant, Mend) };
            }
            return Array.Empty<SimEvent>();
        });
        Assert.Equal(133 + MovieDirector.CastBarLeadTicks, TickOf(shots, MovieDirector.CastBarFrame));
        Assert.Equal(178, TickOf(shots, MovieDirector.AfterKickFrame));
        Assert.Equal($"cast_stopped actor={Lieutenant} ability={Mend}",
            shots.Single(s => s.Shot.Name == MovieDirector.AfterKickFrame).Shot.Event);
    }

    [Fact(DisplayName = "Movie: with no chase and no must-kick cast no frame is taken")]
    public void No_trace_events_means_no_frames()
    {
        var shots = Drive(300, (t, next) => Array.Empty<SimEvent>());
        Assert.Empty(shots);
    }
}
