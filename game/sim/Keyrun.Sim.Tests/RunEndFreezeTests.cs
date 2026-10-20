using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Once <c>run_end</c> has been emitted the run is over: <see cref="RunRoot.StepRun"/> must not resolve any more damage
/// or deaths, so a Sundered Twin (actor 901) left standing when the boss dies can no longer die after the run has
/// ended and rewrite the run record. Everything runs through <see cref="RunRoot.StepRun"/> on the armed boss run.
/// </summary>
public sealed class RunEndFreezeTests
{
    /// <summary>Steps past the Twins' spawn, kills the boss, and steps once so the run ends timed with both Twins up.</summary>
    private static (Run Run, int EndTick) EndWithTwinsStanding()
    {
        var run = TwinsUp();
        run.State.Actors[BossTestKit.Boss].Alive = false;
        run.State.Actors[BossTestKit.Boss].Health = 0;
        var stepped = RunRoot.StepRun(run, System.Array.Empty<Command>());
        Assert.Equal(RunResult.Timed, stepped.Run.RunEnd.Result);
        Assert.Contains(stepped.Events, e => e.Type == "run_end");
        Assert.True(stepped.Run.State.Actors[BossTestKit.TwinA].Alive);
        return (stepped.Run, stepped.Run.State.Tick);
    }

    /// <summary>The live boss run stepped past the Twins' spawn (tick 3), Twin A up: the state the run is ended from.</summary>
    private static Run TwinsUp()
    {
        var (run, _) = BossTestKit.StepUntil(BossTestKit.BuildRun(), 3);
        Assert.True(run.State.Actors[BossTestKit.TwinA].Alive);
        return run;
    }

    /// <summary>The Warden strikes Twin A every tick from <paramref name="from"/> until <paramref name="until"/>.</summary>
    private static List<Command> StrikeTwinA(int from, int until) =>
        Enumerable.Range(from, until - from)
            .Select(t => (Command)new CastCommand(t, BossTestKit.Warden, "warden.strike", BossTestKit.TwinA))
            .ToList();

    [Fact(DisplayName = "After run_end a Twin struck at 1 hp takes no damage and does not die")]
    public void Twin_901_cannot_die_after_run_end()
    {
        var (ended, endTick) = EndWithTwinsStanding();
        ended.State.Actors[BossTestKit.TwinA].Health = 1;

        var (after, events) = BossTestKit.StepUntil(ended, endTick + 200, StrikeTwinA(endTick, endTick + 200));

        Assert.DoesNotContain(events, e => e.Type == "death");
        Assert.DoesNotContain(events, e => e.Type == "damage");
        Assert.True(after.State.Actors[BossTestKit.TwinA].Alive);
        Assert.Equal(1, after.State.Actors[BossTestKit.TwinA].Health);
        Assert.Equal(RunResult.Timed, after.RunEnd.Result);
        Assert.Equal(endTick, after.RunEnd.ResultTick);
    }

    [Fact(DisplayName = "After run_end the Twins' own slams hit nobody and no event is emitted")]
    public void No_events_at_all_after_run_end()
    {
        var (ended, endTick) = EndWithTwinsStanding();
        var partyHealth = ended.State.Actors.Values.Where(a => a.Kind == ActorKind.Player).ToDictionary(a => a.Id, a => a.Health);

        var (after, events) = BossTestKit.StepUntil(ended, endTick + 300);

        Assert.Empty(events);
        Assert.Equal(endTick + 300, after.State.Tick);
        foreach (var (id, health) in partyHealth)
        {
            Assert.Equal(health, after.State.Actors[id].Health);
        }
    }

    [Fact(DisplayName = "Control: before run_end the same strikes do kill Twin A")]
    public void Control_the_strikes_kill_the_twin_while_the_run_is_live()
    {
        var (run, _) = BossTestKit.StepUntil(BossTestKit.BuildRun(), 3);
        run.State.Actors[BossTestKit.TwinA].Health = 1;

        var (after, events) = BossTestKit.StepUntil(run, 203, StrikeTwinA(3, 203));

        Assert.Contains(events, e => e.Type == "death" && e.Get("actorId") is JsInt { Value: BossTestKit.TwinA });
        Assert.False(after.State.Actors[BossTestKit.TwinA].Alive);
        Assert.Null(after.RunEnd.Result);
    }

    [Fact(DisplayName = "Control: in the freeze tests' own harness, before run_end, the Warden's strike damages Twin A")]
    public void Control_the_strike_damages_a_live_twin_before_run_end()
    {
        // Positive control for Twin_901_cannot_die_after_run_end: the same state the run is ended from (TwinsUp), the
        // same StrikeTwinA commands over the same 200-tick span, the run still live. If the strike could not reach or
        // hurt Twin A here, "no damage after run_end" would hold whether or not the run-end freeze exists.
        var run = TwinsUp();
        var start = run.State.Tick;
        var before = run.State.Actors[BossTestKit.TwinA].Health;

        var (after, events) = BossTestKit.StepUntil(run, start + 200, StrikeTwinA(start, start + 200));

        Assert.Null(after.RunEnd.Result);
        Assert.Contains(events, e => e.Type == "damage"
            && e.Get("sourceId") is JsInt { Value: BossTestKit.Warden }
            && e.Get("targetId") is JsInt { Value: BossTestKit.TwinA }
            && e.Get("healthDamage") is JsInt { Value: > 0 });
        Assert.True(after.State.Actors[BossTestKit.TwinA].Health < before);
    }
}
