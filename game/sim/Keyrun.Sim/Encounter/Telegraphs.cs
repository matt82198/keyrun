using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// An active telegraph (sim/encounter/telegraphs.ts Telegraph): a shape on the floor that resolves, hitting
/// whoever stands in it, on <see cref="ResolvesTick"/>. Immutable; drift (Tide) produces a copy via <c>with</c>.
/// </summary>
/// <param name="Id">Run-minted id (EncounterState.nextTelegraphId), never a process-global counter.</param>
/// <param name="SourceId">Caster; never hit by its own telegraph.</param>
/// <param name="AbilityId">The cast this telegraph warns of (the event's <c>cast</c> field).</param>
/// <param name="Shape">"circle", "cone" or "line"; like the TS, any other value resolves as a line.</param>
/// <param name="Origin">Origin, mm.</param>
/// <param name="Yaw">Caster yaw copied verbatim (the shared <see cref="Facing"/> convention).</param>
/// <param name="ResolvesTick">Tick at which it resolves (when <c>ResolvesTick &lt;= state.Tick</c>).</param>
public sealed record Telegraph(int Id, int SourceId, string AbilityId, string Shape, Vec3 Origin, int Yaw, int ResolvesTick)
{
    /// <summary>Circle and cone radius, mm (absent = 0 when resolving).</summary>
    public long? Radius { get; init; }

    /// <summary>Full cone angle in 1/1024 turn (absent = 0); containment assumes it is below 512.</summary>
    public int? Angle1024 { get; init; }

    /// <summary>Line length, mm (absent = 0).</summary>
    public long? Length { get; init; }

    /// <summary>Line full width, mm (absent = 0).</summary>
    public long? Width { get; init; }
}

/// <summary>One resolved telegraph and the living, non-source actors it hit, ascending by id.</summary>
/// <param name="TelegraphId">The telegraph.</param>
/// <param name="HitActorIds">Hit actors, ascending.</param>
public sealed record TelegraphHit(int TelegraphId, IReadOnlyList<int> HitActorIds);

/// <summary>The registry after one <see cref="Telegraphs.Advance"/>.</summary>
/// <param name="Telegraphs">Still-pending telegraphs, in registry order.</param>
/// <param name="NextTelegraphId">The next id to mint.</param>
/// <param name="Hits">Telegraphs resolved this tick.</param>
public sealed record TelegraphAdvance(IReadOnlyList<Telegraph> Telegraphs, int NextTelegraphId, IReadOnlyList<TelegraphHit> Hits);

/// <summary>
/// Port of sim/encounter/telegraphs.ts (integer geometry and resolution) plus the telegraph part of
/// sim/encounter/index.ts stepEncounter (registerTelegraph, resolve, emit <c>telegraph_hit</c>, drop resolved).
/// Integer only: squared distances, no sqrt, facing from the shared <see cref="Facing"/> table.
/// Products such as <c>dot * dot</c> reach about 1e15, so all of it is <c>long</c>.
/// </summary>
public static class Telegraphs
{
    private const long Scale = 4096;
    private const long Quarter = 256;

    // cos(i * 8 / 1024 turn) for i in 0..32, scaled by 4096 (telegraphs.ts COS_Q, verbatim).
    private static readonly int[] CosQ =
    {
        4096, 4091, 4076, 4052, 4017, 3973, 3920, 3857, 3784, 3703, 3612, 3513, 3406, 3290, 3166, 3035,
        2896, 2751, 2598, 2440, 2276, 2106, 1931, 1751, 1567, 1380, 1189, 995, 799, 601, 401, 201, 0,
    };

    /// <summary>
    /// The cone's cos(half angle): telegraphs.ts takes <c>halfAngle = clamp(angle1024, 0, 511) / 2</c> (a
    /// non-integer for odd angles), clamps it to a quarter turn, and indexes <c>Math.round(halfAngle / 8)</c>,
    /// capped at 32. Kept exact as RoundHalfUp(clampedAngle, 16).
    /// </summary>
    public static long CosHalf(long angle1024)
    {
        var twiceHalf = IntMath.Clamp(angle1024, 0, 511); // = 2 * halfAngle, exact
        if (twiceHalf > 2 * Quarter)
        {
            twiceHalf = 2 * Quarter; // half-angle assumed <= 90 degrees (never reached: 511 / 2 < 256)
        }
        var idx = Math.Min(CosQ.Length - 1, IntMath.RoundHalfUp(twiceHalf, 16));
        return CosQ[idx];
    }

