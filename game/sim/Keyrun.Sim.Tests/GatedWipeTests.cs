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
/// GATED-WIPE (finding f-3cc5aeee; design/BIBLE.md sections 0 and 8, STATE.md D27; new behaviour, no TS oracle). On a
/// layout run the active pull's room gate is shut from its pull_start to its pull_clear, and an actor outside cannot
/// enter. When every player locked in that room is dead, no living player can ever fight the pull: the pull cannot clear,
/// the gate cannot open, and before this rule the run never ended (PR #308 run C: the Warden and the Cinder died in
/// pair-hall, the Chanter self-healed outside the gate, no run_end by tick 7000). That is a full wipe of the fight, and it
/// resolves exactly like one (<see cref="RunWiring.WipeGraceTicks"/>, then <c>run_end</c> depleted), whoever is still
/// standing outside the shut gate.
/// </summary>
public sealed class GatedWipeTests
{
    private static readonly Bounds Entrance = new(-5000, 5000, -5000, 5000);
    private static readonly Bounds Corridor1 = new(4000, 12000, -1000, 1000);
    private static readonly Bounds Room1 = new(11000, 25000, -5000, 5000);
    private static readonly Bounds Trigger1 = new(13000, 25000, -5000, 5000);
    private static readonly Bounds Corridor2 = new(24000, 30000, -1000, 1000);
    private static readonly Bounds Room2 = new(29000, 40000, -5000, 5000);
    private static readonly Bounds Trigger2 = new(31000, 40000, -5000, 5000);

