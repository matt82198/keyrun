using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// Port of sim/combat/targeting.ts: integer circle/cone/line queries in the XZ plane, the shared
/// ability-shape resolver, and tab-target cycling. Facing comes from <see cref="Facing"/> (the same 128-entry
/// Q12 tables, ported by lane 1C). Products such as <c>dot</c> and <c>cosHalf * dist</c> are long.
/// </summary>
public static class Targeting
{
    /// <summary>Q12 facing scale (sim/combat/targeting.ts:28 FACING_Q).</summary>
    public const long FacingQ = Facing.Q;

    /// <summary>sim/combat/targeting.ts:57-60 faceDir.</summary>
    /// <param name="yaw1024">Yaw, 1/1024 turn.</param>
    /// <returns>The Q12 facing vector.</returns>
    public static Dir2 FaceDir(long yaw1024) => Facing.FaceDir(yaw1024);

    /// <summary>sim/combat/targeting.ts:62-71 inCircle: ids with <c>dx^2 + dz^2 &lt;= r^2</c>, in input order.</summary>
    /// <param name="origin">Centre.</param>
    /// <param name="radiusMm">Radius.</param>
    /// <param name="actors">Candidates.</param>
    /// <returns>Matching ids.</returns>
    public static List<int> InCircle(Vec3 origin, long radiusMm, IEnumerable<ActorState> actors)
    {
        var r2 = radiusMm * radiusMm;
        return actors
            .Where(a =>
            {
                var dx = a.Pos.X - origin.X;
                var dz = a.Pos.Z - origin.Z;
                return (dx * dx) + (dz * dz) <= r2;
            })
            .Select(a => a.Id)
            .ToList();
    }

    /// <summary>
    /// sim/combat/targeting.ts:73-89 inCone: excludes the caster and anything at distance 0 or beyond the radius
    /// (:79, :83); keeps <c>dot &gt;= cosHalf * isqrt(dist2)</c> (:84-86) where cosHalf is the table cos at
    /// <c>tableIndex(angle1024 / 2)</c> (:75), the half-angle kept exact as a numerator over 2.
    /// </summary>
    /// <param name="caster">The caster (origin and facing).</param>
    /// <param name="angle1024">Full cone angle.</param>
    /// <param name="radiusMm">Radius.</param>
    /// <param name="actors">Candidates.</param>
    /// <returns>Matching ids, in input order.</returns>
    public static List<int> InCone(ActorState caster, long angle1024, long radiusMm, IEnumerable<ActorState> actors)
    {
        var face = Facing.FaceDir(caster.Yaw);
        var cosHalf = Facing.Cos(Facing.TableIndexOfHalf(angle1024));
        var r2 = radiusMm * radiusMm;
        return actors
            .Where(a =>
            {
                if (a.Id == caster.Id)
                {
                    return false;
                }
                var dx = a.Pos.X - caster.Pos.X;
                var dz = a.Pos.Z - caster.Pos.Z;
                var dist2 = (dx * dx) + (dz * dz);
                if (dist2 == 0 || dist2 > r2)
                {
                    return false;
                }
                var dist = Movement.Isqrt(dist2);
                var dot = (face.X * dx) + (face.Z * dz);
                return dot >= cosHalf * dist;
            })
            .Select(a => a.Id)
            .ToList();
    }

