using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// The encounter-only replay's model of each enemy's cast in flight (<see cref="EncounterTraceTests"/>), which runs no
/// combat and so has to learn every cast-time enemy cast from the trace or from its own encounter step.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A cast-time cast that completes is learned from the oracle's <c>cast_complete</c> on tick X: with castTicks C it
/// was in flight on (X - C, X].</item>
/// <item>The trace has no cast_start event (game/sim/ORACLE.md section 3: encounter-generated enemy casts are not in a
/// record's commands, the sim derives them itself), so the cast start is the cast the replay's own encounter step
/// issues on tick S, in flight on (S, S + C]. Without it a cast that never completes (the run ends, or the caster
/// dies, first) is invisible and its caster looks free (PR #415 ruling R-415-C: bots-hv-k10-s7's hollowWarden.slam
/// begun at 2065, run end 2074).</item>
/// <item>An oracle <c>interrupt</c> of the caster on tick I ends the cast: it is in flight through I, not after.</item>
/// <item>A cast the step issues while its ability is still on cooldown never starts (combat refuses it not_ready), so it
/// is not recorded (f-df833cf3: on bots-hv-k10-s7 Tempo fires the cleave step every 135 ticks against a 160-tick
/// cooldown; the refused cleave made the boss look busy, held the slam due after it, and the replay re-drew that slam
/// where the oracle's boss was free; on a merged trace the same phantom cleave held a slam and lock due on one tick,
/// which read as a tiebreak divergence but was not: both sides break that tie in script order).</item>
/// <item>A target-shaped cast combat refuses out of range (pair.spreadMark, seed.lash) never starts either. The replay
/// has no positions between projections, so it cannot judge range itself; combat reports the refusal as a
/// <c>cast_refused</c> event (enemies only, reason out_of_range; Rules.cs and sim/combat/rules.ts), and the ledger
/// reads it from the trace instead of re-deriving the rule (f-fa598b6b: otherwise the refused cast shows a phantom busy
/// caster and holds a cast the oracle does not hold).</item>
/// </list>
/// </remarks>
internal sealed class EncounterCastLedger
{
    private readonly IReadOnlyDictionary<string, int> _castTicks;
    private readonly List<(int Actor, string Ability, int Start, int Ends, int Cut)> _spans = new();

    private readonly IReadOnlyDictionary<string, int> _cooldownTicks;
    private readonly HashSet<(int Actor, string Ability, int Tick)> _refused = new();

    /// <summary>Creates an empty ledger.</summary>
    /// <param name="castTicks">Each ability's cast time in ticks (0 for instant casts, which are never in flight).</param>
    /// <param name="cooldownTicks">Each ability's cooldown in ticks; null = none known.</param>
    /// <param name="castTicksAt">The cast time of a cast an actor begins on a tick, under its live haste (R-HASTE-1,
    /// Casting.HastedTicks); null = the content cast time.</param>
    public EncounterCastLedger(IReadOnlyDictionary<string, int> castTicks, IReadOnlyDictionary<string, int>? cooldownTicks = null,
        Func<int, string, int, int>? castTicksAt = null)
    {
        _castTicks = castTicks;
        _cooldownTicks = cooldownTicks ?? new Dictionary<string, int>();
        _castTicksAt = castTicksAt;
    }

    private readonly Func<int, string, int, int>? _castTicksAt;

    private int CastTicksOf(string ability) => _castTicks.TryGetValue(ability, out var c) ? c : 0;

    private void Add(int actor, string ability, int start, int ends)
    {
        if (!_spans.Any(s => s.Actor == actor && s.Start == start && s.Ends == ends))
        {
            _spans.Add((actor, ability, start, ends, ends));
        }
    }

    /// <summary>Records the oracle's <c>cast_complete</c> of <paramref name="ability"/> by an enemy on <paramref name="tick"/>.</summary>
    /// <param name="actor">The caster.</param>
    /// <param name="ability">The ability.</param>
    /// <param name="tick">The event tick.</param>
    public void Completed(int actor, string ability, int tick)
    {
        var c = CastTicksOf(ability);
        if (c > 0)
        {
            Add(actor, ability, StartOfCompletion(actor, ability, tick, c), tick);
        }
    }

    // The cast time of a cast begun on start: the content cast time, or under haste (R-HASTE-1) the hasted one.
    private int TicksAt(int actor, string ability, int start) =>
        _castTicksAt is null ? CastTicksOf(ability) : _castTicksAt(actor, ability, start);

