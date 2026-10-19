using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using C = Keyrun.Content;

namespace Keyrun.Bots.Shared;

/// <summary>
/// The content layout (game/content/data/layout.json, <see cref="C.LayoutDef"/>) as the sim's <see cref="NavLayout"/>.
/// It lives here because Keyrun.Bots is the one Godot-free assembly that sees both Keyrun.Content and Keyrun.Sim: the
/// host (HostRunSetup) arms runs with it, the bots read it to follow, and the route tests use the same conversion.
/// </summary>
public static class NavLayouts
{
    /// <summary>The sim layout of <paramref name="layout"/>: its boxes (rooms emit room_enter), pull triggers and route.</summary>
    /// <param name="layout">A validated content layout.</param>
    /// <returns>The sim layout.</returns>
    public static NavLayout ToSim(C.LayoutDef layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var boxes = layout.Boxes
            .Select(b => new NavBox(b.Id, new Bounds(b.MinX, b.MaxX, b.MinZ, b.MaxZ), b.Kind == C.LayoutBoxKind.Room, b.Gate))
            .ToList();
        var triggers = layout.Boxes
            .Where(b => b.Pull is not null && b.Trigger is not null)
            .Select(b => new NavTrigger(b.Pull!, new Bounds(b.Trigger!.MinX, b.Trigger.MaxX, b.Trigger.MinZ, b.Trigger.MaxZ)))
            .ToList();
        var route = layout.Route.Select(p => new Vec3(p.X, 0, p.Z)).ToList();
        return new NavLayout(boxes, triggers, route);
    }

    /// <summary>
    /// <paramref name="dungeon"/> with every pull moved into its room: a pull's spawns stand at the room origin plus the
    /// spawn offset the content carries (so formation shapes never change); the boss pull (<see cref="C.LayoutDef.BossPullId"/>)
    /// stands at the arena origin. Fails closed on a pull with no room.
    /// </summary>
    /// <param name="layout">The content layout.</param>
    /// <param name="dungeon">The sim dungeon with content spawn offsets.</param>
    /// <returns>The placed dungeon.</returns>
    /// <exception cref="InvalidOperationException">A pull has no room origin in the layout.</exception>
    public static DungeonDef PlaceDungeon(C.LayoutDef layout, DungeonDef dungeon)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(dungeon);
        var pulls = dungeon.Pulls.Select(pull =>
        {
            var room = RoomOf(layout, pull.Id);
            var origin = room.Origin!;
            var boss = string.Equals(pull.Id, C.LayoutDef.BossPullId, StringComparison.Ordinal);
            var spawns = pull.Spawns.Select(s => s with
            {
                Pos = boss ? new Vec3(origin.X, s.Pos.Y, origin.Z) : new Vec3(origin.X + s.Pos.X, s.Pos.Y, origin.Z + s.Pos.Z),
            }).ToList();
            return pull with { Spawns = spawns };
        }).ToList();
        return dungeon with { Pulls = pulls };
    }

    /// <summary>
    /// <paramref name="party"/> moved to where a run beginning at <paramref name="firstPullId"/> starts: the layout
    /// start for the first pull's room, else that pull's room start (a <c>--pull N</c> run starts in its fight). Each
    /// member keeps its offset from the first member (the tank slot) and faces +x, along the route.
    /// </summary>
    /// <param name="layout">The content layout.</param>
    /// <param name="party">The party (first member = the tank slot).</param>
    /// <param name="firstPullId">The run's first pull.</param>
    /// <returns>The placed party.</returns>
    /// <exception cref="InvalidOperationException">The pull has no room.</exception>
    public static IReadOnlyList<Keyrun.Sim.Runs.PartyMemberSpec> PlaceParty(C.LayoutDef layout, IReadOnlyList<Keyrun.Sim.Runs.PartyMemberSpec> party, string firstPullId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(party);
        if (party.Count == 0)
        {
            return party;
        }
        var firstRoom = layout.Boxes.First(b => b.Pull is not null);
        var start = string.Equals(firstRoom.Pull, firstPullId, StringComparison.Ordinal) ? layout.Start : RoomOf(layout, firstPullId).Start!;
        var anchor = party[0].Pos;
        return party.Select(m => m with
        {
            Pos = new Vec3(start.X + (m.Pos.X - anchor.X), m.Pos.Y, start.Z + (m.Pos.Z - anchor.Z)),
            Yaw = 0,
        }).ToList();
    }

    private static C.LayoutBox RoomOf(C.LayoutDef layout, string pullId) =>
        layout.Boxes.FirstOrDefault(b => string.Equals(b.Pull, pullId, StringComparison.Ordinal) && b.Origin is not null && b.Start is not null)
            ?? throw new InvalidOperationException($"layout has no room for pull \"{pullId}\"");
}

