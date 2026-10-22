using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// One enemy group the aggro step watches (a teaching pull, the boss pull or an optional pack).
/// </summary>
/// <param name="Id">Group id (a pull id or a pack id; one namespace, so links can name either).</param>
/// <param name="PullIndex">The pull's index in the dungeon, or -1 for a pack (a pack never moves the pull index).</param>
/// <param name="ActorIds">The group's actor ids.</param>
/// <param name="Center">Where the aggro ring stands this tick, mm.</param>
/// <param name="AggroMm">Aggro radius, mm.</param>
/// <param name="Links">Groups that engage with this one.</param>
/// <param name="ActivateRoom">KF3 Seed spawn gate: when set, the group is dormant (see <see cref="ActorState.Dormant"/>) and
/// engages only on the first tick a living party member stands inside this room box, ignoring its aggro ring and any
/// damage. Null for every other group.</param>
public sealed record AggroGroup(string Id, int PullIndex, IReadOnlyList<int> ActorIds, Vec3 Center, long AggroMm, IReadOnlyList<string> Links, Combat.Bounds? ActivateRoom = null);

/// <summary>
/// PRESPAWN-AGGRO (C#-only, layout runs; BIBLE section 9.1): every group stands idle from tick 0 and engages, whole,
/// on the first tick a living party member is within its aggro radius (inclusive, integer squared distance in XZ), or
/// when any member takes damage (from the party or a hazard), and a linked group engages with it. A pull's engage
/// emits its <c>pull_start</c>; a pack's emits <c>pack_engage {packId, actorIds}</c>. Integers only, no randomness.
/// KF3 Seed spawn gate (BIBLE section 9.6; finding f-70c82aca): a group in <see cref="RoomActivatedPullIds"/> is dormant
/// until a party member stands inside its room, and only the room engages it, never its ring or a cast from outside.
/// </summary>
public static class Aggro
{
    /// <summary>
    /// The pulls that wait for the party to enter their room instead of engaging on their aggro ring (KF3 Seed spawn gate;
    /// the Mythic+ rule that a pull is inert until the party reaches its area). Only the Seed: its room is the layout's
    /// seed-chamber. A pull named here with no room in the layout keeps the ring rule.
    /// </summary>
    public static readonly IReadOnlySet<string> RoomActivatedPullIds = new HashSet<string>(StringComparer.Ordinal) { "seed" };