    /// <summary>XZ point-in-circle, rim included.</summary>
    public static bool PointInCircle(Vec3 origin, long radius, Vec3 p)
    {
        var dx = p.X - origin.X;
        var dz = p.Z - origin.Z;
        return (dx * dx) + (dz * dz) <= radius * radius;
    }

    /// <summary>
    /// XZ point-in-cone facing <paramref name="yaw"/> with full angle <paramref name="angle1024"/> (&lt; 512):
    /// inside the radius, not behind, and <c>dot^2 &gt;= distSq * cosHalf^2</c>. The origin itself is a hit.
    /// </summary>
    public static bool PointInCone(Vec3 origin, long yaw, long angle1024, long radius, Vec3 p)
    {
        var dx = p.X - origin.X;
        var dz = p.Z - origin.Z;
        var distSq = (dx * dx) + (dz * dz);
        if (distSq > radius * radius)
        {
            return false;
        }
        if (distSq == 0)
        {
            return true;
        }
        var f = Facing.FaceDir(yaw);
        var dot = (dx * f.X) + (dz * f.Z); // scaled by 4096
        if (dot < 0)
        {
            return false;
        }
        var cosHalf = CosHalf(angle1024);
        return dot * dot >= distSq * cosHalf * cosHalf;
    }

    /// <summary>XZ point-in-line: forward in [0, length] and |lateral| at most half the width (all scaled by 4096).</summary>
    public static bool PointInLine(Vec3 origin, long yaw, long length, long width, Vec3 p)
    {
        var dx = p.X - origin.X;
        var dz = p.Z - origin.Z;
        var f = Facing.FaceDir(yaw);
        var forward = (dx * f.X) + (dz * f.Z);
        var lateral = (dx * f.Z) - (dz * f.X);
        if (forward < 0 || forward > length * Scale)
        {
            return false;
        }
        return Math.Abs(lateral) * 2 <= width * Scale;
    }

    /// <summary>
    /// resolveTelegraphs: for every telegraph with <c>ResolvesTick &lt;= state.Tick</c>, in registry order, the
    /// living actors (ascending id, source excluded) inside it. Telegraphs not yet due are not listed.
    /// </summary>
    public static IReadOnlyList<TelegraphHit> Resolve(SimState state, IReadOnlyList<Telegraph> telegraphs)
    {
        var results = new List<TelegraphHit>();
        foreach (var tg in telegraphs)
        {
            if (tg.ResolvesTick > state.Tick)
            {
                continue;
            }
            var hit = new List<int>();
            foreach (var actor in state.Actors.Values)
            {
                if (!actor.Alive || actor.Id == tg.SourceId)
                {
                    continue;
                }
                bool inside;
                if (tg.Shape == "circle")
                {
                    inside = PointInCircle(tg.Origin, tg.Radius ?? 0, actor.Pos);
                }
                else if (tg.Shape == "cone")
                {
                    inside = PointInCone(tg.Origin, tg.Yaw, tg.Angle1024 ?? 0, tg.Radius ?? 0, actor.Pos);
                }
                else
                {
                    inside = PointInLine(tg.Origin, tg.Yaw, tg.Length ?? 0, tg.Width ?? 0, actor.Pos);
                }
                if (inside)
                {
                    hit.Add(actor.Id);
                }
            }
            results.Add(new TelegraphHit(tg.Id, hit));
        }
        return results;
    }

    private static JsValue Required(SimEvent e, string key) =>
        e.Get(key) ?? throw new ArgumentException($"telegraph event lacks '{key}'", nameof(e));

    private static long RequiredInt(SimEvent e, string key) =>
        Required(e, key) is JsInt i ? i.Value : throw new ArgumentException($"telegraph event '{key}' is not an integer", nameof(e));

