using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Bots.Shared;

/// <summary>
/// Port of bots/src/shared/positioning.ts: point-in-shape tests for circle, cone and line telegraphs, and the
/// small-grid nearest-safe-point search. Every coordinate is integer mm, passed through unconverted.
/// </summary>
/// <remarks>
/// The cone and line tests use doubles and trig exactly as the TS does (Math.sqrt, Math.atan2, Math.cos,
/// Math.sin, in the same operation order). sqrt is correctly rounded on both runtimes; atan2/cos/sin may
/// differ from V8 in the last ulp, which can only flip a point lying on a shape boundary to within ~1e-12 rad.
/// </remarks>
public static class Positioning
{
    /// <summary>positioning.ts angle1024ToRad: <c>(units * 2 * PI) / 1024</c>.</summary>
    /// <param name="units">1/1024 turns.</param>
    /// <returns>Radians.</returns>
    public static double Angle1024ToRad(double units) => (units * 2 * Math.PI) / 1024;

    /// <summary>positioning.ts pointInCircle: <c>dx*dx + dz*dz &lt;= r*r</c> (boundary inclusive).</summary>
    /// <param name="p">Point.</param>
    /// <param name="center">Centre.</param>
    /// <param name="radiusMm">Radius.</param>
    /// <returns>True when inside or on the circle.</returns>
    public static bool PointInCircle(PointMm p, PointMm center, long radiusMm)
    {
        var dx = p.X - center.X;
        var dz = p.Z - center.Z;
        return (dx * dx) + (dz * dz) <= radiusMm * radiusMm;
    }

    /// <summary>positioning.ts pointInCone: apex at <paramref name="center"/>, opening along <paramref name="yawRad"/>, full width <paramref name="angle1024"/>, out to <paramref name="lengthMm"/>.</summary>
    /// <param name="p">Point.</param>
    /// <param name="center">Apex.</param>
    /// <param name="yawRad">Facing, radians.</param>
    /// <param name="angle1024">Full width, 1/1024 turn.</param>
    /// <param name="lengthMm">Reach.</param>
    /// <returns>True when inside the cone.</returns>
    public static bool PointInCone(PointMm p, PointMm center, double yawRad, double angle1024, double lengthMm)
    {
        double dx = p.X - center.X;
        double dz = p.Z - center.Z;
        var dist = Math.Sqrt((dx * dx) + (dz * dz));
        if (dist > lengthMm)
        {
            return false;
        }
        if (dist == 0)
        {
            return true;
        }
        var pointAngle = Math.Atan2(dz, dx);
        var delta = pointAngle - yawRad;
        while (delta > Math.PI)
        {
            delta -= 2 * Math.PI;
        }
        while (delta < -Math.PI)
        {
            delta += 2 * Math.PI;
        }
        var halfWidthRad = Angle1024ToRad(angle1024) / 2;
        return Math.Abs(delta) <= halfWidthRad;
    }

    /// <summary>positioning.ts pointInLine: a rectangle from <paramref name="center"/>, <paramref name="lengthMm"/> along <paramref name="yawRad"/>, <paramref name="widthMm"/> wide.</summary>
    /// <param name="p">Point.</param>
    /// <param name="center">Start.</param>
    /// <param name="yawRad">Facing, radians.</param>
    /// <param name="lengthMm">Length.</param>
    /// <param name="widthMm">Width.</param>
    /// <returns>True when inside the rectangle.</returns>
    public static bool PointInLine(PointMm p, PointMm center, double yawRad, double lengthMm, double widthMm)
    {
        double dx = p.X - center.X;
        double dz = p.Z - center.Z;
        var forward = (dx * Math.Cos(yawRad)) + (dz * Math.Sin(yawRad));
        var side = (-dx * Math.Sin(yawRad)) + (dz * Math.Cos(yawRad));
        return forward >= 0 && forward <= lengthMm && Math.Abs(side) <= widthMm / 2;
    }