    /// <summary>
    /// KF3 Seed spawn gate: marks every actor of a room-activated pull (that the layout gives a room) as dormant, on a
    /// layout run's freshly spawned state. Mutates <paramref name="state"/> in place (the run root owns it).
    /// </summary>
    /// <param name="state">The state the pulls were spawned into.</param>
    /// <param name="dungeon">The dungeon.</param>
    /// <param name="layout">The run's layout.</param>
    public static void MarkDormant(SimState state, DungeonDef dungeon, NavLayout layout)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(layout);
        foreach (var pull in dungeon.Pulls.Where(p => RoomActivatedPullIds.Contains(p.Id) && layout.RoomFor(p.Id) is not null))
        {
            foreach (var spawn in pull.Spawns)
            {
                if (state.Actors.TryGetValue(spawn.ActorId, out var actor) && actor.Idle)
                {
                    actor.Dormant = true;
                }
            }
        }
    }

    /// <summary>True when <paramref name="p"/> is within <paramref name="radiusMm"/> of <paramref name="center"/> in XZ (inclusive).</summary>
    public static bool InRing(Vec3 p, Vec3 center, long radiusMm)
    {
        var dx = p.X - center.X;
        var dz = p.Z - center.Z;
        return (dx * dx) + (dz * dz) <= radiusMm * radiusMm;
    }

    /// <summary>
    /// The run's groups on <paramref name="tick"/>: the dungeon's pulls in pull order, then every layout pack
    /// (PATH-PACKS, required and optional) in <paramref name="packs"/> order with pull index -1, its ring at the pack
    /// centre and no links. A pack stands idle from tick 0 like a pull (<see cref="LayoutPacks.SpawnAllIdle"/>) and
    /// engages, whole, by the same proximity and damage rule, emitting <c>pack_engage</c>.
    /// </summary>
    /// <param name="dungeon">The dungeon.</param>
    /// <param name="tick">The tick (where a patrol's ring stands).</param>
    /// <param name="packs">The run's layout packs (<c>RunContent.Packs</c>), or null for none.</param>
    /// <param name="layout">The run's layout, or null: a room-activated pull (<see cref="RoomActivatedPullIds"/>) gets its
    /// room box as <see cref="AggroGroup.ActivateRoom"/> only when a layout gives it one.</param>
    /// <param name="patrols">The run's patrol packs (<c>RunContent.Patrols</c>), or null for none (MPLUS-PATROLS).</param>
    /// <returns>Pull groups, then pack groups, then patrol groups; the last two with pull index -1.</returns>
    public static IReadOnlyList<AggroGroup> GroupsOf(DungeonDef dungeon, int tick, IReadOnlyList<PackGroup>? packs = null, NavLayout? layout = null, IReadOnlyList<PatrolPack>? patrols = null)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        var groups = dungeon.Pulls
            .Select((p, i) => new AggroGroup(
                p.Id,
                i,
                p.Spawns.Select(s => s.ActorId).ToList(),
                Pulls.RingCenter(p),
                p.AggroMm,
                p.Links,
                RoomActivatedPullIds.Contains(p.Id) ? layout?.RoomFor(p.Id)?.Box : null))
            .ToList();
        if (packs is not null)
        {
            groups.AddRange(packs.Select(k => new AggroGroup(k.Id, -1, k.Spawns.Select(s => s.ActorId).ToList(), k.Center, k.AggroMm, Array.Empty<string>())));
        }
        if (patrols is not null)
        {
            // MPLUS-PATROLS: a patrol's ring stands on its point at this tick (Patrols.PointAt); it never moves the pull index.
            groups.AddRange(patrols.Select(k => new AggroGroup(k.Id, -1, k.Spawns.Select(s => s.ActorId).ToList(), Patrols.PointAt(k, tick), k.AggroMm, Array.Empty<string>())));
        }
        return groups;
    }

    /// <summary>
    /// The ids of the groups that engage this tick: every not-yet-engaged group with a living party member (not an open
    /// seat) in its ring in <paramref name="after"/>, or with a member whose health in <paramref name="after"/> is below
    /// its health in <paramref name="before"/>; then every group linked to one of those (either direction, transitively).
    /// In group order.
    /// </summary>
    /// <param name="before">The state before the tick.</param>
    /// <param name="after">The state after the tick's combat.</param>
    /// <param name="groups">The groups.</param>
    /// <param name="encounter">The encounter (its engaged set).</param>
    /// <param name="openSeats">Abandoned seats' actor ids, or null.</param>
    public static IReadOnlyList<string> Triggered(SimState before, SimState after, IReadOnlyList<AggroGroup> groups, EncounterState encounter, IReadOnlyList<int>? openSeats = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(encounter);
        var players = after.Actors.Values
            .Where(a => a.Kind == ActorKind.Player && a.Alive && (openSeats is null || !openSeats.Contains(a.Id)))
            .ToList();
        var hit = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            if (Pulls.IsEngaged(encounter, g.Id) || g.ActorIds.Count == 0)
            {
                continue;
            }
            if (g.ActivateRoom is { } room)
            {
                // KF3 Seed spawn gate: the room alone engages a dormant group (no ring, no damage path: it is not a target).
                if (players.Any(p => Combat.Movement.Contains(room, p.Pos)))
                {
                    hit.Add(g.Id);
                }
                continue;
            }
            if (players.Any(p => InRing(p.Pos, g.Center, g.AggroMm)) || g.ActorIds.Any(id => TookDamage(before, after, id)))
            {
                hit.Add(g.Id);
            }
        }
        // Links chain-pull: symmetric and transitive.
        var changed = hit.Count > 0;
        while (changed)
        {
            changed = false;
            foreach (var g in groups)
            {
                if (hit.Contains(g.Id) || Pulls.IsEngaged(encounter, g.Id))
                {
                    continue;
                }
                var linked = g.Links.Any(hit.Contains) || groups.Any(o => hit.Contains(o.Id) && o.Links.Contains(g.Id, StringComparer.Ordinal));
                if (linked)
                {
                    hit.Add(g.Id);
                    changed = true;
                }
            }
        }
        return groups.Where(g => hit.Contains(g.Id)).Select(g => g.Id).ToList();
    }

    private static bool TookDamage(SimState before, SimState after, int actorId) =>
        before.Actors.TryGetValue(actorId, out var was) && after.Actors.TryGetValue(actorId, out var now) &&
        (now.Health < was.Health || (was.Alive && !now.Alive));

    /// <summary>
    /// Engages <paramref name="ids"/>: each member of <paramref name="after"/> stops being idle (mutated in place: the
    /// run root owns this freshly stepped state), a pull emits <c>{tick, type:"pull_start", pullIndex, pullId, actorIds}</c>
    /// and a pack <c>{tick, type:"pack_engage", packId, actorIds}</c>, and the encounter records the engage.
    /// </summary>
    /// <param name="after">The post-combat state.</param>
    /// <param name="encounter">The encounter.</param>
    /// <param name="groups">The groups.</param>
    /// <param name="ids">The groups to engage (from <see cref="Triggered"/>).</param>
    /// <param name="tick">The engage tick.</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>The encounter with the groups appended to <see cref="EncounterState.Engaged"/>.</returns>
    public static EncounterState Engage(SimState after, EncounterState encounter, IReadOnlyList<AggroGroup> groups, IReadOnlyList<string> ids, int tick, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(emit);
        if (ids.Count == 0)
        {
            return encounter;
        }
        var engaged = encounter.Engaged.ToList();
        foreach (var id in ids)
        {
            var g = groups.First(x => string.Equals(x.Id, id, StringComparison.Ordinal));
            foreach (var actorId in g.ActorIds)
            {
                if (after.Actors.TryGetValue(actorId, out var a))
                {
                    a.Idle = false;
                    a.Dormant = false;
                }
            }
            var actorIds = JsValue.Arr(g.ActorIds.Select(a => JsValue.Int(a)));
            emit(g.PullIndex >= 0
                ? new SimEvent(tick, "pull_start", ("pullIndex", JsValue.Int(g.PullIndex)), ("pullId", JsValue.Str(g.Id)), ("actorIds", actorIds))
                : new SimEvent(tick, "pack_engage", ("packId", JsValue.Str(g.Id)), ("actorIds", actorIds)));
            engaged.Add(id);
        }
        return encounter with { Engaged = engaged };
    }
}
