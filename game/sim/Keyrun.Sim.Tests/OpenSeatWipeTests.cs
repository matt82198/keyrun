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
/// OPEN-SEAT WIPE (PATH-TO-BOSS, STATE.md D44; adapted from PR #359's GATED-WIPE, finding f-3cc5aeee, with the room gate
/// gone; design/BIBLE.md sections 0 and 8; new behaviour, no TS oracle). A run wipes when no living player who is actually
/// in the run remains: an abandoned or unfilled seat (<see cref="StepRunOpts.OpenSeats"/>) is not in the run, so its idle
/// body never holds a dead party's run open, in or out of combat. The wipe resolves like any full wipe: the wipe Slack
/// cost on the last death, <see cref="RunWiring.WipeGraceTicks"/>, then <c>run_end</c> depleted. A living seated player
/// anywhere keeps the run going, and with no gate it can walk back into the fight.
/// </summary>
public sealed class OpenSeatWipeTests
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
            new NavBox("corridor-2", Corridor2, false, null),
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

    // Walks the given seats east until pull p1 engages (pull_start on its aggro ring); returns the run on that tick.
    private static Run WalkInUntilPullStart(Run run, StepRunOpts? opts, params int[] walkers)
    {
        for (var i = 0; i < 400; i++)
        {
            var tick = run.State.Tick;
            var s = Step(run, opts, walkers.Select(id => (Command)East(tick, id)).ToArray());
            run = s.Run;
            if (s.Events.Any(e => e.Type == "pull_start"))
            {
                return run;
            }
        }
        throw new InvalidOperationException("pull p1 never engaged");
    }

    // The pull's enemy kills the given seats, one per tick.
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

    // Steps until run_end with no commands, at most maxTicks.
    private static (Run Run, SimEvent? RunEnd) StepUntilRunEnd(Run run, StepRunOpts? opts, int maxTicks, Func<Run, Command[]>? commands = null)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            var s = Step(run, opts, commands?.Invoke(run) ?? Array.Empty<Command>());
            run = s.Run;
            var end = s.Events.FirstOrDefault(e => e.Type == "run_end");
            if (end is not null)
            {
                return (run, end);
            }
        }
        return (run, null);
    }

    [Fact(DisplayName = "Open-seat wipe: the seated party dies in the fight and an abandoned seat in the Entrance does not hold the run open")]
    public void An_abandoned_seat_outside_the_fight_does_not_hold_the_run_open()
    {
        var opts = new StepRunOpts(OpenSeats: new[] { Chanter });
        var run = WalkInUntilPullStart(CreateThree(), opts, Warden, Cinder);
        Assert.True(Movement.Contains(Entrance, run.State.Actors[Chanter].Pos));

        var (killed, lastDeath) = Kill(run, opts, Warden, Cinder);
        var (ended, runEnd) = StepUntilRunEnd(killed, opts, RunWiring.WipeGraceTicks + 50);

        Assert.True(runEnd is not null, $"no run_end within {RunWiring.WipeGraceTicks + 50} ticks of the last death at {lastDeath}");
        Assert.Equal("depleted", ((JsString)runEnd!.Get("result")!).Value);
        Assert.Equal(lastDeath + RunWiring.WipeGraceTicks, runEnd.Tick);
        Assert.True(ended.State.Actors[Chanter].Alive);
        Assert.Equal(0, ended.Encounter.PullIndex);
    }

    [Fact(DisplayName = "Open-seat wipe: the seated party dies out of combat and an abandoned seat does not hold the run open")]
    public void An_abandoned_seat_does_not_hold_an_out_of_combat_wipe_open()
    {
        var opts = new StepRunOpts(OpenSeats: new[] { Chanter });
        var run = CreateThree();
        foreach (var id in new[] { Warden, Cinder })
        {
            run.State.Actors[id].Health = 0;
            run.State.Actors[id].Alive = false;
        }
        var wipeStart = run.State.Tick + 1;
        var (ended, runEnd) = StepUntilRunEnd(run, opts, RunWiring.WipeGraceTicks + 50);

        Assert.True(runEnd is not null, "an abandoned seat held a dead party's run open out of combat");
        Assert.Equal("depleted", ((JsString)runEnd!.Get("result")!).Value);
        Assert.Equal(wipeStart + RunWiring.WipeGraceTicks, runEnd.Tick);
        Assert.Empty(ended.Encounter.Engaged);
    }

    [Fact(DisplayName = "Open-seat wipe control: a living seated player outside the fought room keeps the run going and can walk back in")]
    public void A_living_seated_player_outside_the_room_is_not_a_wipe()
    {
        var run = WalkInUntilPullStart(CreateThree(), null, Warden, Chanter, Cinder);
        run.State.Actors[Chanter].Pos = new Vec3(0, 0, 0);

        var (killed, _) = Kill(run, null, Warden, Cinder);
        var (ended, runEnd) = StepUntilRunEnd(killed, null, RunWiring.WipeGraceTicks * 3, r => r.State.Actors[Chanter].Pos.X < 15000 ? new Command[] { East(r.State.Tick, Chanter) } : Array.Empty<Command>());

        Assert.Null(runEnd);
        Assert.Null(ended.RunEnd.Result);
        Assert.Null(ended.RunEnd.WipeStartTick);
        Assert.True(ended.State.Actors[Chanter].Alive);
        Assert.True(Movement.Contains(Room1, ended.State.Actors[Chanter].Pos), $"no gate: the Chanter walks back into the fought room, at {ended.State.Actors[Chanter].Pos}");
    }

    [Fact(DisplayName = "Open-seat wipe: the wipe pays the full-wipe Slack cost on the last seated death")]
    public void An_open_seat_wipe_spends_the_wipe_slack()
    {
        var opts = new StepRunOpts(OpenSeats: new[] { Chanter });
        var run = WalkInUntilPullStart(CreateThree(), opts, Warden, Cinder);
        // A bank deep enough that neither spend clamps at 0 (key 1 starts at 600 ticks).
        const int slackBefore = 5000;
        run = run with { Encounter = run.Encounter with { SlackTicks = slackBefore } };

        var (killed, _) = Kill(run, opts, Warden, Cinder);

        Assert.Equal(slackBefore - (2 * Timer.DeathSlackCostTicks) - Timer.WipeSlackCostTicks, killed.Encounter.SlackTicks);
    }

    [Fact(DisplayName = "Open-seat wipe: IsWipe counts only living players who are in the run")]
    public void IsWipe_ignores_open_seats_only()
    {
        var run = CreateThree();
        Assert.False(RunWiring.IsWipe(run.State));
        Assert.False(RunWiring.IsWipe(run.State, new[] { Chanter }));
        run.State.Actors[Warden].Alive = false;
        run.State.Actors[Cinder].Alive = false;
        Assert.False(RunWiring.IsWipe(run.State));
        Assert.True(RunWiring.IsWipe(run.State, new[] { Chanter }));
        Assert.False(RunWiring.IsWipe(run.State, new[] { Cinder }));
        Assert.False(RunWiring.IsWipe(SimState.Create(1)));
    }
}
