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
/// ENGAGE-RING (PRESPAWN-AGGRO, design/BIBLE.md section 9.1, "everything is prespawned with an aggro distance"; was
/// ROOM-LOCK-IN, STATE.md D27, whose room gate PATH-TO-BOSS removed, STATE.md D44): on a layout run every pull's enemies
/// stand idle in their room from tick 0. A pull engages (<c>pull_start</c>) on the first tick a LIVING party member
/// stands inside its aggro ring (<see cref="Pulls.DefaultAggroMm"/> around <see cref="Pulls.RingCenter"/>), or when one
/// of its enemies takes damage; walking into its room is not enough. No gate shuts on the engage: the party can leave the
/// room mid-fight and nobody is held out of it. An idle enemy deals no damage and is never auto-targeted. A dead member
/// never engages anything.
/// </summary>
public sealed class EngageRingTests
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
            new NavBox("corridor-2", Corridor2, false, null),
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

    [Fact(DisplayName = "Engage ring (a): a party standing in the room but outside the aggro ring engages nothing (no spawn on room entry)")]
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
        Assert.DoesNotContain(events, e => e.Type is "pull_start" or "pull_clear");
        var enemy = run.State.Actors[101];
        Assert.True(enemy.Idle, "p1 engaged with nobody in its aggro ring");
        Assert.Equal(new Vec3(22000, 0, 2000), enemy.Pos);
        Assert.Empty(run.Encounter.Engaged);
        Assert.Equal(0, run.Encounter.PullIndex);
    }

    [Fact(DisplayName = "Engage ring (b): the pull engages on the tick the first living member reaches the aggro ring, and no gate shuts")]
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
                Assert.DoesNotContain(s.Events, e => e.Type is "gate_closed" or "gate_open");
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

    [Fact(DisplayName = "Engage ring (c): an idle pull's enemies deal no damage and are never auto-targeted; damage engages the whole group")]
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

    [Fact(DisplayName = "Engage ring (d): no gate holds the party in an engaged room, and no gate event fires through the clear")]
    public void No_gate_holds_the_party_in_an_engaged_room()
    {
        var engaged = WardenEngages(BothInsideShortOfTheRing(CreateTwo()).Run);
        var run = engaged.Run;
        var events = engaged.Events.ToList();
        Assert.False(run.State.Actors[101].Idle);

        // Both walk west mid-fight: nothing holds them in room-1 (PATH-TO-BOSS, STATE.md D44).
        for (var i = 0; i < 200 && (Movement.Contains(Room1, run.State.Actors[1].Pos) || Movement.Contains(Room1, run.State.Actors[2].Pos)); i++)
        {
            var s = Step(run, West(run.State.Tick, 1), West(run.State.Tick, 2));
            events.AddRange(s.Events);
            run = s.Run;
        }
        Assert.False(Movement.Contains(Room1, run.State.Actors[1].Pos), $"the Warden is held in the engaged room: {run.State.Actors[1].Pos}");
        Assert.False(Movement.Contains(Room1, run.State.Actors[2].Pos), $"the Chanter is held in the engaged room: {run.State.Actors[2].Pos}");
        Assert.False(run.State.Actors[101].Idle);

        var killed = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 101));
        var cleared = Step(killed.Run);
        events.AddRange(killed.Events);
        events.AddRange(cleared.Events);
        Assert.Contains(events, e => e.Type == "pull_clear");
        Assert.DoesNotContain(events, e => e.Type is "gate_closed" or "gate_open");
    }

    [Fact(DisplayName = "Engage ring (e): a dead member standing inside the aggro ring engages nothing")]
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
            Assert.DoesNotContain(s.Events, e => e.Type == "pull_start");
            run = s.Run;
        }
        Assert.True(run.State.Actors[101].Idle);
    }

    [Fact(DisplayName = "Engage ring (f): a linked group engages on the tick the group it is linked to engages")]
    public void A_linked_group_engages_with_its_link()
    {
        var run = CreateTwo(Dungeon(health: 5000, p2Links: new[] { "p1" }));
        var hit = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 101));
        var starts = hit.Events.Where(e => e.Type == "pull_start").Select(e => ((JsString)e.Get("pullId")!).Value).ToList();
        Assert.Equal(new[] { "p1", "p2" }, starts);
        Assert.False(hit.Run.State.Actors[101].Idle);
        Assert.False(hit.Run.State.Actors[201].Idle);
        Assert.Equal(new[] { "p1", "p2" }, hit.Run.Encounter.Engaged);
        Assert.DoesNotContain(hit.Events, e => e.Type is "gate_closed" or "gate_open");
    }

    [Fact(DisplayName = "Engage ring: a party with no living member engages no pull")]
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
            Assert.DoesNotContain(s.Events, e => e.Type == "pull_start");
            run = s.Run;
        }
    }

    [Fact(DisplayName = "Engage ring: every trigger lies in a room box, and RoomFor names it")]
    public void RoomFor_names_the_room_holding_the_trigger()
    {
        var layout = Layout();
        Assert.Equal("room-1", layout.RoomFor("p1")!.Id);
        Assert.Equal("room-2", layout.RoomFor("p2")!.Id);
        Assert.Null(layout.RoomFor("nope"));
    }
}