/// <summary>
/// Travel between rooms (STATE.md D18; no TS counterpart). A walker inside box k walks straight at route point k (the
/// doorway into box k+1), so it never cuts a corner of the walkable boxes. With no living enemy each bot walks the route
/// to its formation slot behind the leader (layout.json <c>formation</c>); the autorun Warden (ScriptedWarden) walks
/// the route forward with <see cref="WalkRoute"/>. Moves are built with <see cref="Moves.MoveToward"/>, so they pass the
/// sim's dir/yaw validation.
/// </summary>
public static class Follow
{
    /// <summary>A walker this close to its point stops (no jitter on the spot), mm.</summary>
    public const long ArriveMm = 300;

    /// <summary>True when any enemy actor is alive (the party is fighting, so roles decide, not travel).</summary>
    /// <param name="state">The state.</param>
    /// <returns>True when an enemy lives.</returns>
    public static bool AnyLivingEnemy(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Actors.Values.Any(a => a.Alive && a.Kind == ActorKind.Enemy);
    }

    /// <summary>The party's leader: the living player actor with the lowest id (the human seat is actor 1).</summary>
    /// <param name="state">The state.</param>
    /// <returns>The leader, or null when no player lives.</returns>
    public static ActorState? Leader(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ActorState? best = null;
        foreach (var a in state.Actors.Values)
        {
            if (a.Alive && a.Kind == ActorKind.Player && (best is null || a.Id < best.Id))
            {
                best = a;
            }
        }
        return best;
    }