    /// <summary>
    /// positioning.ts inTelegraph: <paramref name="p"/> inside the telegraph's shape. Yaw is converted from
    /// 1/1024 turns to radians; a cone's reach is its radius (only a line reads length/width).
    /// </summary>
    /// <param name="p">Point.</param>
    /// <param name="tg">Telegraph.</param>
    /// <returns>True when inside.</returns>
    public static bool InTelegraph(PointMm p, BotTelegraph tg)
    {
        ArgumentNullException.ThrowIfNull(tg);
        var center = tg.Center;
        var yawRad = Angle1024ToRad(tg.Yaw ?? 0);
        return tg.Shape switch
        {
            TelegraphShape.Circle => PointInCircle(p, center, tg.Radius ?? 0),
            TelegraphShape.Cone => PointInCone(p, center, yawRad, tg.Angle1024 ?? 0, tg.Radius ?? 0),
            _ => PointInLine(p, center, yawRad, tg.Length ?? 0, tg.Width ?? 0),
        };
    }

    /// <summary>positioning.ts isInAnyDanger: inside any telegraph flagged <c>danger</c>.</summary>
    /// <param name="p">Point.</param>
    /// <param name="telegraphs">Telegraphs.</param>
    /// <returns>True when in danger.</returns>
    public static bool IsInAnyDanger(PointMm p, IEnumerable<BotTelegraph> telegraphs)
    {
        ArgumentNullException.ThrowIfNull(telegraphs);
        return telegraphs.Any(tg => tg.Danger && InTelegraph(p, tg));
    }

    /// <summary>
    /// DEAD-CASTER-TELEGRAPH (no TS counterpart; design/BIBLE.md section 3, a dead enemy has no cast): true when
    /// <paramref name="tg"/>'s caster is dead (or gone) and holds no cast in progress. A script telegraph is announced
    /// leadTicks before its cast fires and a dead actor's timed steps never fire (Scripts.cs), so such a telegraph
    /// warns of nothing although the sim keeps it until its resolvesTick. A dead caster still mid-cast keeps it:
    /// Rules completes that cast regardless. Telegraphs with no <see cref="BotTelegraph.SourceId"/> are never spent.
    /// </summary>
    /// <param name="tg">Telegraph.</param>
    /// <param name="state">The view's sim state.</param>
    /// <returns>True when the telegraph can be ignored.</returns>
    public static bool IsSpent(BotTelegraph tg, Keyrun.Sim.Core.SimState state)
    {
        ArgumentNullException.ThrowIfNull(tg);
        ArgumentNullException.ThrowIfNull(state);
        if (tg.SourceId is not { } sourceId)
        {
            return false;
        }
        return !state.Actors.TryGetValue(sourceId, out var caster) || (!caster.Alive && caster.CastId is null);
    }

    /// <summary>
    /// positioning.ts nearestSafePoint: searches outward square rings (step <paramref name="gridStepMm"/>) around
    /// <paramref name="from"/> for the nearest point in no danger telegraph that passes <paramref name="extraCheck"/>.
    /// Each ring's candidates are visited in squared-distance order, ties in generation order (JS sort is stable).
    /// </summary>
    /// <param name="from">Start.</param>
    /// <param name="telegraphs">Telegraphs (only <c>danger</c> ones count).</param>
    /// <param name="gridStepMm">Grid step.</param>
    /// <param name="maxRingsSearched">Ring limit.</param>
    /// <param name="extraCheck">Extra predicate (default: always true).</param>
    /// <returns><paramref name="from"/> when it already qualifies, the first qualifying point, or null.</returns>
    public static PointMm? NearestSafePoint(
        PointMm from,
        IReadOnlyList<BotTelegraph> telegraphs,
        long gridStepMm = 500,
        int maxRingsSearched = 20,
        Func<PointMm, bool>? extraCheck = null)
    {
        ArgumentNullException.ThrowIfNull(telegraphs);
        var check = extraCheck ?? (_ => true);
        if (!IsInAnyDanger(from, telegraphs) && check(from))
        {
            return from;
        }
        for (var ring = 1; ring <= maxRingsSearched; ring++)
        {
            var candidates = new List<PointMm>();
            for (var i = -ring; i <= ring; i++)
            {
                for (var j = -ring; j <= ring; j++)
                {
                    if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring)
                    {
                        continue;
                    }
                    candidates.Add(new PointMm(from.X + (i * gridStepMm), from.Z + (j * gridStepMm)));
                }
            }
            foreach (var c in candidates.OrderBy(c => DistSq(c, from)))
            {
                if (!IsInAnyDanger(c, telegraphs) && check(c))
                {
                    return c;
                }
            }
        }
        return null;
    }

    private static long DistSq(PointMm a, PointMm b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }
}