    // R-HASTE-1: a cast completing on tick X began on the latest tick S whose (hasted) cast time ends it at X; with no
    // haste that is X - C. In the oracle runs only the Anthem's hasted (+1500 bp) reaches an enemy (Chill comes only from the
    // C#-only Overheat), so S is searched in [X - C, X), falling back to X - C.
    private int StartOfCompletion(int actor, string ability, int tick, int c)
    {
        for (var s = tick - 1; s >= tick - c; s--)
        {
            if (s + TicksAt(actor, ability, s) == tick)
            {
                return s;
            }
        }
        return tick - c;
    }

    /// <summary>Records the cast the replay's encounter step issued for an enemy on <paramref name="tick"/> (its cast start).</summary>
    /// <param name="actor">The caster.</param>
    /// <param name="ability">The ability.</param>
    /// <param name="tick">The command tick.</param>
    public void Started(int actor, string ability, int tick)
    {
        var c = CastTicksOf(ability);
        if (c > 0 && !_refused.Contains((actor, ability, tick)) && OffCooldown(actor, ability, tick))
        {
            Add(actor, ability, tick, tick + TicksAt(actor, ability, tick));
        }
    }

    // sim/combat/casting.ts startCast refuses a cast whose ability is not ready (cooldowns.ts chargesAvailable: every
    // enemy ability has one charge, so ready once now >= its last start + cooldownTicks) BEFORE it consumes a charge, so
    // a refused cast never starts, never holds its caster and never restarts the cooldown. The last start is the latest
    // span of this ability that began before tick: an oracle completion, an interrupted start, or an accepted Started.
    private bool OffCooldown(int actor, string ability, int tick)
    {
        if (!_cooldownTicks.TryGetValue(ability, out var cd) || cd <= 0)
        {
            return true;
        }
        var last = _spans.Where(s => s.Actor == actor && s.Ability == ability && s.Start < tick)
            .Select(s => (int?)s.Start).DefaultIfEmpty(null).Max();
        return last is not { } l || tick >= l + cd;
    }

    /// <summary>
    /// Records combat's <c>cast_refused</c> of <paramref name="ability"/> by an enemy on <paramref name="tick"/>: the cast
    /// the encounter step issued that tick never started, so it is not in flight and starts no cooldown (f-fa598b6b).
    /// </summary>
    /// <param name="actor">The caster.</param>
    /// <param name="ability">The ability.</param>
    /// <param name="tick">The event tick (the tick the step issued the cast).</param>
    public void Refused(int actor, string ability, int tick)
    {
        _refused.Add((actor, ability, tick));
        _spans.RemoveAll(s => s.Actor == actor && s.Ability == ability && s.Start == tick);
    }

    /// <summary>Records an oracle <c>interrupt</c> of <paramref name="actor"/>'s cast on <paramref name="tick"/>.</summary>
    /// <param name="actor">The interrupted caster (the event's targetId).</param>
    /// <param name="tick">The event tick.</param>
    public void Interrupted(int actor, int tick)
    {
        for (var i = 0; i < _spans.Count; i++)
        {
            var s = _spans[i];
            if (s.Actor == actor && s.Start <= tick && tick < s.Cut)
            {
                _spans[i] = s with { Cut = tick };
            }
        }
    }

    /// <summary>The cast <paramref name="actor"/> has in flight on state tick <paramref name="t"/>, if any.</summary>
    /// <param name="actor">The enemy.</param>
    /// <param name="t">The state tick.</param>
    /// <returns>The ability and its scheduled end tick, or null.</returns>
    public (string Ability, int Ends)? InFlight(int actor, int t)
    {
        foreach (var s in _spans)
        {
            if (s.Actor == actor && s.Start < t && t <= s.Cut)
            {
                return (s.Ability, s.Ends);
            }
        }
        return null;
    }

    private bool CutBefore(int actor, int t) => _spans.Any(s => s.Actor == actor && s.Cut < s.Ends && s.Cut < t && t <= s.Ends);

    /// <summary>Writes every enemy's in-flight cast on tick <paramref name="t"/> into <paramref name="st"/>.</summary>
    /// <param name="st">The replay state.</param>
    /// <param name="t">The state tick.</param>
    public void Apply(SimState st, int t)
    {
        ArgumentNullException.ThrowIfNull(st);
        foreach (var a in st.Actors.Values.Where(a => a.Kind == ActorKind.Enemy))
        {
            if (InFlight(a.Id, t) is { } c)
            {
                a.CastId = c.Ability;
                a.CastEndsTick = c.Ends;
            }
            else if (a.CastEndsTick is { } ends && (ends < t || CutBefore(a.Id, t)))
            {
                a.CastId = null;
                a.CastEndsTick = null;
            }
        }
    }
}
