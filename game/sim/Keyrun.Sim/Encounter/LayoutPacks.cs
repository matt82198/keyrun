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
/// <param name="Links">R-SEED-1: the pull or pack ids that engage with this pack (layout.json <c>links</c>), or null for none.</param>
public sealed record PackGroup(string Id, IReadOnlyList<PullSpawn> Spawns, Vec3 Center, long AggroMm, IReadOnlyList<string>? Links = null);

/// <summary>
/// PATH-PACKS on PRESPAWN-AGGRO (new behaviour, no TS oracle; layout runs only; Matt 2026-10-20 "no more gates, just
/// path to boss with packs", "not literally spawning enemies on the player"): every pack, required and optional, stands
/// idle on the path from tick 0 exactly as a pull does, and is one more <see cref="Aggro.GroupsOf"/> entry with pull
/// index -1. It engages, whole, by the pulls' own proximity and damage rule (<see cref="Aggro.Triggered"/>) with one
/// <c>{tick, type:"pack_engage", packId, actorIds}</c>, never moves the pull index and never shuts a gate. Nothing ever
/// enters the state on a party member's ring step. Integers only, no randomness.
/// </summary>
public static class LayoutPacks
{
    /// <summary>
    /// Every member of <paramref name="packs"/> as an idle actor (full health, untargeted, as a pull spawns them), placed
    /// by the same non-overlapping row rule as an idle pull (<see cref="Pulls.SpawnAllIdle"/>) so members stacked on one
    /// offset stand apart from tick 0. In pack then spawn order.
    /// </summary>
    /// <param name="packs">The run's packs, or null for none.</param>
    /// <param name="actorRadiusMm">The run's actor radius, mm (the row spacing is twice this).</param>
    /// <returns>The idle pack actors, for the caller to merge.</returns>
    public static IReadOnlyList<ActorState> SpawnAllIdle(IReadOnlyList<PackGroup>? packs, long actorRadiusMm)
    {
        if (packs is null || packs.Count == 0)
        {
            return Array.Empty<ActorState>();
        }
        return Pulls.SpawnAllIdle(new DungeonDef("packs", packs.Select(p => new PullDef(p.Id, p.Spawns)).ToList()), actorRadiusMm);
    }
}
