using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>Arena bounds, an XZ axis-aligned box in mm (sim/combat/movement.ts Bounds).</summary>
/// <param name="MinX">Lowest x.</param>
/// <param name="MaxX">Highest x.</param>
/// <param name="MinZ">Lowest z.</param>
/// <param name="MaxZ">Highest z.</param>
public readonly record struct Bounds(long MinX, long MaxX, long MinZ, long MaxZ);

/// <summary>
/// Port of sim/combat/movement.ts: integer movement in mm/tick from move commands (with a basis-point
/// movement modifier), equal-radius actor push-out in the XZ plane, and bounds clamping. Every division is
/// the TS <c>Math.trunc</c>, i.e. C# integer <c>/</c> (ORACLE.md section 8, movement.ts:46-48, :61, :67-68).
/// </summary>
public static class Movement
{
    /// <summary>
    /// Space dash (D10 placeholder, design/BIBLE.md does not define it): a dash command moves this many mm in
    /// one tick through <see cref="ApplyMovement"/>, so movement modifiers scale it.
    /// </summary>
    public const long DashDistanceMm = 3000;

    /// <summary>Integer square root with the movement.ts guard: n &lt;= 0 returns 0 (callers pass squared distances).</summary>
    public static long Isqrt(long n) => n <= 0 ? 0 : Fixed.Isqrt(n);

    /// <summary>
    /// Faces <paramref name="yaw"/>, then steps along <paramref name="dir"/> at
    /// <c>max(0, trunc(base * (10000 + mod) / 10000))</c> mm, each component <c>trunc(dir * speed / |dir|)</c>.
    /// A zero direction only turns the actor.
    /// </summary>
    public static void ApplyMovement(ActorState actor, Dir2 dir, long baseSpeedMmPerTick, long movementModBp, int yaw)
    {
        actor.Yaw = yaw;
        var len2 = (dir.X * dir.X) + (dir.Z * dir.Z);
        if (len2 == 0)
        {
            return;
        }
        var len = Isqrt(len2);
        if (len == 0)
        {
            return;
        }
        var speed = Math.Max(0, IntMath.TruncDiv(baseSpeedMmPerTick * (Fixed.BpScale + movementModBp), Fixed.BpScale));
        var dx = IntMath.TruncDiv(dir.X * speed, len);
        var dz = IntMath.TruncDiv(dir.Z * speed, len);
        actor.Pos = new Vec3(actor.Pos.X + dx, actor.Pos.Y, actor.Pos.Z + dz);
    }

    /// <summary>Pushes two overlapping equal-radius actor cylinders apart symmetrically along their XZ delta.</summary>
    public static void ResolveCollision(ActorState a, ActorState b, long radiusMm)
    {
        var dx = b.Pos.X - a.Pos.X;
        var dz = b.Pos.Z - a.Pos.Z;
        var dist2 = (dx * dx) + (dz * dz);
        var minDist = radiusMm * 2;
        if (dist2 >= minDist * minDist)
        {
            return;
        }
        var dist = Isqrt(dist2);
        if (dist == 0)
        {
            var half = IntMath.TruncDiv(minDist, 2);
            a.Pos = a.Pos with { X = a.Pos.X - half };
            b.Pos = b.Pos with { X = b.Pos.X + half };
            return;
        }
        var overlap = minDist - dist;
        var pushX = IntMath.TruncDiv(dx * overlap, 2 * dist);
        var pushZ = IntMath.TruncDiv(dz * overlap, 2 * dist);
        a.Pos = a.Pos with { X = a.Pos.X - pushX, Z = a.Pos.Z - pushZ };
        b.Pos = b.Pos with { X = b.Pos.X + pushX, Z = b.Pos.Z + pushZ };
    }

    /// <summary>Clamps the actor's x and z into <paramref name="bounds"/> (y untouched).</summary>
    public static void ClampToBounds(ActorState actor, Bounds bounds)
    {
        actor.Pos = actor.Pos with
        {
            X = Math.Min(bounds.MaxX, Math.Max(bounds.MinX, actor.Pos.X)),
            Z = Math.Min(bounds.MaxZ, Math.Max(bounds.MinZ, actor.Pos.Z)),
        };
    }
}