    /// <summary>
    /// sim/combat/targeting.ts:92-104 inLine: <paramref name="radiusMm"/> is the half-width; forward and perp are
    /// <c>Math.trunc(... / FACING_Q)</c> (:99-100, TruncDiv); keeps <c>0 &lt;= forward &lt;= length</c> and
    /// <c>|perp| &lt;= radius</c>. Excludes the caster.
    /// </summary>
    /// <param name="caster">The caster.</param>
    /// <param name="length">Line length.</param>
    /// <param name="radiusMm">Half-width.</param>
    /// <param name="actors">Candidates.</param>
    /// <returns>Matching ids, in input order.</returns>
    public static List<int> InLine(ActorState caster, long length, long radiusMm, IEnumerable<ActorState> actors)
    {
        var face = Facing.FaceDir(caster.Yaw);
        return actors
            .Where(a =>
            {
                if (a.Id == caster.Id)
                {
                    return false;
                }
                var dx = a.Pos.X - caster.Pos.X;
                var dz = a.Pos.Z - caster.Pos.Z;
                var forward = IntMath.TruncDiv((face.X * dx) + (face.Z * dz), FacingQ);
                var perp = IntMath.TruncDiv((face.X * dz) - (face.Z * dx), FacingQ);
                return forward >= 0 && forward <= length && System.Math.Abs(perp) <= radiusMm;
            })
            .Select(a => a.Id)
            .ToList();
    }

    /// <summary>
    /// sim/combat/targeting.ts:123-148 resolveTargets. "self" is the caster (:124). "target" is the live actor at
    /// targetId, else nothing (:125-128). circle/cone/line query the LIVE actors in ascending id order (:129-137,
    /// radius/angle/length default 0), keep those matching <paramref name="affinity"/> (affinity.ts:92: hostile
    /// keeps the other kind, friendly the caster's kind), add a live caster for a friendly ability (:143), and
    /// return them sorted by id (:144-147).
    /// </summary>
    /// <remarks>
    /// C# deviation in signature only: the TS reads the ability's Affinity from affinity.ts's module-level
    /// registry inside this call; lane 2B owns Affinity.cs, so the caller passes <c>abilityAffinity(def)</c> here.
    /// C# deviation in behaviour (CAST-RANGE-RECHECK, D28 the C# sim is the oracle): "target" also requires the
    /// target to be within <c>def.Range</c> NOW, so a cast that completes after its target walked out of range lands
    /// on nothing (CompleteCastOn still emits cast_complete, exactly as for a target that died mid-cast). StartCast
    /// checks the same range at start; moving out of a telegraphed cast is counterplay (design/BIBLE.md section 3).
    /// C# deviation in behaviour (CHOIR-PACK-HEAL; no TS counterpart, an oracle run has no idle actor): a FRIENDLY
    /// circle/cone/line skips idle actors, so the Choir Cantor's pack heal reaches its own fight and never a
    /// PRESPAWN-AGGRO group still waiting in a later room. Hostile shapes still reach idle actors (damage engages them).
    /// </remarks>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The ability.</param>
    /// <param name="affinity">The ability's affinity (affinity.ts abilityAffinity).</param>
    /// <returns>The actors it applies to.</returns>
    public static List<ActorState> ResolveTargets(SimState state, ActorState actor, AbilityDef def, Affinity affinity) =>
        ResolveTargetsOn(state, actor, def, affinity, Casting.CastTargetOf(actor));

