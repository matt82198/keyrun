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
/// WIPE-IS-RELEASE (ruling R-BACKFILL-2, finding f-47a7a6db; design/BIBLE.md sections 8 and 9.1; WoW Mythic+). On a
/// layout run a wipe is a release and a run-back, not a run end: every dead seat releases and, after
/// <see cref="RunWiring.ReleaseDelayTicks"/>, respawns where the run placed it (the dungeon entrance), the run timer
/// keeps counting, D46's death and wipe time is the only penalty, and the run ends only at the timer cap
/// (<see cref="Timer.CapTicks"/>, run clock), the boss kill or abandonment. This replaces the forced depleted
/// finish after <see cref="RunWiring.WipeGraceTicks"/>, which stays only for layout-less runs (the frozen TS oracle's
/// rule, sim/run-wiring.ts:178).
/// </summary>
public sealed class WipeIsReleaseTests
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

    private static readonly Dictionary<int, Vec3> Placed = new()
    {
        [Warden] = new Vec3(0, 0, 0),
        [Chanter] = new Vec3(-2000, 0, 0),
        [Cinder] = new Vec3(-1000, 0, 0),
    };

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

    // The OpenSeatWipeTests dungeon: p1's ring crosses the z = 0 walk line, so walkers engage it near x 18536.
    private static readonly DungeonDef Dungeon = new("nav", new[]
    {
        new PullDef("p1", new[] { RunTestKit.Spawn(Enemy, "dummy", 22000, 2000, 10) }),
        new PullDef("p2", new[] { RunTestKit.Spawn(201, "dummy", 38000, 4000, 10) }),
    });

    private static Run CreateThree(int? parTicks = null)
    {
        var nuke = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1000, "physical") });
        var layout = Layout();
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["nuke"] = nuke }, bounds: layout.Extent) with
        {
            Layout = layout,
            ParTicks = parTicks,
        };
        return RunRoot.CreateRun(
            1,
            new[]
            {
                RunTestKit.Member(Warden, "warden", ActorRole.Tank, Placed[Warden].X, Placed[Warden].Z, 400, 100),
                RunTestKit.Member(Chanter, "chanter", ActorRole.Healer, Placed[Chanter].X, Placed[Chanter].Z, 250, 100),
                RunTestKit.Member(Cinder, "cinder", ActorRole.Damage, Placed[Cinder].X, Placed[Cinder].Z, 250, 100),
            },
            Dungeon,
            content);
    }

    private static MoveCommand East(int tick, int actor) => new(tick, actor, new Dir2(1000, 0), 0);

    private static RunStepResult Step(Run run, StepRunOpts? opts, params Command[] commands) => RunRoot.StepRun(run, commands, opts);

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

    // The pull's enemy kills the given seats, one per tick; returns each seat's death tick (the post-step tick).
    private static (Run Run, Dictionary<int, int> DeathTicks) Kill(Run run, StepRunOpts? opts, params int[] victims)
    {
        var deaths = new Dictionary<int, int>();
        foreach (var victim in victims)
        {
            var s = Step(run, opts, new CastCommand(run.State.Tick, Enemy, "nuke", victim));
            Assert.Contains(s.Events, e => e.Type == "death" && e.Get("actorId") is JsInt id && id.Value == victim);
            deaths[victim] = s.Run.State.Tick;
            run = s.Run;
        }
        return (run, deaths);
    }

    // Steps with no commands until the run's tick reaches untilTick; returns every event on the way.
    private static (Run Run, List<SimEvent> Events) StepTo(Run run, StepRunOpts? opts, int untilTick)
    {
        var events = new List<SimEvent>();
        while (run.State.Tick < untilTick)
        {
            var s = Step(run, opts);
            events.AddRange(s.Events);
            run = s.Run;
        }
        return (run, events);
    }

    [Fact(DisplayName = "Wipe is release: the whole party dies mid-dungeon and the run does not end")]
    public void A_full_wipe_mid_dungeon_does_not_end_the_run()
    {
        var run = WalkInUntilPullStart(CreateThree(), null, Warden, Chanter, Cinder);
        var (killed, deaths) = Kill(run, null, Warden, Chanter, Cinder);
        Assert.True(RunWiring.IsWipe(killed.State));

        var lastDeath = deaths.Values.Max();
        var (after, events) = StepTo(killed, null, lastDeath + (RunWiring.WipeGraceTicks * 4) + RunWiring.ReleaseDelayTicks);

        Assert.DoesNotContain(events, e => e.Type == "run_end" || e.Type == "finish");
        Assert.Null(after.RunEnd.Result);
        Assert.False(after.Encounter.Depleted, "a wipe forced the key depleted");
        Assert.Equal(0, after.Encounter.PullIndex);
    }

    [Fact(DisplayName = "Wipe is release: each dead seat respawns at the entrance exactly after the release delay")]
    public void Dead_seats_respawn_at_the_entrance_after_the_release_delay()
    {
        var run = WalkInUntilPullStart(CreateThree(), null, Warden, Chanter, Cinder);
        var (killed, deaths) = Kill(run, null, Warden, Chanter, Cinder);

        foreach (var (seat, deathTick) in deaths.OrderBy(kv => kv.Value))
        {
            var (before, _) = StepTo(killed, null, deathTick + RunWiring.ReleaseDelayTicks - 1);
            Assert.False(before.State.Actors[seat].Alive, $"seat {seat} came back before its release delay ended (tick {before.State.Tick})");

            var s = Step(before, null);
            var back = s.Run.State.Actors[seat];
            Assert.True(back.Alive, $"seat {seat} did not respawn at tick {s.Run.State.Tick} (died {deathTick}, delay {RunWiring.ReleaseDelayTicks})");
            Assert.Equal(back.MaxHealth, back.Health);
            Assert.Equal(Placed[seat], back.Pos);
            Assert.True(Movement.Contains(Entrance, back.Pos));
            Assert.Null(back.CastId);
            Assert.Empty(back.Effects);
            Assert.Contains(s.Events, e => e.Type == "respawn" && e.Get("actorId") is JsInt id && id.Value == seat);
            killed = s.Run;
        }
        Assert.False(RunWiring.IsWipe(killed.State));
        Assert.Null(killed.RunEnd.Result);
    }

    [Fact(DisplayName = "Wipe is release: the run timer keeps counting through the release, and D46 time is the only penalty")]
    public void The_timer_keeps_counting_through_the_release()
    {
        var run = WalkInUntilPullStart(CreateThree(), null, Warden, Chanter, Cinder);
        var penaltyBefore = run.Encounter.PenaltyTicks;
        var (killed, deaths) = Kill(run, null, Warden, Chanter, Cinder);
        var timerAtWipe = killed.State.RunTimerTicks;

        var span = RunWiring.ReleaseDelayTicks + 300;
        var (after, _) = StepTo(killed, null, killed.State.Tick + span);

        Assert.Equal(timerAtWipe + span, after.State.RunTimerTicks);
        Assert.Equal((3 * Timer.DeathSlackCostTicks) + Timer.WipeSlackCostTicks, after.Encounter.PenaltyTicks - penaltyBefore);
        Assert.Equal(3, after.Encounter.Deaths - run.Encounter.Deaths);
        Assert.All(new[] { Warden, Chanter, Cinder }, id => Assert.True(after.State.Actors[id].Alive));
    }

    [Fact(DisplayName = "Wipe is release: one dead seat releases too, while the rest of the party fights on")]
    public void A_single_death_releases_and_respawns_at_the_entrance()
    {
        var run = WalkInUntilPullStart(CreateThree(), null, Warden, Chanter, Cinder);
        var (killed, deaths) = Kill(run, null, Cinder);

        var (after, events) = StepTo(killed, null, deaths[Cinder] + RunWiring.ReleaseDelayTicks);

        Assert.True(after.State.Actors[Cinder].Alive);
        Assert.Equal(Placed[Cinder], after.State.Actors[Cinder].Pos);
        Assert.Single(events, e => e.Type == "respawn");
        Assert.True(Movement.Contains(Room1, after.State.Actors[Warden].Pos), "the living seats were moved by another seat's release");
    }

    [Fact(DisplayName = "Wipe is release: the key depletes only at the timer cap, which ends the run on the run clock")]
    public void The_key_depletes_only_at_the_timer_cap()
    {
        var run = WalkInUntilPullStart(CreateThree(parTicks: 2000), null, Warden, Chanter, Cinder);
        var (killed, _) = Kill(run, null, Warden, Chanter, Cinder);
        var cap = Timer.CapTicks(killed.Encounter);
        Assert.True(cap > killed.Encounter.ParTicks, $"cap {cap} must lie past par {killed.Encounter.ParTicks}: a party may still finish a depleted key");

        SimEvent? end = null;
        var current = killed;
        for (var i = 0; i < (int)cap + 10 && end is null; i++)
        {
            var s = Step(current, null);
            end = s.Events.FirstOrDefault(e => e.Type == "run_end");
            if (end is null)
            {
                Assert.True(Timer.ClockTicks(s.Run.Encounter, s.Run.State.RunTimerTicks) < cap, $"the run outlived its cap at tick {s.Run.State.Tick}");
                Assert.False(s.Run.Encounter.Depleted, $"the key depleted before the cap, at tick {s.Run.State.Tick}");
            }
            current = s.Run;
        }

        Assert.True(end is not null, $"no run_end by the timer cap ({cap} on the run clock)");
        Assert.Equal("depleted", ((JsString)end!.Get("result")!).Value);
        Assert.Equal(cap, Timer.ClockTicks(current.Encounter, current.RunEnd.ResultTick!.Value));
        Assert.True(current.Encounter.Depleted);
        Assert.Equal(RunEndTitle.OutOfTime(current.Encounter.KeyLevel), RunEndTitle.ForRun(current, 1, 2));
    }

    [Fact(DisplayName = "Wipe is release: an abandoned seat is not in the run and is never respawned or moved by a release")]
    public void An_open_seat_is_not_released()
    {
        var opts = new StepRunOpts(OpenSeats: new[] { Chanter });
        var run = WalkInUntilPullStart(CreateThree(), opts, Warden, Cinder);
        var chanterAt = run.State.Actors[Chanter].Pos;
        var (killed, deaths) = Kill(run, opts, Warden, Cinder);
        Assert.True(RunWiring.IsWipe(killed.State, opts.OpenSeats));

        var (after, events) = StepTo(killed, opts, deaths.Values.Max() + RunWiring.ReleaseDelayTicks + 50);

        Assert.Null(after.RunEnd.Result);
        Assert.True(after.State.Actors[Warden].Alive);
        Assert.True(after.State.Actors[Cinder].Alive);
        Assert.Equal(chanterAt, after.State.Actors[Chanter].Pos);
        Assert.DoesNotContain(events, e => e.Type == "respawn" && e.Get("actorId") is JsInt id && id.Value == Chanter);
    }

    [Fact(DisplayName = "Wipe is release control: a layout-less run (the frozen TS oracle's rule) still finishes a lasting wipe depleted")]
    public void A_layoutless_run_keeps_the_frozen_wipe_finish()
    {
        var nuke = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1000, "physical") });
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["nuke"] = nuke });
        var run = RunRoot.CreateRun(1, new[] { RunTestKit.Member(Warden, "warden", ActorRole.Tank, 0, 0, 400, 100) },
            RunTestKit.OnePull("flat", "p1", RunTestKit.Spawn(Enemy, "dummy", 2000, 0, 10)), content);
        var s = Step(run, null, new CastCommand(run.State.Tick, Enemy, "nuke", Warden));
        var deathTick = s.Run.State.Tick;

        var (after, events) = StepTo(s.Run, null, deathTick + RunWiring.WipeGraceTicks);

        Assert.Contains(events, e => e.Type == "run_end" && e.Get("result") is JsString r && r.Value == "depleted");
        Assert.Equal(deathTick + RunWiring.WipeGraceTicks, after.RunEnd.ResultTick);
        Assert.DoesNotContain(events, e => e.Type == "respawn");
    }
}