    private const int Warden = 1;
    private const int Chanter = 2;
    private const int Cinder = 3;
    private const int Enemy = 101;

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
        new PullDef("p1", new[] { RunTestKit.Spawn(Enemy, "dummy", 22000, 4000, 10) }),
        new PullDef("p2", new[] { RunTestKit.Spawn(201, "dummy", 38000, 4000, 10) }),
    });

    private static Run CreateThree()
    {
        var nuke = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1000, "physical") });
        var layout = Layout();
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["nuke"] = nuke }, bounds: layout.Extent) with { Layout = layout };
        return RunRoot.CreateRun(
            1,
            new[]
            {
                RunTestKit.Member(Warden, "warden", ActorRole.Tank, 0, 0, 400, 100),
                RunTestKit.Member(Chanter, "chanter", ActorRole.Healer, -2000, 0, 250, 100),
                RunTestKit.Member(Cinder, "cinder", ActorRole.Damage, -1000, 0, 250, 100),
            },
            Dungeon,
            content);
    }

    private static MoveCommand East(int tick, int actor) => new(tick, actor, new Dir2(1000, 0), 0);

    private static RunStepResult Step(Run run, StepRunOpts? opts, params Command[] commands) => RunRoot.StepRun(run, commands, opts);

    // Walks the given seats east until pull p1 has started (gate_closed on room-1); returns the run on that tick.
    private static Run WalkInUntilPullStart(Run run, StepRunOpts? opts, params int[] walkers)
    {
        for (var i = 0; i < 400; i++)
        {
            var tick = run.State.Tick;
            var s = Step(run, opts, walkers.Select(id => (Command)East(tick, id)).ToArray());
            run = s.Run;
            if (s.Events.Any(e => e.Type == "gate_closed"))
            {
                return run;
            }
        }
        throw new InvalidOperationException("pull p1 never started");
    }

    // The pull's enemy kills the given seats, one per tick (a careless pull: the tank and the dps die in the room).
    private static (Run Run, int LastDeathTick) Kill(Run run, StepRunOpts? opts, params int[] victims)
    {
        var last = -1;
        foreach (var victim in victims)
        {
            var s = Step(run, opts, new CastCommand(run.State.Tick, Enemy, "nuke", victim));
            Assert.Contains(s.Events, e => e.Type == "death" && e.Get("actorId") is JsInt id && id.Value == victim);
            last = s.Run.State.Tick;
            run = s.Run;
        }
        return (run, last);
    }

    // Steps until run_end (the gated-out seat keeps trying to walk into the shut room), at most maxTicks.
    private static (Run Run, SimEvent? RunEnd) StepUntilRunEnd(Run run, StepRunOpts? opts, int walker, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var s = Step(run, opts, East(run.State.Tick, walker));
            run = s.Run;
            var end = s.Events.FirstOrDefault(e => e.Type == "run_end");
            if (end is not null)
            {
                return (run, end);
            }
        }
        return (run, null);
    }

    [Fact(DisplayName = "Gated wipe: the tank and the dps die in the locked room, the healer is gated out, and the run ends as a wipe")]
    public void Locked_room_wipe_with_a_living_seat_outside_resolves_the_run()
    {
        // PR #308 run C's shape: all three walk in, the pull starts, and the Chanter ends up outside the shut gate (under
        // PRESPAWN-AGGRO the Pair engaged on the Cinder's ring with the Chanter still in the corridor).
        var run = WalkInUntilPullStart(CreateThree(), null, Warden, Chanter, Cinder);
        run.State.Actors[Chanter].Pos = new Vec3(0, 0, 0);
        Assert.False(Movement.Contains(Room1, run.State.Actors[Chanter].Pos));

        var (killed, lastDeath) = Kill(run, null, Warden, Cinder);
        var (ended, runEnd) = StepUntilRunEnd(killed, null, Chanter, RunWiring.WipeGraceTicks + 50);

        Assert.True(runEnd is not null, $"no run_end within {RunWiring.WipeGraceTicks + 50} ticks of the last death at {lastDeath}");
        Assert.Equal("depleted", ((JsString)runEnd!.Get("result")!).Value);
        Assert.Equal(lastDeath + RunWiring.WipeGraceTicks, runEnd.Tick);
        Assert.True(ended.State.Actors[Chanter].Alive, "the gated-out Chanter is alive: the run ended on the locked-room wipe, not on a full party death");
        Assert.False(Movement.Contains(Room1, ended.State.Actors[Chanter].Pos), "the shut gate kept the Chanter out");
        Assert.Equal(0, ended.Encounter.PullIndex);
    }

    [Fact(DisplayName = "Gated wipe: an abandoned seat left outside the locked room does not hold a dead party's run open")]
    public void Locked_room_wipe_with_an_open_seat_outside_resolves_the_run()
    {
        // The same stalemate on main without PRESPAWN-AGGRO: an abandoned, unfilled seat (alive, idle) never holds a pull
        // back (D27), so the pull starts with it in the Entrance. Its joiner would arrive at the NEXT pull start, which a
        // dead party never reaches.
        var opts = new StepRunOpts(OpenSeats: new[] { Chanter });
        var run = WalkInUntilPullStart(CreateThree(), opts, Warden, Cinder);
        Assert.True(Movement.Contains(Entrance, run.State.Actors[Chanter].Pos));

        var (killed, lastDeath) = Kill(run, opts, Warden, Cinder);
        var (ended, runEnd) = StepUntilRunEnd(killed, opts, Chanter, RunWiring.WipeGraceTicks + 50);

        Assert.True(runEnd is not null, $"no run_end within {RunWiring.WipeGraceTicks + 50} ticks of the last death at {lastDeath}");
        Assert.Equal("depleted", ((JsString)runEnd!.Get("result")!).Value);
        Assert.Equal(lastDeath + RunWiring.WipeGraceTicks, runEnd.Tick);
        Assert.True(ended.State.Actors[Chanter].Alive);
    }

    [Fact(DisplayName = "Gated wipe control: one living player still in the locked room keeps the fight going")]
    public void A_living_player_in_the_locked_room_is_not_a_wipe()
    {
        var run = WalkInUntilPullStart(CreateThree(), null, Warden, Chanter, Cinder);
        Assert.True(Movement.Contains(Room1, run.State.Actors[Chanter].Pos));

        var (killed, _) = Kill(run, null, Warden, Cinder);
        var (ended, runEnd) = StepUntilRunEnd(killed, null, Chanter, RunWiring.WipeGraceTicks * 3);

        Assert.Null(runEnd);
        Assert.Null(ended.RunEnd.Result);
        Assert.Null(ended.RunEnd.WipeStartTick);
        Assert.True(ended.State.Actors[Chanter].Alive);
    }

    [Fact(DisplayName = "Gated wipe: the locked-room wipe pays the full-wipe Slack cost on the last death")]
    public void Locked_room_wipe_spends_the_wipe_slack()
    {
        var run = WalkInUntilPullStart(CreateThree(), null, Warden, Chanter, Cinder);
        run.State.Actors[Chanter].Pos = new Vec3(0, 0, 0);
        // A bank deep enough that neither spend clamps at 0 (key 1 starts at 600 ticks).
        const int slackBefore = 5000;
        run = run with { Encounter = run.Encounter with { SlackTicks = slackBefore } };

        var (killed, _) = Kill(run, null, Warden, Cinder);

        Assert.Equal(slackBefore - (2 * Timer.DeathSlackCostTicks) - Timer.WipeSlackCostTicks, killed.Encounter.SlackTicks);
    }
}
