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
/// ROOM-LOCK-IN (keyrun STATE.md D27; design/BIBLE.md sections 4 and 9; new behaviour, no TS oracle): on a layout run a
/// pull's <c>pull_start</c> fires on the first tick every LIVING party member stands inside the pull's room box (the
/// room box holding its trigger); until then the pull's enemies are not in the state, so they cannot be targeted,
/// damaged or deal damage. At <c>pull_start</c> the room's entry gate closes (<c>gate_closed {room}</c>): an actor inside
/// the room box cannot leave it and an actor outside cannot enter it. It reopens on the pull's <c>pull_clear</c>
/// (<c>gate_open {room}</c>). A dead member never holds a pull back.
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

    private static readonly DungeonDef Dungeon = new("nav", new[]
    {
        new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 22000, 4000, 10) }),
        new PullDef("p2", new[] { RunTestKit.Spawn(201, "dummy", 38000, 4000, 10) }),
    });

    private static RunContent Content()
    {
        var nuke = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1000, "physical") });
        var layout = Layout();
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["nuke"] = nuke }, bounds: layout.Extent);
        return content with { Layout = layout };
    }

    // Warden (1) and Chanter (2), both in the Entrance.
    private static Run CreateTwo() => RunRoot.CreateRun(
        1,
        new[]
        {
            RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 400, 100),
            RunTestKit.Member(2, "chanter", ActorRole.Healer, -2000, 0, 250, 100),
        },
        Dungeon,
        Content());

    private static MoveCommand East(int tick, int actor) => new(tick, actor, new Dir2(1000, 0), 0);

    private static MoveCommand West(int tick, int actor) => new(tick, actor, new Dir2(-1000, 0), 0);

    private static (Run Run, IReadOnlyList<SimEvent> Events) Step(Run run, params Command[] commands)
    {
        var r = RunRoot.StepRun(run, commands);
        return (r.Run, r.Events);
    }

    private static string Room(SimEvent e) => ((JsString)e.Get("room")!).Value;

    // Walks the Warden east until it stands deep in room-1 (clamped at the gated corridor-2 doorway); the Chanter idles.
    private static (Run Run, List<SimEvent> Events) WardenInside(Run run)
    {
        var events = new List<SimEvent>();
        for (var i = 0; i < 400; i++)
        {
            var s = Step(run, East(run.State.Tick, 1));
            events.AddRange(s.Events);
            run = s.Run;
        }
        return (run, events);
    }

    [Fact(DisplayName = "Room lock-in (a): a pull does not start while a living party member is outside its room")]
    public void A_pull_does_not_start_while_a_living_member_is_outside()
    {
        var (run, events) = WardenInside(CreateTwo());
        Assert.True(Movement.Contains(Room1, run.State.Actors[1].Pos), $"the Warden should stand inside room-1, at {run.State.Actors[1].Pos}");
        Assert.True(Movement.Contains(Entrance, run.State.Actors[2].Pos));
        Assert.DoesNotContain(events, e => e.Type is "pull_start" or "gate_closed" or "pull_clear");
        Assert.False(run.State.Actors.ContainsKey(101));
        Assert.Equal(0, run.Encounter.PullIndex);
    }

    [Fact(DisplayName = "Room lock-in (b): the pull starts and the gate closes on the tick the last living member enters")]
    public void The_pull_starts_on_the_tick_the_last_living_member_enters()
    {
        var run = WardenInside(CreateTwo()).Run;
        int? startTick = null;
        for (var i = 0; i < 400 && startTick is null; i++)
        {
            var before = run.State.Actors[2].Pos;
            var s = Step(run, East(run.State.Tick, 2));
            var after = s.Run.State.Actors[2].Pos;
            if (s.Events.Any(e => e.Type == "pull_start"))
            {
                startTick = s.Run.State.Tick;
                Assert.False(Movement.Contains(Room1, before), $"pull started late: the Chanter was already inside at {before}");
                Assert.True(Movement.Contains(Room1, after), $"pull started with the Chanter at {after}, outside the room");
                Assert.True(s.Run.State.Actors.ContainsKey(101));
                var gate = Assert.Single(s.Events, e => e.Type == "gate_closed");
                Assert.Equal("room-1", Room(gate));
                Assert.Equal(startTick, gate.Tick);
            }
            else
            {
                Assert.False(Movement.Contains(Room1, after), $"the Chanter stood inside at {after} with no pull_start");
            }
            run = s.Run;
        }
        Assert.NotNull(startTick);
    }

    [Fact(DisplayName = "Room lock-in (c): an unstarted pull's enemies cannot be targeted, damaged or deal damage")]
    public void Unstarted_pull_enemies_cannot_be_targeted_or_damaged()
    {
        var run = WardenInside(CreateTwo()).Run;
        var events = new List<SimEvent>();
        for (var i = 0; i < 60; i++)
        {
            var s = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 101));
            events.AddRange(s.Events);
            run = s.Run;
        }
        Assert.False(run.State.Actors.ContainsKey(101));
        Assert.Empty(RunTestKit.Damages(events));
        Assert.DoesNotContain(events, e => e.Type is "death" or "pull_start");
        Assert.Equal(400, run.State.Actors[1].Health);
        Assert.Equal(250, run.State.Actors[2].Health);
        Assert.NotEqual(101, run.State.Actors[1].TargetId);
    }

    [Fact(DisplayName = "Room lock-in (d): the gate holds the party in the room from pull_start to pull_clear, then opens")]
    public void The_gate_holds_the_party_in_until_pull_clear()
    {
        var run = WardenInside(CreateTwo()).Run;
        for (var i = 0; i < 400 && !run.State.Actors.ContainsKey(101); i++)
        {
            run = Step(run, East(run.State.Tick, 2)).Run;
        }
        Assert.True(run.State.Actors.ContainsKey(101));

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

    [Fact(DisplayName = "Room lock-in (e): a dead member outside the room does not hold the pull back")]
    public void A_dead_member_outside_does_not_block_the_start()
    {
        var run = CreateTwo();
        run.State.Actors[2].Health = 0;
        run.State.Actors[2].Alive = false;
        int? startTick = null;
        for (var i = 0; i < 400 && startTick is null; i++)
        {
            var s = Step(run, East(run.State.Tick, 1));
            if (s.Events.Any(e => e.Type == "pull_start"))
            {
                startTick = s.Run.State.Tick;
                Assert.True(Movement.Contains(Room1, s.Run.State.Actors[1].Pos));
                Assert.True(Movement.Contains(Entrance, s.Run.State.Actors[2].Pos));
                Assert.Contains(s.Events, e => e.Type == "gate_closed" && Room(e) == "room-1");
            }
            run = s.Run;
        }
        Assert.NotNull(startTick);
    }

    [Fact(DisplayName = "Room lock-in: a party with no living member starts no pull")]
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
