using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// ROOM-LOCK-IN (keyrun STATE.md D27; design/BIBLE.md sections 4 and 9.1), re-derived for PRESPAWN-AGGRO (BIBLE section
/// 9.1, "everything is prespawned with an aggro distance"; "room lock-in moves to engage"): on a layout run every pull's
/// enemies stand idle in their room from tick 0. A pull engages (<c>pull_start</c>) on the first tick a LIVING party
/// member stands inside its aggro ring (<see cref="Pulls.DefaultAggroMm"/> around <see cref="Pulls.RingCenter"/>), or
/// when one of its enemies takes damage; walking into its room is not enough. On that tick the room's entry gate closes
/// (<c>gate_closed {room}</c>): an actor inside the room box cannot leave it and an actor outside cannot enter it. It
/// reopens on the pull's <c>pull_clear</c> (<c>gate_open {room}</c>). An idle enemy deals no damage and is never
/// auto-targeted. A dead member never engages anything.
/// </summary>
public sealed class RoomLockInTests
{
    private static readonly Bounds Entrance = new(-5000, 5000, -5000, 5000);
    private static readonly Bounds Corridor1 = new(4000, 12000, -1000, 1000);
    private static readonly Bounds Room1 = new(11000, 25000, -5000, 5000);
    private static readonly Bounds Trigger1 = new(13000, 25000, -5000, 5000);
    private static readonly Bounds Corridor2 = new(24000, 30000, -1000, 1000);
    private static readonly Bounds Room2 = new(29000, 40000, -5000, 5000);
    private static readonly Bounds Trigger2 = new(31000, 40000, -5000, 5000);

    /// <summary>
    /// Where each seat stops: inside room-1 (min x 11000) but well outside p1's aggro ring (centre x 22000, radius 4000),
    /// 2000 mm apart so neither presses on the other.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, long> ShortOfRingX = new Dictionary<int, long> { [1] = 14000, [2] = 12000 };

    private static NavLayout Layout() => new(
        new[]
        {
            new NavBox("entrance", Entrance, true, null),
            new NavBox("corridor-1", Corridor1, false, null),
            new NavBox("room-1", Room1, true, null),
            new NavBox("corridor-2", Corridor2, false, "p1"),
            new NavBox("room-2", Room2, true, null),
        },
        new[] { new NavTrigger("p1", Trigger1), new NavTrigger("p2", Trigger2) },
        new[] { new Vec3(4500, 0, 0), new Vec3(11500, 0, 0), new Vec3(24500, 0, 0), new Vec3(29500, 0, 0), new Vec3(35000, 0, 0) });