    /// <summary>
    /// <see cref="ResolveTargets"/> with the "target" shape resolving on
    /// <paramref name="castTargetId"/> (an ended cast's <see cref="Casting.TargetOfEnded"/>,
    /// R2) instead of the actor's live cast.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The ability.</param>
    /// <param name="affinity">The ability's affinity (affinity.ts abilityAffinity).</param>
    /// <param name="castTargetId">The target the cast resolves on, or null.</param>
    /// <returns>The actors it applies to.</returns>
    public static List<ActorState> ResolveTargetsOn(SimState state, ActorState actor, AbilityDef def, Affinity affinity, int? castTargetId)
    {
        var shape = def.Shape;
        if (shape.Kind == ShapeKind.Self)
        {
            return new List<ActorState> { actor };
        }
        if (shape.Kind == ShapeKind.Target)
        {
            // CAST-KEEPS-START-TARGET (f-bd8fbbb1): the cast's start target, not a mid-cast retarget (Casting.SetTarget).
            if (castTargetId is int tid && state.Actors.TryGetValue(tid, out var t) && t.Alive && !t.Evading && Casting.RangeMm(actor, t) <= def.Range)
            {
                return new List<ActorState> { t };
            }
            return new List<ActorState>();
        }
        var all = state.Actors.Values.Where(a => a.Alive).ToList();
        var ids = shape.Kind switch
        {
            ShapeKind.Circle => InCircle(CircleOrigin(state, actor, def, affinity), shape.Radius ?? 0, all),
            ShapeKind.Cone => InCone(actor, shape.Angle1024 ?? 0, shape.Radius ?? 0, all),
            _ => InLine(actor, shape.Length ?? 0, shape.Radius ?? 0, all),
        };
        var kept = new SortedSet<int>(ids.Where(id =>
            state.Actors.TryGetValue(id, out var candidate) &&
            (affinity == Affinity.Hostile ? candidate.Kind != actor.Kind && !candidate.Evading : candidate.Kind == actor.Kind && !candidate.Idle)));
        if (affinity == Affinity.Friendly && actor.Alive)
        {
            kept.Add(actor.Id);
        }
        return kept.Where(state.Actors.ContainsKey).Select(id => state.Actors[id]).ToList();
    }

    /// <summary>The ability tag that marks a single-target spell turned into an area burst (content talent/facet patches).</summary>
    public const string AreaBurstTag = "aoe";

    /// <summary>
    /// Where a circle is centred. The TS (targeting.ts:130) always centres on the caster. C# deviation in behaviour
    /// (WILDFIRE-NEVER-ENDS, D28 the C# sim is the oracle): a HOSTILE circle tagged <see cref="AreaBurstTag"/> with a
    /// live enemy target within <c>def.Range</c> bursts AT that target. Only the patches that turn a ranged
    /// single-target spell into an area carry the tag on a hostile ability (Cinder Wildfire, "Flare becomes a small
    /// area instead of single target", and the Widening Flare facet); centred on the caster, Wildfire's 1.5 m burst
    /// landed on nothing from Flare's 25 m range, so it dealt no damage and opened no Kindled, and an all-Cinder bot
    /// party never finished the run. Untagged circles (Cinderfield, enemy slams) and friendly circles (Group Song,
    /// heals) still centre on the caster, and with no valid target the burst falls back to the caster.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The circle ability.</param>
    /// <param name="affinity">The ability's affinity.</param>
    /// <returns>The circle's centre.</returns>
    public static Vec3 CircleOrigin(SimState state, ActorState actor, AbilityDef def, Affinity affinity)
    {
        if (affinity == Affinity.Hostile &&
            def.Tags is { } tags && tags.Contains(AreaBurstTag) &&
            actor.TargetId is int tid &&
            state.Actors.TryGetValue(tid, out var t) &&
            t.Alive &&
            t.Kind != actor.Kind &&
            Casting.RangeMm(actor, t) <= def.Range)
        {
            return t.Pos;
        }
        return actor.Pos;
    }

    /// <summary>
    /// sim/combat/targeting.ts:151-158 cycleTarget: the next live id after <paramref name="fromId"/> in ascending
    /// order, wrapping; the lowest when <paramref name="fromId"/> is null or not a live candidate; null when none live.
    /// </summary>
    /// <param name="actors">Candidates.</param>
    /// <param name="fromId">Current target, or null.</param>
    /// <returns>The next target id, or null.</returns>
    public static int? CycleTarget(IEnumerable<ActorState> actors, int? fromId)
    {
        var candidates = actors.Where(a => a.Alive).Select(a => a.Id).OrderBy(x => x).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }
        if (fromId is null)
        {
            return candidates[0];
        }
        var idx = candidates.IndexOf(fromId.Value);
        var nextIdx = idx == -1 ? 0 : (idx + 1) % candidates.Count;
        return candidates[nextIdx];
    }
}
