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
/// The layout run rules on the public run root (STATE.md D18, design/ART-PLAN.md section 3; new behaviour, no TS oracle):
/// with <see cref="RunContent.Layout"/> set, a pull spawns the first tick a party member stands in its room's trigger box
/// (never at run start, never on the previous clear), the corridor out of a room is a wall until the room's pull clears,
/// and entering a room emits <c>room_enter</c>. A run without a layout is every oracle run and is unchanged.
/// </summary>
public sealed class NavLayoutRunTests
{
    private static readonly Bounds Entrance = new(-5000, 5000, -5000, 5000);
    private static readonly Bounds Corridor1 = new(4000, 12000, -1000, 1000);
    private static readonly Bounds Room1 = new(11000, 25000, -5000, 5000);
    private static readonly Bounds Trigger1 = new(13000, 25000, -5000, 5000);
    private static readonly Bounds Corridor2 = new(24000, 30000, -1000, 1000);
    private static readonly Bounds Room2 = new(29000, 40000, -5000, 5000);
    private static readonly Bounds Trigger2 = new(31000, 40000, -5000, 5000);

    private static NavLayout Layout(bool withSecondTrigger = true) => new(
        new[]
        {
            new NavBox("entrance", Entrance, true, null),
            new NavBox("corridor-1", Corridor1, false, null),
            new NavBox("room-1", Room1, true, null),
            new NavBox("corridor-2", Corridor2, false, "p1"),
            new NavBox("room-2", Room2, true, null),
        },
        withSecondTrigger
            ? new[] { new NavTrigger("p1", Trigger1), new NavTrigger("p2", Trigger2) }
            : new[] { new NavTrigger("p1", Trigger1) },
        new[] { new Vec3(4500, 0, 0), new Vec3(11500, 0, 0), new Vec3(24500, 0, 0), new Vec3(29500, 0, 0), new Vec3(35000, 0, 0) });

