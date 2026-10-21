using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>Arena bounds, an XZ axis-aligned box in mm (sim/combat/movement.ts Bounds).</summary>
/// <param name="MinX">Lowest x.</param>
/// <param name="MaxX">Highest x.</param>
/// <param name="MinZ">Lowest z.</param>
/// <param name="MaxZ">Highest z.</param>
public readonly record struct Bounds(long MinX, long MaxX, long MinZ, long MaxZ);

/// <summary>
/// The arena movement rules a host feeds <see cref="Movement"/>: walk speed (sim/combat/rules.ts
/// RulesConfig.baseSpeedMmPerTick), the equal actor radius for push-out, and the arena bounds. The host
/// builds it from content (Keyrun.Content run.json); the sim holds no default.
/// </summary>
public sealed record MovementConfig
{
    /// <summary>Builds a config, rejecting values the movement rules cannot use.</summary>
    /// <param name="BaseSpeedMmPerTick">Walk speed for an actor with no per-actor speed, mm/tick; must be positive.</param>
    /// <param name="ActorRadiusMm">Actor cylinder radius, mm; must be positive.</param>
    /// <param name="Bounds">Arena bounds; MinX &lt; MaxX and MinZ &lt; MaxZ.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is non-positive or the bounds are empty.</exception>
    public MovementConfig(long BaseSpeedMmPerTick, long ActorRadiusMm, Bounds Bounds)
    {
        if (BaseSpeedMmPerTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BaseSpeedMmPerTick), BaseSpeedMmPerTick, "walk speed must be positive");
        }
        if (ActorRadiusMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ActorRadiusMm), ActorRadiusMm, "actor radius must be positive");
        }
        if (Bounds.MinX >= Bounds.MaxX || Bounds.MinZ >= Bounds.MaxZ)
        {
            throw new ArgumentOutOfRangeException(nameof(Bounds), Bounds, "bounds need MinX < MaxX and MinZ < MaxZ");
        }
        this.BaseSpeedMmPerTick = BaseSpeedMmPerTick;
        this.ActorRadiusMm = ActorRadiusMm;
        this.Bounds = Bounds;
    }

    /// <summary>Walk speed for an actor with no <see cref="ActorState.SpeedMmPerTick"/>, mm/tick.</summary>
    public long BaseSpeedMmPerTick { get; }

    /// <summary>Actor cylinder radius, mm.</summary>
    public long ActorRadiusMm { get; }

    /// <summary>Arena bounds.</summary>
    public Bounds Bounds { get; }
}

/// <summary>
/// Port of sim/combat/movement.ts: integer movement in mm/tick from move commands (with a basis-point
/// movement modifier), equal-radius actor push-out in the XZ plane, and bounds clamping. Every division is
/// the TS <c>Math.trunc</c>, i.e. C# integer <c>/</c> (ORACLE.md section 8, movement.ts:46-48, :61, :67-68).
/// </summary>
public static class Movement
{
    /// <summary>
    /// Space dash (placeholder; no recorded decision, and design/BIBLE.md does not define it): a dash command moves this many mm in
    /// one tick through <see cref="ApplyMovement(ActorState, Dir2, long, long, int)"/>, so movement modifiers scale it.
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

