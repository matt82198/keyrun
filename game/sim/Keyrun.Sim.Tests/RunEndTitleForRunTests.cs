using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The run-end panel's title for a real finished run (<see cref="RunEndTitle.ForRun"/>, the one read RunEndView.Of
/// draws). A timed clear that finished past par but inside its Slack must read "over par", never "within par" (P0:
/// wrong data shown to the player), and must equal the sim's <see cref="RunEndTitle.Of"/> for the same run. Every
/// case ends on the same tick T; only par moves, so T lands exactly on par, on par+1, and on the last Slack tick.
/// </summary>
public sealed class RunEndTitleForRunTests
{
    private const int EnemyId = 10;

    // Steps the run this far with the enemy alive (nothing deals damage, so no Slack is spent), then clears it.
    private const int ClearAfterTicks = 700;

    private static Run EndedRun(int parTicks)
    {
        var run = RunRoot.CreateRun(
            515151,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 500, 100),
                RunTestKit.Member(2, "cinder", ActorRole.Damage, 1000, 0, 250, 100),
            },
            RunTestKit.OnePull("title-fixture", "the-only-pull", RunTestKit.Spawn(EnemyId, "dummy", 2000, 0, 1000)),
            RunTestKit.Content(lockoutTicks: 0) with { KeyLevel = 2, ParTicks = parTicks });
        var (live, _) = RunTestKit.StepUntil(run, ClearAfterTicks);
        Assert.Null(live.RunEnd.Result);
        live.State.Actors[EnemyId].Alive = false;
        live.State.Actors[EnemyId].Health = 0;
        var ended = RunRoot.StepRun(live, System.Array.Empty<Command>()).Run;
        Assert.NotNull(ended.RunEnd.ResultTick);
        return ended;
    }

    // The tick every fixture run ends on (par never changes when the run ends, only how it is judged).
    private static int EndTick() => EndedRun(100000).RunEnd.ResultTick!.Value;

    private static string SimTitle(Run run) =>
        RunEndTitle.Of(run.RunEnd.Result!.Value, true, run.RunEnd.ResultTick!.Value, run.Encounter, 0, 0);

    [Fact]
    public void A_clear_on_exactly_par_reads_within_par()
    {
        var run = EndedRun(EndTick());
        Assert.Equal(RunResult.Timed, run.RunEnd.Result);
        Assert.Equal(RunEndTitle.WithinPar, RunEndTitle.ForRun(run, 0, 0));
        Assert.Equal(SimTitle(run), RunEndTitle.ForRun(run, 0, 0));
    }

    [Fact]
    public void A_clear_one_tick_past_par_inside_its_Slack_reads_over_par_not_within_par()
    {
        var run = EndedRun(EndTick() - 1);
        Assert.Equal(RunResult.Timed, run.RunEnd.Result);
        Assert.NotEqual(RunEndTitle.WithinPar, RunEndTitle.ForRun(run, 0, 0));
        Assert.Equal(RunEndTitle.OverPar, RunEndTitle.ForRun(run, 0, 0));
        Assert.Equal(SimTitle(run), RunEndTitle.ForRun(run, 0, 0));
    }

    [Fact]
    public void A_clear_on_the_last_Slack_tick_is_still_timed_and_reads_over_par()
    {
        var run = EndedRun(EndTick() - Timer.InitialSlackTicks(2));
        Assert.Equal(RunResult.Timed, run.RunEnd.Result);
        Assert.Equal(RunEndTitle.OverPar, RunEndTitle.ForRun(run, 0, 0));
        Assert.Equal(SimTitle(run), RunEndTitle.ForRun(run, 0, 0));
    }

    [Fact]
    public void A_clear_one_tick_past_the_Slack_reads_key_depleted()
    {
        var run = EndedRun(EndTick() - Timer.InitialSlackTicks(2) - 1);
        Assert.Equal(RunResult.Depleted, run.RunEnd.Result);
        Assert.Equal(RunEndTitle.Depleted, RunEndTitle.ForRun(run, 0, 0));
        Assert.Equal(SimTitle(run), RunEndTitle.ForRun(run, 0, 0));
    }

    [Fact]
    public void ForRun_is_null_while_the_run_is_live()
    {
        var run = RunRoot.CreateRun(
            515151,
            new[] { RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 500, 100) },
            RunTestKit.OnePull("title-fixture", "the-only-pull", RunTestKit.Spawn(EnemyId, "dummy", 2000, 0, 1000)),
            RunTestKit.Content(lockoutTicks: 0));
        Assert.Null(RunEndTitle.ForRun(run, 0, 0));
    }
}
