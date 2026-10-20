using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// One trash pack of a layout run (PATH-PACKS, BIBLE section 9.1; layout.json <c>packs</c>): its members, where its aggro
/// ring stands and how wide it is. A pack is never a pull: it has no pull index, no room gate and no clear the encounter
/// waits on. Its members are worth forces like any enemy (<see cref="Forces"/>).
/// </summary>
/// <param name="Id">Pack id (layout.json; never a pull id).</param>
/// <param name="Spawns">Its members, placed in the world (actor ids unique in the run).</param>
/// <param name="Center">Aggro ring centre, mm.</param>
/// <param name="AggroMm">Aggro ring radius, mm.</param>
public sealed record PackGroup(string Id, IReadOnlyList<PullSpawn> Spawns, Vec3 Center, long AggroMm);

/// <summary>
/// PATH-PACKS (new behaviour, no TS oracle; layout runs only): a pack pulls as one group. On the first tick a living party
/// member (not an open seat) stands within a pack's aggro radius (inclusive, integer squared distance in XZ), every
/// member of that pack enters the state together and <c>{tick, type:"pack_engage", packId, actorIds}</c> is emitted. A
/// pack engages once; it never moves the pull index and never shuts a gate. Integers only, no randomness.
/// </summary>
/// <remarks>
/// This is main's spawn-on-engage form. PRESPAWN-AGGRO (PR #308) stands every group idle from tick 0 instead; there a
/// <see cref="PackGroup"/> becomes one more <c>Aggro.GroupsOf</c> entry with pull index -1, which already emits the same
/// <c>pack_engage</c> event.
/// </remarks>
public static class LayoutPacks
{
    /// <summary>True when <paramref name="p"/> is within <paramref name="radiusMm"/> of <paramref name="center"/> in XZ (inclusive).</summary>
    /// <param name="p">The point.</param>
    /// <param name="center">The ring centre.</param>
    /// <param name="radiusMm">The ring radius, mm.</param>
    /// <returns>True inside or on the ring.</returns>
    public static bool InRing(Vec3 p, Vec3 center, long radiusMm)
    {
        var dx = p.X - center.X;
        var dz = p.Z - center.Z;
        return (dx * dx) + (dz * dz) <= radiusMm * radiusMm;
    }

    /// <summary>True once any member of <paramref name="pack"/> is in the state (actors are never removed).</summary>
    /// <param name="state">The state.</param>
    /// <param name="pack">The pack.</param>
    /// <returns>True when the pack has engaged.</returns>
    public static bool IsSpawned(SimState state, PackGroup pack)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(pack);
        return pack.Spawns.Any(s => state.Actors.ContainsKey(s.ActorId));
    }

    /// <summary>
    /// The packs that engage on <paramref name="state"/>: every pack not yet spawned with a living player actor (not one of
    /// <paramref name="openSeats"/>) inside its ring. In <paramref name="packs"/> order.
    /// </summary>
    /// <param name="state">The post-move state.</param>
    /// <param name="packs">The run's packs.</param>
    /// <param name="openSeats">Abandoned seats' actor ids, or null.</param>
    /// <returns>The packs to spawn this tick.</returns>
    public static IReadOnlyList<PackGroup> Triggered(SimState state, IReadOnlyList<PackGroup> packs, IReadOnlyList<int>? openSeats = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(packs);
        var players = state.Actors.Values
            .Where(a => a.Kind == ActorKind.Player && a.Alive && (openSeats is null || !openSeats.Contains(a.Id)))
            .ToList();
        return packs
            .Where(p => p.Spawns.Count > 0 && !IsSpawned(state, p) && players.Any(a => InRing(a.Pos, p.Center, p.AggroMm)))
            .ToList();
    }

    /// <summary>
    /// <paramref name="pack"/>'s members as new actors (full health, untargeted, exactly as a pull spawns them), after
    /// emitting <c>{tick, type:"pack_engage", packId, actorIds}</c>.
    /// </summary>
    /// <param name="pack">The pack.</param>
    /// <param name="tick">The engage tick.</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>The new actors, for the caller to merge.</returns>
    public static IReadOnlyList<ActorState> Spawn(PackGroup pack, int tick, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(emit);
        emit(new SimEvent(tick, "pack_engage",
            ("packId", JsValue.Str(pack.Id)),
            ("actorIds", JsValue.Arr(pack.Spawns.Select(s => JsValue.Int(s.ActorId))))));
        // The pull spawner builds each actor (full health, untargeted); its own pull_start is dropped, a pack is no pull.
        return Pulls.SpawnPull(new DungeonDef(pack.Id, new[] { new PullDef(pack.Id, pack.Spawns) }), 0, tick, static _ => { });
    }
}
