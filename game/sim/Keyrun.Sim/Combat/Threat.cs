using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// Port of sim/combat/threat.ts: the per-actor threat table (threat generated against this actor, by source id).
/// <see cref="ActorState.Threat"/> is sorted by id, which is the order JS iterates integer record keys in.
/// </summary>
public static class Threat
{
    /// <summary>
    /// sim/combat/threat.ts:9-13 addThreat: adds <c>trunc(amount * mult / 10000)</c> (Math.trunc, :11), with
    /// <paramref name="multiplierBp"/> defaulting to 10000 (:10).
    /// </summary>
    /// <param name="actor">The actor whose table grows.</param>
    /// <param name="sourceId">Who generated the threat.</param>
    /// <param name="amount">Base threat.</param>
    /// <param name="multiplierBp">Multiplier in bp, or null for 100%.</param>
    public static void AddThreat(ActorState actor, int sourceId, long amount, long? multiplierBp = null)
    {
        var mult = multiplierBp ?? Effects.BpDenom;
        var delta = IntMath.TruncDiv(amount * mult, Effects.BpDenom);
        actor.Threat[sourceId] = (actor.Threat.TryGetValue(sourceId, out var cur) ? cur : 0) + delta;
    }

    /// <summary>
    /// sim/combat/threat.ts:15-27 highestThreatSource: the first id (ascending) holding the strictly greatest value,
    /// or null for an empty table. The TS starts from -Infinity, so the first entry always wins initially.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The top source, or null.</returns>
    public static int? HighestThreatSource(ActorState actor)
    {
        int? best = null;
        long bestVal = 0;
        foreach (var (id, val) in actor.Threat)
        {
            if (best is null || val > bestVal)
            {
                bestVal = val;
                best = id;
            }
        }
        return best;
    }

    /// <summary>sim/combat/threat.ts:29-31 clearThreat: deletes <paramref name="sourceId"/>'s entry.</summary>
    /// <param name="actor">The actor.</param>
    /// <param name="sourceId">The source to forget.</param>
    public static void ClearThreat(ActorState actor, int sourceId) => actor.Threat.Remove(sourceId);
}
