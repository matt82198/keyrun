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
/// </list>
/// </remarks>
internal sealed class EncounterCastLedger
{
    private readonly IReadOnlyDictionary<string, int> _castTicks;
    private readonly List<(int Actor, string Ability, int Start, int Ends, int Cut)> _spans = new();

    /// <summary>Creates an empty ledger.</summary>
    /// <param name="castTicks">Each ability's cast time in ticks (0 for instant casts, which are never in flight).</param>
    public EncounterCastLedger(IReadOnlyDictionary<string, int> castTicks) => _castTicks = castTicks;

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
            Add(actor, ability, tick - c, tick);
        }
    }

    /// <summary>Records the cast the replay's encounter step issued for an enemy on <paramref name="tick"/> (its cast start).</summary>
    /// <param name="actor">The caster.</param>
    /// <param name="ability">The ability.</param>
    /// <param name="tick">The command tick.</param>
    public void Started(int actor, string ability, int tick)
    {
        var c = CastTicksOf(ability);
        if (c > 0)
        {
            Add(actor, ability, tick, tick + c);
        }
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
