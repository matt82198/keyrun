using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>A script cast's resolved target (script-target.ts:54 ResolveScriptTargetResult).</summary>
/// <param name="TargetId">The target, or null when none is needed or none was found.</param>
/// <param name="Blocked">True when a target was required and none was in range: the caller skips the cast this tick.</param>
public sealed record ScriptTargetResult(int? TargetId, bool Blocked);

/// <summary>
/// The targetId an enemy script's cast carries (sim/encounter/script-target.ts, the P0 targeting fix).
/// Default: a heal (any "heal" op) targets the lowest-health living ally enemy, else itself; anything else targets the
/// caster's highest-threat living player, else the nearest living player. A self-shaped ability targets itself. With
/// the ability known, candidates outside its range are excluded and a target-shaped cast with none left is blocked;
/// a known circle/cone/line needs no target. Overrides: Self, Threat, Random (a PRNG draw derived from state without
/// mutating it). Iteration is ascending by actor id, as JS integer-keyed objects iterate. HOLDER-LOCK (STATE.md D30):
/// a caster with a <see cref="ActorState.HolderId"/> skips the threat-then-nearest pick and goes at its holder
/// (see <see cref="Keyrun.Sim.Combat.Holder"/>); threat-then-nearest remains only for a caster with no holder.
/// </summary>
public static class ScriptTarget
{
    /// <summary>sim/combat/casting.ts:39 rangeMm: <c>isqrt(dx^2 + dz^2)</c> (y ignored).</summary>
    public static long RangeMm(ActorState a, ActorState b)
    {
        var dx = a.Pos.X - b.Pos.X;
        var dz = a.Pos.Z - b.Pos.Z;
        return Fixed.Isqrt((dx * dx) + (dz * dz));
    }

    // script-target.ts:65 inRange.
    private static bool InRange(ActorState caster, ActorState candidate, ScriptAbility? ability) =>
        ability is null || RangeMm(caster, candidate) <= ability.Range;

    // script-target.ts:69 livingPlayers / :73 livingAllies (Object.values: ascending id).
    private static List<ActorState> LivingPlayers(SimState state) =>
        state.Actors.Values.Where(a => a.Alive && a.Kind == ActorKind.Player).ToList();

    private static List<ActorState> LivingAllies(SimState state, int casterId) =>
        state.Actors.Values.Where(a => a.Alive && a.Kind == ActorKind.Enemy && a.Id != casterId).ToList();

    // script-target.ts:77 nearestId: by range, then id (stable).
    private static int NearestId(ActorState caster, List<ActorState> candidates) =>
        candidates.OrderBy(a => RangeMm(caster, a)).ThenBy(a => a.Id).First().Id;

    // script-target.ts:83 lowestHealthId: by health, then id.
    private static int LowestHealthId(List<ActorState> candidates) =>
        candidates.OrderBy(a => a.Health).ThenBy(a => a.Id).First().Id;

    // script-target.ts:87 highestThreatLivingId: walks caster.threat in ascending id; a strictly higher value wins,
    // a tie keeps the lower id.
    private static int? HighestThreatLivingId(ActorState caster, HashSet<int> candidateIds)
    {
        int? best = null;
        long bestVal = 0;
        foreach (var kv in caster.Threat)
        {
            if (!candidateIds.Contains(kv.Key))
            {
                continue;
            }
            if (best is null || kv.Value > bestVal || (kv.Value == bestVal && kv.Key < best.Value))
            {
                bestVal = kv.Value;
                best = kv.Key;
            }
        }
        return best;
    }

    // script-target.ts:104 randomId: seed [(p0 ^ tick) >>> 0, (p1 ^ casterId) >>> 0, p2, p3] (all-zero becomes
    // [1, ...]), then nextInt over the candidates sorted by id.
    private static int RandomId(SimState state, int casterId, List<ActorState> candidates)
    {
        var p = state.Prng;
        var seed = new PrngState(p.S0 ^ unchecked((uint)state.Tick), p.S1 ^ unchecked((uint)casterId), p.S2, p.S3);
        if (seed.S0 == 0 && seed.S1 == 0 && seed.S2 == 0 && seed.S3 == 0)
        {
            seed = seed with { S0 = 1 };
        }
        var sorted = candidates.OrderBy(a => a.Id).ToList();
        var (idx, _) = Prng.NextInt(seed, sorted.Count);
        return sorted[(int)idx].Id;
    }

    // script-target.ts:114 threatThenNearest.
    private static int ThreatThenNearest(ActorState caster, List<ActorState> candidates) =>
        HighestThreatLivingId(caster, new HashSet<int>(candidates.Select(a => a.Id))) ?? NearestId(caster, candidates);

    /// <summary>script-target.ts:119 resolveScriptTarget (rules in the class summary).</summary>
    /// <param name="state">The tick's state (read only).</param>
    /// <param name="caster">The casting enemy.</param>
    /// <param name="targetOverride">The step's override, if any.</param>
    /// <param name="ability">The ability, when known; null keeps the legacy never-block behaviour.</param>
    public static ScriptTargetResult Resolve(SimState state, ActorState caster, ScriptTargetOverride? targetOverride = null, ScriptAbility? ability = null)
    {
        if (targetOverride == ScriptTargetOverride.Self || ability?.ShapeKind == "self")
        {
            return new ScriptTargetResult(caster.Id, false);
        }
        var requiresTarget = ability?.ShapeKind == "target";
        var heal = ability is { Heals: true } && targetOverride != ScriptTargetOverride.Threat;
        if (ability is not null && !requiresTarget && !heal && targetOverride is null)
        {
            return new ScriptTargetResult(null, false); // circle/cone/line: no target needed
        }

        var pool = heal ? LivingAllies(state, caster.Id) : LivingPlayers(state);
        var candidates = pool.Where(a => InRange(caster, a, ability)).ToList();

        // HOLDER-LOCK (STATE.md D30, Combat/Holder.cs): a hostile pick (default or Threat override; not a heal, not
        // Random) goes at the locked holder only. A living holder out of range blocks the cast rather than switching
        // to a player in range; a dead holder falls to the nearest living candidate, never the top threat.
        if (!heal && targetOverride != ScriptTargetOverride.Random && caster.HolderId is { } holderId)
        {
            if (candidates.Any(a => a.Id == holderId))
            {
                return new ScriptTargetResult(holderId, false);
            }
            var holderLives = pool.Any(a => a.Id == holderId);
            if (!holderLives && candidates.Count > 0)
            {
                return new ScriptTargetResult(NearestId(caster, candidates), false);
            }
            return ability is not null ? new ScriptTargetResult(null, true) : new ScriptTargetResult(null, false);
        }

        if (candidates.Count > 0)
        {
            var picked = targetOverride == ScriptTargetOverride.Random
                ? RandomId(state, caster.Id, candidates)
                : heal ? LowestHealthId(candidates) : ThreatThenNearest(caster, candidates);
            return new ScriptTargetResult(picked, false);
        }
        if (heal)
        {
            return new ScriptTargetResult(caster.Id, false); // "else self"
        }
        if (ability is not null)
        {
            return new ScriptTargetResult(null, true); // target required, none in range
        }
        return new ScriptTargetResult(null, false); // ability unknown: legacy cast-anyway
    }
}