    private static readonly DungeonDef Dungeon = new("nav", new[]
    {
        new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 22000, 4000, 10) }),
        new PullDef("p2", new[] { RunTestKit.Spawn(201, "dummy", 38000, 4000, 10) }),
    });

    private static RunContent Content(NavLayout? layout)
    {
        var nuke = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1000, "physical") });
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["nuke"] = nuke }, bounds: layout?.Extent ?? RunTestKit.Arena20k);
        return content with { Layout = layout };
    }

    private static Run Create(long x, NavLayout? layout = null) => RunRoot.CreateRun(
        1,
        new[] { RunTestKit.Member(1, "warden", ActorRole.Tank, x, 0, 400, 100) },
        Dungeon,
        Content(layout ?? Layout()));

    private static MoveCommand East(int tick) => new(tick, 1, new Dir2(1000, 0), 0);

    // Steps one tick with the given commands; returns the run and that tick's events.
    private static (Run Run, IReadOnlyList<SimEvent> Events) Step(Run run, params Command[] commands)
    {
        var r = RunRoot.StepRun(run, commands);
        return (r.Run, r.Events);
    }

    [Fact(DisplayName = "Nav run: no pull spawns before a party member enters its trigger box")]
    public void No_pull_spawns_before_a_party_member_enters_its_trigger_box()
    {
        var run = Create(0);
        Assert.False(run.State.Actors.ContainsKey(101));
        var idle = run;
        for (var i = 0; i < 300; i++)
        {
            var s = Step(idle);
            Assert.DoesNotContain(s.Events, e => e.Type is "pull_start" or "pull_clear" or "run_end");
            idle = s.Run;
        }
        Assert.False(idle.State.Actors.ContainsKey(101));
        Assert.Equal(0, idle.Encounter.PullIndex);

        // Walk east: the pull starts on exactly the tick the Warden first stands in the trigger box.
        var walking = run;
        int? startTick = null;
        for (var i = 0; i < 200 && startTick is null; i++)
        {
            var before = walking.State.Actors[1].Pos;
            var s = Step(walking, East(walking.State.Tick));
            var after = s.Run.State.Actors[1].Pos;
            if (s.Events.Any(e => e.Type == "pull_start"))
            {
                startTick = s.Run.State.Tick;
                Assert.False(Movement.Contains(Trigger1, before), $"pull started at {after} but the Warden was already in the trigger at {before}");
                Assert.True(Movement.Contains(Trigger1, after), $"pull started with the Warden at {after}, outside the trigger");
                Assert.True(s.Run.State.Actors.ContainsKey(101));
            }
            else
            {
                Assert.False(Movement.Contains(Trigger1, after), $"the Warden stood in the trigger at {after} with no pull_start");
            }
            walking = s.Run;
        }
        Assert.NotNull(startTick);
    }

    [Fact(DisplayName = "Nav run: a run that starts inside a trigger spawns that pull at creation")]
    public void A_run_starting_inside_a_trigger_spawns_that_pull_at_creation()
    {
        var run = Create(14000);
        Assert.True(run.State.Actors.ContainsKey(101));
        Assert.False(run.State.Actors.ContainsKey(201));
    }

    [Fact(DisplayName = "Nav run: the corridor out of a room is a wall until the room's pull clears")]
    public void The_corridor_out_of_a_room_opens_on_pull_clear()
    {
        var run = Create(14000);
        for (var i = 0; i < 120; i++)
        {
            run = Step(run, East(run.State.Tick)).Run;
        }
        Assert.Equal(Room1.MaxX, run.State.Actors[1].Pos.X);
        Assert.True(run.State.Actors[101].Alive);

        // The kill lands in combat; the next tick's encounter step sees the pull dead and clears it.
        var killed = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 101));
        var cleared = Step(killed.Run);
        Assert.Contains(killed.Events.Concat(cleared.Events), e => e.Type == "pull_clear");
        Assert.DoesNotContain(killed.Events.Concat(cleared.Events), e => e.Type == "pull_start");
        run = cleared.Run;
        Assert.False(run.State.Actors.ContainsKey(201));
        int? p2Start = null;
        for (var i = 0; i < 120 && p2Start is null; i++)
        {
            var s = Step(run, East(run.State.Tick));
            if (s.Events.Any(e => e.Type == "pull_start"))
            {
                p2Start = s.Run.State.Tick;
                Assert.True(Movement.Contains(Trigger2, s.Run.State.Actors[1].Pos));
            }
            run = s.Run;
        }
        Assert.NotNull(p2Start);
        Assert.True(run.State.Actors[1].Pos.X > Room1.MaxX);
    }

    [Fact(DisplayName = "Nav run: entering a room emits room_enter naming the room and the actor")]
    public void Entering_a_room_emits_room_enter()
    {
        var run = Create(0);
        var rooms = new List<(int Tick, string Room)>();
        for (var i = 0; i < 200; i++)
        {
            var s = Step(run, East(run.State.Tick));
            foreach (var e in s.Events.Where(e => e.Type == "room_enter"))
            {
                Assert.Equal(1, ((JsInt)e.Get("actorId")!).Value);
                rooms.Add((e.Tick, ((JsString)e.Get("room")!).Value));
            }
            run = s.Run;
        }
        Assert.Equal(new[] { "room-1" }, rooms.Select(r => r.Room));
        Assert.Equal(new Vec3(25000, 0, 0), run.State.Actors[1].Pos);
    }

    [Fact(DisplayName = "Nav run: backfill fills an open seat at the next pull's room-entry start, not at the clear")]
    public void Backfill_fills_at_the_room_entry_pull_start()
    {
        var run = RunRoot.CreateRun(
            1,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 14000, 0, 400, 100),
                RunTestKit.Member(2, "chanter", ActorRole.Healer, 13000, -2000, 250, 100),
            },
            Dungeon,
            Content(Layout()) with { KeyLevel = 2 });
        var book = Backfill.Abandon(BackfillState.Empty, run, 2);
        var queue = new[] { new QueueRequest("nav", 2, "none") };
        var kill = Backfill.Step(book, run, new Command[] { new CastCommand(run.State.Tick, 1, "nuke", 101) }, queue);
        var clear = Backfill.Step(kill.Backfill, kill.Step.Run, Array.Empty<Command>(), queue);
        Assert.Contains(clear.Step.Events, e => e.Type == "pull_clear");
        Assert.Empty(kill.Fills.Concat(clear.Fills));
        run = clear.Step.Run;
        book = clear.Backfill;
        int? fillTick = null;
        for (var i = 0; i < 200 && fillTick is null; i++)
        {
            var s = Backfill.Step(book, run, new Command[] { East(run.State.Tick) }, queue);
            if (s.Fills.Count > 0)
            {
                Assert.Contains(s.Step.Events, e => e.Type == "pull_start");
                Assert.Equal(2, s.Fills[0].ActorId);
                Assert.Equal(1, s.Fills[0].PullIndex);
                fillTick = s.Step.Run.State.Tick;
            }
            run = s.Step.Run;
            book = s.Backfill;
        }
        Assert.NotNull(fillTick);
    }

    [Fact(DisplayName = "Nav run: a layout with no trigger for a pull fails closed at creation")]
    public void A_pull_without_a_trigger_fails_closed()
    {
        var ex = Assert.Throws<ArgumentException>(() => Create(0, Layout(withSecondTrigger: false)));
        Assert.Contains("\"p2\"", ex.Message);
    }

    [Fact(DisplayName = "Nav run: without a layout pull 0 spawns at creation and the next pull on the clear (unchanged)")]
    public void Without_a_layout_the_run_root_is_unchanged()
    {
        var run = RunRoot.CreateRun(1, new[] { RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 400, 100) }, Dungeon, Content(null));
        Assert.True(run.State.Actors.ContainsKey(101));
        var killed = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 101));
        var next = Step(killed.Run);
        Assert.Contains(next.Events, e => e.Type == "pull_start");
        Assert.True(next.Run.State.Actors.ContainsKey(201));
        Assert.DoesNotContain(killed.Events.Concat(next.Events), e => e.Type == "room_enter");
    }
}