    /// <summary>
    /// A move command under <paramref name="config"/>: the actor's own <see cref="ActorState.SpeedMmPerTick"/>
    /// when set, else the config walk speed (rules.ts:114 <c>actor.speedMmPerTick ?? cfg.baseSpeedMmPerTick</c>).
    /// </summary>
    public static void ApplyMovement(ActorState actor, Dir2 dir, MovementConfig config, long movementModBp, int yaw)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(config);
        ApplyMovement(actor, dir, actor.SpeedMmPerTick ?? config.BaseSpeedMmPerTick, movementModBp, yaw);
    }

    /// <summary>Push-out at the config actor radius.</summary>
    public static void ResolveCollision(ActorState a, ActorState b, MovementConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ResolveCollision(a, b, config.ActorRadiusMm);
    }

    /// <summary>Clamp into the config arena bounds.</summary>
    public static void ClampToBounds(ActorState actor, MovementConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ClampToBounds(actor, config.Bounds);
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

    /// <summary>
    /// D54 (defer to WoW: a pack stacks around the player it attacks, it does not queue in single file): two overlapping
    /// enemies on one target part symmetrically (<see cref="ResolveCollision(ActorState, ActorState, long)"/>), then the
    /// one farther from the target (the blocked one; the higher id on a tie) also slides sideways around it by the
    /// overlap, perpendicular to its line to the target, away from the nearer one's bearing (by id parity when the two
    /// stand on one line through the target: odd ids counterclockwise). A pack on one line, as PRESPAWN-AGGRO places a
    /// pull, therefore fans around its target instead of pressing the front one in. Integer only.
    /// </summary>
    /// <param name="a">One enemy.</param>
    /// <param name="b">The other enemy.</param>
    /// <param name="target">Where their shared target stands.</param>
    /// <param name="radiusMm">The equal actor radius, mm.</param>
    public static void ResolveCollisionAround(ActorState a, ActorState b, Vec3 target, long radiusMm)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var dx = b.Pos.X - a.Pos.X;
        var dz = b.Pos.Z - a.Pos.Z;
        var dist2 = (dx * dx) + (dz * dz);
        var minDist = radiusMm * 2;
        if (dist2 >= minDist * minDist)
        {
            return;
        }
        var overlap = minDist - Isqrt(dist2);
        ResolveCollision(a, b, radiusMm);
        long D2(ActorState s) => ((s.Pos.X - target.X) * (s.Pos.X - target.X)) + ((s.Pos.Z - target.Z) * (s.Pos.Z - target.Z));
        var (front, rear) = D2(a) < D2(b) || (D2(a) == D2(b) && a.Id < b.Id) ? (a, b) : (b, a);
        var rx = rear.Pos.X - target.X;
        var rz = rear.Pos.Z - target.Z;
        var len = Isqrt((rx * rx) + (rz * rz));
        if (len == 0)
        {
            return;
        }
        var fx = front.Pos.X - target.X;
        var fz = front.Pos.Z - target.Z;
        var cross = (fx * rz) - (fz * rx);
        var side = cross > 0 ? 1 : cross < 0 ? -1 : (rear.Id % 2 != 0 ? 1 : -1);
        // (-rz, rx) is the counterclockwise perpendicular of the rear's bearing (cross(r, perp) > 0).
        rear.Pos = rear.Pos with
        {
            X = rear.Pos.X + (side * IntMath.TruncDiv(-rz * overlap, len)),
            Z = rear.Pos.Z + (side * IntMath.TruncDiv(rx * overlap, len)),
        };
    }

    /// <summary>
    /// D54 ENEMIES-NEVER-DISPLACE (defer to WoW: a mob chasing or meleeing a player never pushes them; it closes to melee
    /// and stacks on its target): moves <paramref name="mover"/> out of <paramref name="anchor"/> by the WHOLE overlap
    /// along their XZ delta and leaves <paramref name="anchor"/> where it stands. Coincident centres move the mover
    /// +x by the full contact distance (the symmetric rule's tie direction). Integer only.
    /// </summary>
    /// <param name="anchor">The actor that never moves (the player).</param>
    /// <param name="mover">The actor pushed out (the enemy).</param>
    /// <param name="radiusMm">The equal actor radius, mm.</param>
    public static void ResolveCollisionMoverOnly(ActorState anchor, ActorState mover, long radiusMm)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(mover);
        var dx = mover.Pos.X - anchor.Pos.X;
        var dz = mover.Pos.Z - anchor.Pos.Z;
        var dist2 = (dx * dx) + (dz * dz);
        var minDist = radiusMm * 2;
        if (dist2 >= minDist * minDist)
        {
            return;
        }
        var dist = Isqrt(dist2);
        if (dist == 0)
        {
            mover.Pos = mover.Pos with { X = mover.Pos.X + minDist };
            return;
        }
        var overlap = minDist - dist;
        mover.Pos = mover.Pos with
        {
            X = mover.Pos.X + IntMath.TruncDiv(dx * overlap, dist),
            Z = mover.Pos.Z + IntMath.TruncDiv(dz * overlap, dist),
        };
    }

    /// <summary>Clamps the actor's x and z into <paramref name="bounds"/> (y untouched).</summary>
    public static void ClampToBounds(ActorState actor, Bounds bounds)
    {
        actor.Pos = ClampPoint(actor.Pos, bounds);
    }

    /// <summary>True when <paramref name="p"/>'s x and z lie in <paramref name="bounds"/>, edges included.</summary>
    public static bool Contains(Bounds bounds, Vec3 p) =>
        p.X >= bounds.MinX && p.X <= bounds.MaxX && p.Z >= bounds.MinZ && p.Z <= bounds.MaxZ;

    /// <summary><paramref name="p"/> with x and z clamped into <paramref name="bounds"/> (y untouched).</summary>
    public static Vec3 ClampPoint(Vec3 p, Bounds bounds) => p with
    {
        X = Math.Min(bounds.MaxX, Math.Max(bounds.MinX, p.X)),
        Z = Math.Min(bounds.MaxZ, Math.Max(bounds.MinZ, p.Z)),
    };

    /// <summary>
    /// The walkable-space rule (STATE.md D18; no TS counterpart, run only for a layout run): walkable space is a list of
    /// open boxes that overlap at doorways. A move to <paramref name="post"/> is kept when <paramref name="post"/> lies
    /// in any open box; otherwise it is clamped into the first open box that held <paramref name="pre"/>, the position
    /// before the move. When no open box held <paramref name="pre"/> (an actor placed outside the open space), it is
    /// clamped into the open box nearest <paramref name="post"/> (lowest index on a tie). A list of one box is exactly
    /// <see cref="ClampToBounds(ActorState, Bounds)"/>.
    /// </summary>
    /// <param name="pre">Position before the move.</param>
    /// <param name="post">Position after the move.</param>
    /// <param name="open">The open boxes, in layout order; must not be empty.</param>
    /// <returns>The accepted position.</returns>
    /// <exception cref="ArgumentException"><paramref name="open"/> is empty.</exception>
    public static Vec3 ClampToWalkable(Vec3 pre, Vec3 post, IReadOnlyList<Bounds> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (open.Count == 0)
        {
            throw new ArgumentException("walkable space needs at least one open box", nameof(open));
        }
        for (var i = 0; i < open.Count; i++)
        {
            if (Contains(open[i], post))
            {
                return post;
            }
        }
        for (var i = 0; i < open.Count; i++)
        {
            if (Contains(open[i], pre))
            {
                return ClampPoint(post, open[i]);
            }
        }
        var best = 0;
        long bestD2 = long.MaxValue;
        for (var i = 0; i < open.Count; i++)
        {
            var c = ClampPoint(post, open[i]);
            var dx = c.X - post.X;
            var dz = c.Z - post.Z;
            var d2 = (dx * dx) + (dz * dz);
            if (d2 < bestD2)
            {
                best = i;
                bestD2 = d2;
            }
        }
        return ClampPoint(post, open[best]);
    }
}
