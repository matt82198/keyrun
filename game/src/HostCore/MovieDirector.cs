using System;
using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Host;

/// <summary>One proof frame due on the step just observed: its file name, the trace event it shows, and that
/// event's tick.</summary>
/// <param name="Name">The PNG file name (01-choir-chase.png, 02-lieutenant-cast-bar.png, 03-after-kick.png).</param>
/// <param name="Event">The trace event the frame was taken for, as a RunLog-style line.</param>
/// <param name="EventTick">The tick of that event.</param>
public sealed record MovieShot(string Name, string Event, int EventTick);

/// <summary>
/// Picks the <c>--write-movie</c> proof frames from the trace (Godot-free; finding f-50d8099e). Fixed capture ticks
/// drifted off their moments as pull timing changed, so each frame now waits for the event it names:
/// <list type="bullet">
/// <item><c>01-choir-chase.png</c> on the first CHASE step (an enemy itself moved and ended nearer the Warden, the
/// same rule as RunLog's CHASE line).</item>
/// <item><c>02-lieutenant-cast-bar.png</c> <see cref="CastBarLeadTicks"/> ticks into the first enemy cast of a must-kick
/// (interruptible) ability that is still being cast then, so the bar is on screen. A cast stopped before that tick
/// takes no frame; the next must-kick cast is used instead.</item>
/// <item><c>03-after-kick.png</c> on the step that carried the first interrupt (or cast_stopped) of an enemy's
/// must-kick cast after the cast-bar frame, i.e. the tick after the kick.</item>
/// </list>
/// Each frame is taken at most once per director (one director per run).
/// </summary>
public sealed class MovieDirector
{
    /// <summary>The Choir chase frame.</summary>
    public const string ChaseFrame = "01-choir-chase.png";

    /// <summary>The must-kick cast bar frame.</summary>
    public const string CastBarFrame = "02-lieutenant-cast-bar.png";

    /// <summary>The frame after the kick.</summary>
    public const string AfterKickFrame = "03-after-kick.png";

    /// <summary>Ticks into a must-kick cast before its bar is captured (200 ms at 20 Hz: the bar has visibly filled).</summary>
    public const int CastBarLeadTicks = 4;

    private readonly int _wardenId;
    private readonly IReadOnlyDictionary<string, AbilityDef> _abilities;
    private bool _chaseTaken;
    private bool _castBarTaken;
    private bool _afterKickTaken;

    /// <summary>Builds a director for one run.</summary>
    /// <param name="wardenId">The human Warden's actor id.</param>
    /// <param name="abilities">The run's abilities (which casts are must-kick).</param>
    public MovieDirector(int wardenId, IReadOnlyDictionary<string, AbilityDef> abilities)
    {
        _wardenId = wardenId;
        _abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
    }

    /// <summary>Whether an enemy chased the Warden this step: it moved itself and ended nearer him (RunLog CHASE).</summary>
    /// <param name="prev">The enemy before the step.</param>
    /// <param name="now">The enemy after the step.</param>
    /// <param name="w0">The Warden before the step.</param>
    /// <param name="w1">The Warden after the step.</param>
    /// <returns>True for a chase step.</returns>
    public static bool IsChase(ActorState prev, ActorState now, ActorState w0, ActorState w1)
    {
        ArgumentNullException.ThrowIfNull(prev);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(w0);
        ArgumentNullException.ThrowIfNull(w1);
        var d0 = Casting.RangeMm(prev, w0);
        return prev.Pos != now.Pos && Casting.RangeMm(now, w1) < d0 && Casting.RangeMm(now, w0) < d0;
    }

    /// <summary>The frames due on this step (captured once the step's frame is drawn).</summary>
    /// <param name="before">The state before the step.</param>
    /// <param name="after">The state after the step.</param>
    /// <param name="events">The step's events.</param>
    /// <returns>The shots, possibly empty.</returns>
    public IReadOnlyList<MovieShot> Observe(SimState before, SimState after, IReadOnlyList<SimEvent> events)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(events);
        var shots = new List<MovieShot>();
        if (!_chaseTaken && Chase(before, after) is { } chase)
        {
            _chaseTaken = true;
            shots.Add(chase);
        }
        // The kick is checked before the bar: a kick on this step belongs to a bar already taken, never to one
        // taken on the same step.
        if (_castBarTaken && !_afterKickTaken && Kick(before, events) is { } kick)
        {
            _afterKickTaken = true;
            shots.Add(kick);
        }
        if (!_castBarTaken && CastBar(after) is { } bar)
        {
            _castBarTaken = true;
            shots.Add(bar);
        }
        return shots;
    }

    private MovieShot? Chase(SimState before, SimState after)
    {
        if (!before.Actors.TryGetValue(_wardenId, out var w0) || !after.Actors.TryGetValue(_wardenId, out var w1))
        {
            return null;
        }
        foreach (var (id, now) in after.Actors)
        {
            if (now.Kind == ActorKind.Enemy && now.Alive && before.Actors.TryGetValue(id, out var prev) && IsChase(prev, now, w0, w1))
            {
                var line = FormattableString.Invariant(
                    $"CHASE actor={id} class={now.ClassId} dist={Casting.RangeMm(prev, w0)}->{Casting.RangeMm(now, w1)}");
                return new MovieShot(ChaseFrame, line, before.Tick);
            }
        }
        return null;
    }

    private MovieShot? CastBar(SimState after)
    {
        foreach (var (id, a) in after.Actors)
        {
            if (a.Kind != ActorKind.Enemy || !a.Alive || a.CastId is not { } castId || a.CastStartTick is not { } start ||
                !MustKick(castId))
            {
                continue;
            }
            var length = (a.CastEndsTick ?? start) - start;
            var lead = Math.Min(CastBarLeadTicks, Math.Max(0, length - 1));
            if (after.Tick >= start + lead)
            {
                return new MovieShot(CastBarFrame, FormattableString.Invariant($"CAST start actor={id} ability={castId}"), start);
            }
        }
        return null;
    }

    private MovieShot? Kick(SimState before, IReadOnlyList<SimEvent> events)
    {
        foreach (var e in events)
        {
            if (e.Type == "interrupt" && e.Get("targetId") is JsInt target && e.Get("abilityId") is JsString kicked &&
                IsEnemy(before, (int)target.Value) && MustKick(kicked.Value))
            {
                var by = e.Get("sourceId") is JsInt s ? s.Value : 0;
                return new MovieShot(AfterKickFrame,
                    FormattableString.Invariant($"interrupt target={target.Value} ability={kicked.Value} by={by}"), e.Tick);
            }
            if (e.Type == "cast_stopped" && e.Get("actorId") is JsInt actor && e.Get("abilityId") is JsString ability &&
                IsEnemy(before, (int)actor.Value) && MustKick(ability.Value))
            {
                return new MovieShot(AfterKickFrame,
                    FormattableString.Invariant($"cast_stopped actor={actor.Value} ability={ability.Value}"), e.Tick);
            }
        }
        return null;
    }

    private bool MustKick(string abilityId) => _abilities.TryGetValue(abilityId, out var def) && def.Interruptible;

    private static bool IsEnemy(SimState state, int id) => state.Actors.TryGetValue(id, out var a) && a.Kind == ActorKind.Enemy;
}