    private static string RequiredString(SimEvent e, string key) =>
        Required(e, key) is JsString s ? s.Value : throw new ArgumentException($"telegraph event '{key}' is not a string", nameof(e));

    private static long? OptionalInt(SimEvent e, string key) => e.Get(key) switch
    {
        null => null,
        JsInt i => i.Value,
        _ => throw new ArgumentException($"telegraph event '{key}' is not an integer", nameof(e)),
    };

    private static int ToInt(long v, string key) =>
        v >= int.MinValue && v <= int.MaxValue ? (int)v : throw new ArgumentException($"telegraph event '{key}' out of range");

    /// <summary>
    /// sim/encounter/index.ts registerTelegraph: the Telegraph for one "telegraph" event (fields actorId, cast,
    /// shape, origin {x,y,z}, yaw, resolvesTick, optional radius / angle1024 / length / width).
    /// </summary>
    /// <exception cref="ArgumentException">A required field is missing or mistyped (the TS would carry undefined
    /// on into NaN geometry; this port fails closed).</exception>
    public static Telegraph Register(SimEvent telegraphEvent, int id)
    {
        var e = telegraphEvent;
        if (Required(e, "origin") is not JsObject o ||
            o.Get("x") is not JsInt ox || o.Get("y") is not JsInt oy || o.Get("z") is not JsInt oz)
        {
            throw new ArgumentException("telegraph event 'origin' must be {x,y,z} integers", nameof(telegraphEvent));
        }
        var angle = OptionalInt(e, "angle1024");
        return new Telegraph(
            id,
            ToInt(RequiredInt(e, "actorId"), "actorId"),
            RequiredString(e, "cast"),
            RequiredString(e, "shape"),
            new Vec3(ox.Value, oy.Value, oz.Value),
            ToInt(RequiredInt(e, "yaw"), "yaw"),
            ToInt(RequiredInt(e, "resolvesTick"), "resolvesTick"))
        {
            Radius = OptionalInt(e, "radius"),
            Angle1024 = angle is { } a ? ToInt(a, "angle1024") : null,
            Length = OptionalInt(e, "length"),
            Width = OptionalInt(e, "width"),
        };
    }

    /// <summary>
    /// One tick of the telegraph registry, in stepEncounter's order: register <paramref name="newTelegraphEvents"/>
    /// with consecutive ids from <paramref name="nextTelegraphId"/>, append them to <paramref name="active"/>,
    /// apply <paramref name="drift"/> (the Tide clause hook, lane 2C; identity when null), resolve, emit one
    /// <c>{tick, type:"telegraph_hit", telegraphId, actorIds}</c> per resolved telegraph, and drop the resolved.
    /// The "telegraph" events themselves are emitted by the caller (the encounter step), not here.
    /// </summary>
    public static TelegraphAdvance Advance(
        SimState state,
        IReadOnlyList<Telegraph> active,
        IReadOnlyList<SimEvent> newTelegraphEvents,
        int nextTelegraphId,
        Action<SimEvent> emit,
        Func<IReadOnlyList<Telegraph>, IReadOnlyList<Telegraph>>? drift = null)
    {
        var registered = newTelegraphEvents.Select((e, i) => Register(e, nextTelegraphId + i)).ToList();
        IReadOnlyList<Telegraph> all = active.Concat(registered).ToList();
        if (drift is not null)
        {
            all = drift(all);
        }
        var hits = Resolve(state, all);
        foreach (var hit in hits)
        {
            emit(new SimEvent(state.Tick, "telegraph_hit",
                ("telegraphId", JsValue.Int(hit.TelegraphId)),
                ("actorIds", JsValue.Arr(hit.HitActorIds.Select(id => JsValue.Int(id))))));
        }
        var resolved = new HashSet<int>(hits.Select(h => h.TelegraphId));
        var remaining = all.Where(t => !resolved.Contains(t.Id)).ToList();
        return new TelegraphAdvance(remaining, nextTelegraphId + registered.Count, hits);
    }
}
