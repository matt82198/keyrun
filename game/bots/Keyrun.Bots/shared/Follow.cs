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
        // D45: the other branch's boxes (content SideBoxes) are walkable and hold its pull rooms, off the route chain.
        var side = layout.SideBoxes
            .Select(b => new NavBox(b.Id, new Bounds(b.MinX, b.MaxX, b.MinZ, b.MaxZ), b.Kind == C.LayoutBoxKind.Room, null))
            .ToList();
        var triggers = layout.Boxes.Concat(layout.SideBoxes)
            .Where(b => b.Pull is not null && b.Trigger is not null)
            .Select(b => new NavTrigger(b.Pull!, new Bounds(b.Trigger!.MinX, b.Trigger.MaxX, b.Trigger.MinZ, b.Trigger.MaxZ)))
            .ToList();
        var route = layout.Route.Select(p => new Vec3(p.X, 0, p.Z)).ToList();
        return new NavLayout(boxes, triggers, route) { Side = side };
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
            // PRESPAWN-AGGRO: the room's per-group aggro radius (layout.json aggroMm), else the pull's own.
            // D45: a seeded branch room tags its pull with the branch (Pulls.CheckPullAdvance skips the untaken one).
            return pull with { Spawns = spawns, AggroMm = room.AggroMm ?? pull.AggroMm, Branch = room.Branch };
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
        layout.Boxes.Concat(layout.SideBoxes).FirstOrDefault(b => string.Equals(b.Pull, pullId, StringComparison.Ordinal) && b.Origin is not null && b.Start is not null)
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

    /// <summary>
    /// True when any engaged enemy actor is alive (the party is fighting, so roles decide, not travel). An idle group
    /// (PRESPAWN-AGGRO, BIBLE section 9.1: every group stands in its room from tick 0 until it engages) is not a fight.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>True when an engaged enemy lives.</returns>
    public static bool AnyLivingEnemy(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Actors.Values.Any(a => a.Alive && a.Kind == ActorKind.Enemy && !a.Idle);
    }

    /// <summary>
    /// The party's leader (PATH-TO-BOSS, STATE.md D44): the LIVING player actor in the lowest seat (the human seat is
    /// actor 1), the same seat <see cref="LeadCommand"/> leads from. A dead leader passes the lead on: the next living
    /// seat walks the path (LeadCommand) and the other bots follow it, so a fallen tank never strands the party on his
    /// body. Null when no player lives.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The leader, or null when no player lives.</returns>
    public static ActorState? Leader(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ActorState? best = null;
        foreach (var a in state.Actors.Values)
        {
            if (a.Kind == ActorKind.Player && a.Alive && (best is null || a.Id < best.Id))
            {
                best = a;
            }
        }
        return best;
    }

    /// <summary>
    /// Ticks a party's leader may stand still in the Entrance (box 0) with no engaged enemy before the bots take the lead
    /// (KF2, BOTS-ENGAGE-CHOIR). 200 ticks is 10 s at 20 Hz: a walking leader is out of the Entrance well before it.
    /// </summary>
    public const int IdleLeaderTicks = 200;

    /// <summary>
    /// True when the party's leader (<see cref="Leader"/>) has stood in the Entrance box (box 0) past
    /// <see cref="IdleLeaderTicks"/> with no engaged enemy alive, and another living player seat exists to take the lead.
    /// A human who never walks would otherwise hold every bot at the Entrance forever: the lead is the lowest living seat,
    /// and the bots only walk the route when that seat is a bot (<see cref="LeadCommand"/>). Stateless: it reads the view
    /// only (tick, positions, the layout), never the human's input.
    /// </summary>
    /// <param name="view">This tick's view.</param>
    /// <returns>True when the lead is handed over to the next living seat.</returns>
    public static bool LeadHandedOver(BotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Layout is not { } layout || AnyLivingEnemy(view.State) || view.State.Tick < IdleLeaderTicks)
        {
            return false;
        }
        if (Leader(view.State) is not { } leader || layout.BoxIndexAt(leader.Pos) != 0)
        {
            return false;
        }
        return view.State.Actors.Values.Any(a => a.Kind == ActorKind.Player && a.Alive && a.Id != leader.Id);
    }

    /// <summary>
    /// The lead the bots follow: <see cref="Leader"/>, or while <see cref="LeadHandedOver"/> holds, the lowest other living
    /// player seat (the bots lead, and the party follows them, while the lead seat stands still at the Entrance).
    /// </summary>
    /// <param name="view">This tick's view.</param>
    /// <returns>The leader, or null when no player lives.</returns>
    public static ActorState? LeaderOf(BotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var leader = Leader(view.State);
        if (leader is null || !LeadHandedOver(view))
        {
            return leader;
        }
        return view.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != leader.Id)
            .OrderBy(a => a.Id)
            .First();
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
        // R-PATH-1: the computed grid path (walls and props), not the box-chain doorway point (NextPoint).
        return PathMoves.Along(tick, self, layout, goal);
    }

    /// <summary>
    /// A forward step along the route: inside box k (the highest box holding the walker) walk at route point k, so the
    /// walker crosses each room (and its trigger) to the next doorway and ends at the last box's goal point. Null at the
    /// goal. The path is open to the boss (STATE.md D44): the packs along it, not gates, stop the walker, because walking
    /// into a pack's aggro ring starts a fight and the roles take over.
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
            : PathMoves.Along(tick, self, layout, target);
    }

    /// <summary>
    /// How far past a pull room's entry doorway (route point k-1) the leader holds while that room's pull waits for the
    /// party (LEADER-WAITS), mm. 4000 past the doorway, which stands 500 mm inside the room, is the room's layout start
    /// (entry wall + 4500 mm). The layout validator puts every spawn at least 11180 mm past the entry wall
    /// (trigger inset 2000 + clearance 6000 + overshoot 3180), so the hold is at least 6680 mm from every spawn, outside a
    /// default 4000 mm aggro ring (PRESPAWN-AGGRO); and the formation slots (2500 mm behind) fall 2000 mm inside the room,
    /// so the followers' arrival releases the leader into the ring.
    /// </summary>
    public const long HoldPastDoorMm = 4000;

    /// <summary>
    /// The scripted leader's travel (ScriptedWarden; STATE.md D27, LEADER-WAITS; PRESPAWN-AGGRO, BIBLE section 9.1 "bots
    /// pull on purpose, with the tank first"): <see cref="WalkRoute"/>, except that inside the room of the active pull,
    /// before that pull engages and while a living party member (not an open seat) is still outside the room, the leader
    /// walks to <see cref="HoldPoint"/> (outside the pull's aggro ring) and waits there instead of crossing to the far
    /// doorway. Once the party is in, the leader walks on along the route into the ring, so he engages the pull first with
    /// the party behind him. Null while holding at the point.
    /// </summary>
    /// <param name="tick">Command tick.</param>
    /// <param name="self">The leader.</param>
    /// <param name="run">The run (its layout, dungeon and active pull).</param>
    /// <param name="openSeats">Abandoned seats' actor ids (they never come in, so they are not waited for), or null.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? LeadRoute(int tick, ActorState self, Keyrun.Sim.Runs.Run run, IReadOnlyList<int>? openSeats = null)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(run);
        var layout = run.Content.Layout ?? throw new ArgumentException("the run has no layout", nameof(run));
        var index = run.Encounter.PullIndex;
        var (_, high) = Span(layout, self.Pos);
        if (high > 0 && index < run.Dungeon.Pulls.Count && !Pulls.IsEngaged(run.Encounter, run.Dungeon, index) &&
            layout.RoomFor(run.Dungeon.Pulls[index].Id) is { } room && string.Equals(room.Id, layout.Boxes[high].Id, StringComparison.Ordinal) &&
            !NavLayout.PartyInside(run.State, room.Box, openSeats))
        {
            var hold = HoldPoint(layout, high);
            return Distance2(self.Pos, hold) <= ArriveMm * ArriveMm
                ? null
                : PathMoves.Along(tick, self, layout, hold);
        }
        return WalkRoute(tick, self, layout);
    }

    /// <summary>
    /// The leader seat's travel when that seat is a bot (KF2, BOTS-ENGAGE-CHOIR): on a layout run, while no engaged enemy
    /// lives (PRESPAWN-AGGRO: an idle prespawned group is not a fight), the bot in the party's lowest LIVING player seat
    /// (<see cref="Leader"/>; a dead human leader passes the lead to it, STATE.md D44) walks the route like the scripted leader
    /// (<see cref="LeadRoute"/>): in a pull room whose pull has not engaged (every enemy standing in that room is alive
    /// and idle) while a living party member is still outside the room, it walks to <see cref="HoldPoint"/> (outside the
    /// pull's aggro ring) and waits there for the party; once the party is in, and anywhere else, it walks the route
    /// forward (<see cref="WalkRoute"/>), so it engages the pull first with the party behind it. The bot sees only the
    /// view (positions, idle flags, the layout); it never reads the run's pull index. Null for any other bot, off a
    /// layout run, in a fight, or while it holds at the point.
    /// </summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="self">The bot.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? LeadCommand(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        if (view.Layout is not { } layout || AnyLivingEnemy(view.State))
        {
            return null;
        }
        if (LeaderOf(view)?.Id != self.Id)
        {
            return null;
        }
        var tick = view.State.Tick;
        var (_, high) = Span(layout, self.Pos);
        // A handed-over lead (LeadHandedOver) does not wait for the party: the seat that stood still never comes in, so the
        // bots walk the route and engage the pull with the party behind them.
        if (!LeadHandedOver(view) && high > 0 && layout.Boxes[high] is { IsRoom: true } room && HasPull(layout, room) &&
            !view.State.Actors.Values.Any(a => a.Kind == ActorKind.Enemy && (!a.Alive || !a.Idle) && Movement.Contains(room.Box, a.Pos)) &&
            !NavLayout.PartyInside(view.State, room.Box))
        {
            var hold = HoldPoint(layout, high);
            return Distance2(self.Pos, hold) <= ArriveMm * ArriveMm ? null : PathMoves.Along(tick, self, layout, hold);
        }
        return WalkRoute(tick, self, layout);
    }

    private static bool HasPull(NavLayout layout, NavBox room) =>
        layout.Triggers.Any(t => layout.RoomFor(t.PullId) is { } r && string.Equals(r.Id, room.Id, StringComparison.Ordinal));

    /// <summary>
    /// Where the leader waits in box <paramref name="k"/> (k &gt; 0): route point k-1 (the doorway into the box) moved
    /// <see cref="HoldPastDoorMm"/> toward route point k (the far doorway). Integers only.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="k">The box index.</param>
    /// <returns>The hold point.</returns>
    public static Vec3 HoldPoint(NavLayout layout, int k)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var from = layout.Route[k - 1];
        var to = layout.Route[k];
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        var length = Movement.Isqrt((dx * dx) + (dz * dz));
        if (length <= HoldPastDoorMm)
        {
            return to;
        }
        return new Vec3(from.X + IntMath.TruncDiv(dx * HoldPastDoorMm, length), from.Y, from.Z + IntMath.TruncDiv(dz * HoldPastDoorMm, length));
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
        if (layout.InAnyBox(p))
        {
            return p;
        }
        // D45: the leader's box may be on the other branch (layout.Side), off the route chain.
        var home = layout.AllBoxes.LastOrDefault(b => Movement.Contains(b.Box, leader.Pos));
        return home is not null ? Movement.ClampPoint(p, home.Box) : leader.Pos;
    }

    /// <summary>
    /// The bot's travel move on a layout run (<see cref="BotView.Layout"/>) with the content formation in the view and a
    /// living leader (<see cref="Leader"/>, the lowest living seat) other than the bot: a walk along the route to its formation slot
    /// behind the leader while no enemy lives; during a fight with the leader in a room, a walk to its slot pulled <see cref="FightGoalInsetMm"/> inside
    /// that room while the bot stands outside the room shrunk by <see cref="FightInsetMm"/> (still in the corridor or the
    /// doorway, KF2c), and with the leader in no room only while the bot shares no box with him. A bot already in the
    /// leader's room whose slot falls outside it (the leader near the entry wall) walks to that slot pulled
    /// <see cref="FightInsetMm"/> inside the room instead, so it never steps back out to re-enter (JOINER-DOORWAY). Null
    /// otherwise (the roles decide).
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
        if (LeaderOf(view) is not { } leader || leader.Id == self.Id)
        {
            return null;
        }
        if (SlotFor(layout, content.Formation, leader, self.ClassId) is not { } slot)
        {
            return null;
        }
        if (AnyLivingEnemy(view.State))
        {
            // KF2c (design/BIBLE.md sections 4 and 9: the party lands every pull together): in a fight the bot first
            // closes into the room the leader fights in, past the doorway overlap, then its role takes over. A doorway
            // or the previous corridor is not "in the room".
            if (RoomAt(layout, leader.Pos) is { } room)
            {
                return Movement.Contains(Inset(room.Box, FightInsetMm), self.Pos)
                    ? null
                    : WalkTo(view.State.Tick, self, layout, FightGoal(room.Box, leader.Pos, slot));
            }
            if (SharesBox(layout, self.Pos, leader.Pos))
            {
                return null;
            }
        }
        if (RoomAt(layout, leader.Pos) is { } leaderRoom && Movement.Contains(leaderRoom.Box, self.Pos) && !Movement.Contains(leaderRoom.Box, slot))
        {
            // JOINER-DOORWAY: a bot already in the leader's room never steps back out through the doorway to a slot behind
            // a leader standing near the entry wall (it would re-enter, a second room_enter); it waits inside instead.
            return WalkTo(view.State.Tick, self, layout, Movement.ClampPoint(slot, Inset(leaderRoom.Box, FightInsetMm)));
        }
        return WalkTo(view.State.Tick, self, layout, slot);
    }

    /// <summary>
    /// In a fight a bot counts as in the leader's room once it stands this far inside every wall of the room box: past
    /// the 1000 mm doorway overlap a room shares with each corridor, so it is in the room, not in its door, mm.
    /// </summary>
    public const long FightInsetMm = 1500;

    /// <summary>
    /// Where a bot outside the leader's room walks in a fight: its formation slot pulled this far inside the room box
    /// (deeper than <see cref="FightInsetMm"/> by more than <see cref="ArriveMm"/>, so it stops inside, never on the line), mm.
    /// </summary>
    public const long FightGoalInsetMm = 2500;

    /// <summary>
    /// Where a bot walks to join a fight in <paramref name="room"/>: its formation slot when that lies
    /// <see cref="FightGoalInsetMm"/> inside the room; else (the leader holds the doorway, so "behind him" is the
    /// corridor) the leader's flank on the slot's side, as far out as the slot was from him (behind plus side, so the
    /// party never stacks on the tank), pulled <see cref="FightGoalInsetMm"/> inside the room. Integers only.
    /// </summary>
    /// <param name="room">The leader's room box.</param>
    /// <param name="leader">The leader's position.</param>
    /// <param name="slot">The bot's formation slot behind the leader.</param>
    /// <returns>The point to walk to.</returns>
    public static Vec3 FightGoal(Bounds room, Vec3 leader, Vec3 slot)
    {
        var inner = Inset(room, FightGoalInsetMm);
        if (Movement.Contains(inner, slot))
        {
            return slot;
        }
        var side = slot.Z - leader.Z;
        var reach = Math.Abs(leader.X - slot.X) + Math.Abs(side);
        var flank = new Vec3(leader.X, slot.Y, leader.Z + (side < 0 ? -reach : reach));
        return Movement.ClampPoint(flank, inner);
    }

    /// <summary>The room box holding <paramref name="p"/> (the first in layout order), or null in a corridor or outside every box.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="p">The point.</param>
    /// <returns>The room, or null.</returns>
    public static NavBox? RoomAt(NavLayout layout, Vec3 p)
    {
        ArgumentNullException.ThrowIfNull(layout);
        foreach (var box in layout.AllBoxes)
        {
            if (box.IsRoom && Movement.Contains(box.Box, p))
            {
                return box;
            }
        }
        return null;
    }

    /// <summary><paramref name="b"/> shrunk by <paramref name="mm"/> on every side; an axis too short for it collapses to its centre.</summary>
    /// <param name="b">The box.</param>
    /// <param name="mm">The inset, mm.</param>
    /// <returns>The inner box.</returns>
    public static Bounds Inset(Bounds b, long mm)
    {
        var (minX, maxX) = b.MaxX - b.MinX >= 2 * mm ? (b.MinX + mm, b.MaxX - mm) : (Mid(b.MinX, b.MaxX), Mid(b.MinX, b.MaxX));
        var (minZ, maxZ) = b.MaxZ - b.MinZ >= 2 * mm ? (b.MinZ + mm, b.MaxZ - mm) : (Mid(b.MinZ, b.MaxZ), Mid(b.MinZ, b.MaxZ));
        return new Bounds(minX, maxX, minZ, maxZ);
    }

    private static long Mid(long a, long b) => a + ((b - a) / 2);

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
        // D45: two points in one box of the other branch (layout.Side) share it too.
        return (aLow >= 0 && bLow >= 0 && aLow <= bHigh && bLow <= aHigh) ||
            layout.Side.Any(s => Movement.Contains(s.Box, a) && Movement.Contains(s.Box, b));
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