    // The enemies stand 2000 mm off the walking line (z 0), so a walker crosses each ring without touching a body.
    private static DungeonDef Dungeon(long health = 10, IReadOnlyList<string>? p2Links = null) => new("nav", new[]
    {
        new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 22000, 2000, health) }),
        new PullDef("p2", new[] { RunTestKit.Spawn(201, "dummy", 38000, 2000, health) }) { Links = p2Links ?? Array.Empty<string>() },
    });

    private static RunContent Content()
    {
        var nuke = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1000, "physical") });
        var layout = Layout();
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["nuke"] = nuke }, bounds: layout.Extent);
        return content with { Layout = layout };
    }

    // Warden (1) and Chanter (2), both in the Entrance.
    private static Run CreateTwo(DungeonDef? dungeon = null) => RunRoot.CreateRun(
        1,
        new[]
        {
            RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 400, 100),
            RunTestKit.Member(2, "chanter", ActorRole.Healer, -2000, 0, 250, 100),
        },
        dungeon ?? Dungeon(),
        Content());

    private static MoveCommand East(int tick, int actor) => new(tick, actor, new Dir2(1000, 0), 0);

    private static MoveCommand West(int tick, int actor) => new(tick, actor, new Dir2(-1000, 0), 0);

    private static (Run Run, IReadOnlyList<SimEvent> Events) Step(Run run, params Command[] commands)
    {
        var r = RunRoot.StepRun(run, commands);
        return (r.Run, r.Events);
    }

    private static string Room(SimEvent e) => ((JsString)e.Get("room")!).Value;

    private static bool InP1Ring(Run run, Vec3 p) => Aggro.InRing(p, Pulls.RingCenter(run.Dungeon.Pulls[0]), run.Dungeon.Pulls[0].AggroMm);

    // Walks both seats east until each stands at its ShortOfRingX: inside room-1, outside p1's aggro ring.
    private static (Run Run, List<SimEvent> Events) BothInsideShortOfTheRing(Run run)
    {
        var events = new List<SimEvent>();
        for (var i = 0; i < 400; i++)
        {
            var tick = run.State.Tick;
            var moves = new[] { 1, 2 }.Where(id => run.State.Actors[id].Pos.X < ShortOfRingX[id]).Select(id => (Command)East(tick, id)).ToArray();
            if (moves.Length == 0)
            {
                break;
            }
            var s = Step(run, moves);
            events.AddRange(s.Events);
            run = s.Run;
        }
        return (run, events);
    }

    // From BothInsideShortOfTheRing, walks the Warden east until p1 engages; returns the engage step's events.
    private static (Run Run, IReadOnlyList<SimEvent> Events) WardenEngages(Run run)
    {
        for (var i = 0; i < 400; i++)
        {
            var s = Step(run, East(run.State.Tick, 1));
            if (s.Events.Any(e => e.Type == "pull_start"))
            {
                return s;
            }
            run = s.Run;
        }
        throw new InvalidOperationException("p1 never engaged");
    }

    [Fact(DisplayName = "Room lock-in (a): a party standing in the room but outside the aggro ring engages nothing (no spawn on room entry)")]
    public void Entering_the_room_engages_nothing()
    {
        var (run, events) = BothInsideShortOfTheRing(CreateTwo());
        for (var i = 0; i < 100; i++)
        {
            var s = Step(run);
            events.AddRange(s.Events);
            run = s.Run;
        }
        Assert.True(Movement.Contains(Room1, run.State.Actors[1].Pos), $"the Warden should stand inside room-1, at {run.State.Actors[1].Pos}");
        Assert.True(Movement.Contains(Room1, run.State.Actors[2].Pos), $"the Chanter should stand inside room-1, at {run.State.Actors[2].Pos}");
        Assert.Contains(events, e => e.Type == "room_enter" && Room(e) == "room-1");
        Assert.DoesNotContain(events, e => e.Type is "pull_start" or "gate_closed" or "pull_clear");
        var enemy = run.State.Actors[101];
        Assert.True(enemy.Idle, "p1 engaged with nobody in its aggro ring");
        Assert.Equal(new Vec3(22000, 0, 2000), enemy.Pos);
        Assert.Empty(run.Encounter.Engaged);
        Assert.Equal(0, run.Encounter.PullIndex);
    }

    [Fact(DisplayName = "Room lock-in (b): the pull engages and the gate closes on the tick the first living member reaches the aggro ring")]
    public void The_pull_engages_on_the_tick_a_member_reaches_the_ring()
    {
        var run = BothInsideShortOfTheRing(CreateTwo()).Run;
        int? startTick = null;
        for (var i = 0; i < 400 && startTick is null; i++)
        {
            var before = run.State.Actors[1].Pos;
            var s = Step(run, East(run.State.Tick, 1));
            var after = s.Run.State.Actors[1].Pos;
            if (s.Events.Any(e => e.Type == "pull_start"))
            {
                startTick = s.Run.State.Tick;
                Assert.False(InP1Ring(run, before), $"p1 engaged late: the Warden was already in its ring at {before}");
                Assert.True(InP1Ring(run, after), $"p1 engaged with the Warden at {after}, outside its ring");
                var start = Assert.Single(s.Events, e => e.Type == "pull_start");
                Assert.Equal("p1", ((JsString)start.Get("pullId")!).Value);
                Assert.Equal(startTick, start.Tick);
                var gate = Assert.Single(s.Events, e => e.Type == "gate_closed");
                Assert.Equal("room-1", Room(gate));
                Assert.Equal(startTick, gate.Tick);
                Assert.False(s.Run.State.Actors[101].Idle);
                Assert.True(s.Run.State.Actors[201].Idle, "p2 engaged with p1");
                Assert.Equal(new[] { "p1" }, s.Run.Encounter.Engaged);
            }
            else
            {
                Assert.False(InP1Ring(run, after), $"the Warden stood in p1's ring at {after} with no pull_start");
            }
            run = s.Run;
        }
        Assert.NotNull(startTick);
    }

    [Fact(DisplayName = "Room lock-in (c): an idle pull's enemies deal no damage and are never auto-targeted; damage engages the whole group")]
    public void Idle_enemies_are_inert_until_damage_engages_the_group()
    {
        var dungeon = new DungeonDef("nav", new[]
        {
            new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 22000, 2000, 5000), RunTestKit.Spawn(102, "dummy", 22000, -2000, 5000) }),
            new PullDef("p2", new[] { RunTestKit.Spawn(201, "dummy", 38000, 2000, 5000) }),
        });
        var run = CreateTwo(dungeon);
        var events = new List<SimEvent>();
        for (var i = 0; i < 60; i++)
        {
            var s = Step(run);
            events.AddRange(s.Events);
            run = s.Run;
        }
        Assert.Empty(RunTestKit.Damages(events));
        Assert.DoesNotContain(events, e => e.Type is "death" or "pull_start");
        Assert.Equal(400, run.State.Actors[1].Health);
        Assert.Equal(250, run.State.Actors[2].Health);
        Assert.All(new[] { 101, 102, 201 }, id =>
        {
            Assert.True(run.State.Actors[id].Idle);
            Assert.Null(run.State.Actors[id].TargetId);
        });

        // The Warden nukes 101 from the Entrance: 101 and 102 engage together on that tick; p2 stays idle.
        var hit = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 101));
        Assert.Contains(RunTestKit.Damages(hit.Events), d => d == 1000);
        var start = Assert.Single(hit.Events, e => e.Type == "pull_start");
        Assert.Equal("p1", ((JsString)start.Get("pullId")!).Value);
        Assert.Equal(new long[] { 101, 102 }, ((JsArray)start.Get("actorIds")!).Items.Cast<JsInt>().Select(a => a.Value));
        Assert.False(hit.Run.State.Actors[101].Idle);
        Assert.False(hit.Run.State.Actors[102].Idle);
        Assert.True(hit.Run.State.Actors[201].Idle);
        Assert.Equal(1, hit.Run.State.Actors[101].TargetId);
        Assert.Equal(1, hit.Run.State.Actors[102].TargetId);
        Assert.Null(hit.Run.State.Actors[201].TargetId);
    }

    [Fact(DisplayName = "Room lock-in (d): the gate holds the party in the room from the engage to pull_clear, then opens")]
    public void The_gate_holds_the_party_in_until_pull_clear()
    {
        var run = WardenEngages(BothInsideShortOfTheRing(CreateTwo()).Run).Run;
        Assert.False(run.State.Actors[101].Idle);

        // Both walk west for longer than it takes to reach the Entrance: the gate keeps them in room-1.
        for (var i = 0; i < 400; i++)
        {
            run = Step(run, West(run.State.Tick, 1), West(run.State.Tick, 2)).Run;
            Assert.True(Movement.Contains(Room1, run.State.Actors[1].Pos), $"the Warden left the locked room: {run.State.Actors[1].Pos}");
            Assert.True(Movement.Contains(Room1, run.State.Actors[2].Pos), $"the Chanter left the locked room: {run.State.Actors[2].Pos}");
        }
        // The front walker stands on the room edge (the other is held behind it by actor spacing).
        Assert.Equal(Room1.MinX, Math.Min(run.State.Actors[1].Pos.X, run.State.Actors[2].Pos.X));

        var killed = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 101));
        var cleared = Step(killed.Run);
        var clearEvents = killed.Events.Concat(cleared.Events).ToList();
        Assert.Contains(clearEvents, e => e.Type == "pull_clear");
        var open = Assert.Single(clearEvents, e => e.Type == "gate_open");
        Assert.Equal("room-1", Room(open));
        Assert.Equal(clearEvents.First(e => e.Type == "pull_clear").Tick, open.Tick);

        run = cleared.Run;
        for (var i = 0; i < 100; i++)
        {
            run = Step(run, West(run.State.Tick, 1)).Run;
        }
        Assert.True(run.State.Actors[1].Pos.X < Room1.MinX, $"the gate should be open after pull_clear: {run.State.Actors[1].Pos}");
    }

    [Fact(DisplayName = "Room lock-in (d): a closed gate keeps an outside actor out and an inside actor in")]
    public void The_gate_rule_blocks_both_directions()
    {
        // Outside to inside: the move is refused (the actor stays where it was).
        var outside = new Vec3(10500, 0, 0);
        Assert.Equal(outside, NavLayout.GateClamp(Room1, outside, new Vec3(11500, 0, 0)));

        // Inside to outside: the actor stops on the room edge.
        Assert.Equal(new Vec3(11000, 0, 200), NavLayout.GateClamp(Room1, new Vec3(11400, 0, 0), new Vec3(10600, 0, 200)));

        // Moves that keep their side are untouched.
        Assert.Equal(new Vec3(12000, 0, 0), NavLayout.GateClamp(Room1, new Vec3(11400, 0, 0), new Vec3(12000, 0, 0)));
        Assert.Equal(new Vec3(9000, 0, 0), NavLayout.GateClamp(Room1, new Vec3(10000, 0, 0), new Vec3(9000, 0, 0)));
    }

    [Fact(DisplayName = "Room lock-in (e): a dead member standing inside the aggro ring engages nothing")]
    public void A_dead_member_in_the_ring_engages_nothing()
    {
        var run = RunRoot.CreateRun(
            1,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 400, 100),
                RunTestKit.Member(2, "chanter", ActorRole.Healer, 22000, 0, 250, 100),
            },
            Dungeon(),
            Content());
        Assert.True(InP1Ring(run, run.State.Actors[2].Pos));
        run.State.Actors[2].Health = 0;
        run.State.Actors[2].Alive = false;
        for (var i = 0; i < 50; i++)
        {
            var s = Step(run);
            Assert.DoesNotContain(s.Events, e => e.Type is "pull_start" or "gate_closed");
            run = s.Run;
        }
        Assert.True(run.State.Actors[101].Idle);
    }

    [Fact(DisplayName = "Room lock-in (f): a linked group engages on the tick the group it is linked to engages")]
    public void A_linked_group_engages_with_its_link()
    {
        var run = CreateTwo(Dungeon(health: 5000, p2Links: new[] { "p1" }));
        var hit = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 101));
        var starts = hit.Events.Where(e => e.Type == "pull_start").Select(e => ((JsString)e.Get("pullId")!).Value).ToList();
        Assert.Equal(new[] { "p1", "p2" }, starts);
        Assert.False(hit.Run.State.Actors[101].Idle);
        Assert.False(hit.Run.State.Actors[201].Idle);
        Assert.Equal(new[] { "p1", "p2" }, hit.Run.Encounter.Engaged);
        // Only the active pull's room shuts.
        var gate = Assert.Single(hit.Events, e => e.Type == "gate_closed");
        Assert.Equal("room-1", Room(gate));
    }

    [Fact(DisplayName = "Room lock-in: a party with no living member engages no pull")]
    public void A_dead_party_starts_no_pull()
    {
        var run = CreateTwo();
        foreach (var id in new[] { 1, 2 })
        {
            run.State.Actors[id].Health = 0;
            run.State.Actors[id].Alive = false;
        }
        for (var i = 0; i < 50; i++)
        {
            var s = Step(run);
            Assert.DoesNotContain(s.Events, e => e.Type is "pull_start" or "gate_closed");
            run = s.Run;
        }
    }

    [Fact(DisplayName = "Room lock-in: every trigger lies in a room box, and RoomFor names it")]
    public void RoomFor_names_the_room_holding_the_trigger()
    {
        var layout = Layout();
        Assert.Equal("room-1", layout.RoomFor("p1")!.Id);
        Assert.Equal("room-2", layout.RoomFor("p2")!.Id);
        Assert.Null(layout.RoomFor("nope"));
    }
}