    /// <summary>
    /// The next point to walk at from <paramref name="from"/> toward <paramref name="goal"/> through the layout's boxes:
    /// the goal itself when both share a box, else the doorway out of the walker's box toward the goal's box (forward
    /// route point k, or backward route point k-1). Outside every box, the goal.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="from">The walker's position.</param>
    /// <param name="goal">Where it is going.</param>
    /// <returns>The point to walk straight at.</returns>
    public static Vec3 NextPoint(NavLayout layout, Vec3 from, Vec3 goal)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var (bLow, bHigh) = Span(layout, from);
        var (sLow, sHigh) = Span(layout, goal);
        if (bLow < 0 || sLow < 0 || (bLow <= sHigh && sLow <= bHigh))
        {
            return goal;
        }
        return bHigh < sLow ? layout.Route[bHigh] : layout.Route[bLow - 1];
    }

    /// <summary>A move from <paramref name="self"/> toward <paramref name="goal"/> along the boxes; null once within <paramref name="arriveMm"/>.</summary>
    /// <param name="tick">Command tick.</param>
    /// <param name="self">The walker.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="goal">Destination.</param>
    /// <param name="arriveMm">Stop distance.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? WalkTo(int tick, ActorState self, NavLayout layout, Vec3 goal, long arriveMm = ArriveMm)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (Distance2(self.Pos, goal) <= arriveMm * arriveMm)
        {
            return null;
        }
        var next = NextPoint(layout, self.Pos, goal);
        return Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(next));
    }

    /// <summary>
    /// A forward step along the route: inside box k (the highest box holding the walker) walk at route point k, so the
    /// walker crosses each room (and its trigger) to the next doorway and ends at the last box's goal point. Null at the
    /// goal. A gate that is still closed simply holds the walker at the doorway (the sim clamps the move).
    /// </summary>
    /// <param name="tick">Command tick.</param>
    /// <param name="self">The walker.</param>
    /// <param name="layout">The layout.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? WalkRoute(int tick, ActorState self, NavLayout layout)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(layout);
        var (_, high) = Span(layout, self.Pos);
        var target = layout.Route[Math.Max(0, high)];
        return Distance2(self.Pos, target) <= ArriveMm * ArriveMm
            ? null
            : Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(target));
    }

    /// <summary>
    /// Where <paramref name="classId"/> stands behind <paramref name="leader"/>: the formation slot (behind along -x,
    /// sideways in z), pulled into the leader's box when it falls outside every box. Null when the class has no slot.
    /// </summary>
    /// <param name="layout">The sim layout.</param>
    /// <param name="formation">The content formation.</param>
    /// <param name="leader">The leader.</param>
    /// <param name="classId">The follower's class.</param>
    /// <returns>The slot, or null.</returns>
    public static Vec3? SlotFor(NavLayout layout, IEnumerable<C.FormationSlot> formation, ActorState leader, string classId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(formation);
        ArgumentNullException.ThrowIfNull(leader);
        var slot = formation.FirstOrDefault(s => string.Equals(s.ClassId, classId, StringComparison.Ordinal));
        if (slot is null)
        {
            return null;
        }
        var p = new Vec3(leader.Pos.X - slot.BehindMm, leader.Pos.Y, leader.Pos.Z + slot.SideMm);
        if (layout.BoxIndexAt(p) >= 0)
        {
            return p;
        }
        var home = layout.BoxIndexAt(leader.Pos);
        return home >= 0 ? Movement.ClampPoint(p, layout.Boxes[home].Box) : leader.Pos;
    }

    /// <summary>
    /// The bot's travel move on a layout run (<see cref="BotView.Layout"/>) with the content formation in the view and a
    /// living leader other than the bot: a walk along the route to its formation slot behind the leader while no enemy
    /// lives, and during a fight only while the bot shares no box with the leader (it was left a room behind), so it
    /// catches up without leaving its role's fight position. Null otherwise (the roles decide).
    /// </summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="self">The bot.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? FollowCommand(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        if (view.Layout is not { } layout || view.Kits.Content?.Layout is not { } content)
        {
            return null;
        }
        if (Leader(view.State) is not { } leader || leader.Id == self.Id)
        {
            return null;
        }
        if (SlotFor(layout, content.Formation, leader, self.ClassId) is not { } slot)
        {
            return null;
        }
        if (AnyLivingEnemy(view.State) && SharesBox(layout, self.Pos, leader.Pos))
        {
            return null;
        }
        return WalkTo(view.State.Tick, self, layout, slot);
    }

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> lie in a common box (a doorway overlap counts for both sides).</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="a">One point.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when some box holds both.</returns>
    public static bool SharesBox(NavLayout layout, Vec3 a, Vec3 b)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var (aLow, aHigh) = Span(layout, a);
        var (bLow, bHigh) = Span(layout, b);
        return aLow >= 0 && bLow >= 0 && aLow <= bHigh && bLow <= aHigh;
    }

    private static (int Low, int High) Span(NavLayout layout, Vec3 p)
    {
        var low = -1;
        var high = -1;
        for (var i = 0; i < layout.Boxes.Count; i++)
        {
            if (Movement.Contains(layout.Boxes[i].Box, p))
            {
                if (low < 0)
                {
                    low = i;
                }
                high = i;
            }
        }
        return (low, high);
    }

    private static long Distance2(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }
}
