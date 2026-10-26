using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using C = Keyrun.Content;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The ledger outcome label must match the sim's own run-end title (G7: wrong data is P0). Every case goes through
/// <see cref="Summary.OutcomeOf"/> (the path the summary and the ledger take) over a Hollow Vault run built the way
/// <see cref="PlaytestRunner.Play"/> builds one, then shaped to the end state under test. Par is the dungeon's own par
/// from content, never a literal.
/// </summary>
public sealed class KeyOutcomeLabelTests
{
    private const string DungeonId = "hollow-vault";
    private const int KeyLevel = 2;
    private const long Seed = 3;

    [Fact(DisplayName = "Outcome label: a layout run depleted by penalties under par, uncleared, is the sim's Out of time, labelled untimed (out of time)")]
    public void Layout_run_depleted_by_penalties_under_par_is_out_of_time()
    {
        // RunEndTitle.ForRun: an uncleared depleted layout run was ended by the timer cap ("Out of time -- key stays at +2").
        // The clock (elapsed + penalties) reaches the cap of 2x par while the elapsed run timer is still under par.
        var result = Shaped(layoutRun: true, shape: run =>
        {
            var par = run.Encounter.ParTicks;
            var elapsed = par * 2 / 3;
            run.State.RunTimerTicks = elapsed;
            return run with
            {
                Encounter = run.Encounter with { Depleted = true, PenaltyTicks = 2 * par - elapsed },
                RunEnd = new RunEndState(RunResult.Depleted, 2 * par, null),
            };
        });

        Assert.NotNull(result.Run.Content.Layout);
        Assert.True(result.Run.State.RunTimerTicks < result.Run.Encounter.ParTicks);
        var outcome = Summary.OutcomeOf(result);
        Assert.False(outcome.Timed);
        Assert.Equal("untimed (out of time)", outcome.Label);
    }

    [Fact(DisplayName = "Outcome label: a layout-less run that wipes and is never recovered is labelled untimed (wiped)")]
    public void Layout_less_lasting_wipe_is_labelled_wiped()
    {
        // RunEndTitle.Of: an uncleared depleted run with no layout is "Party wiped on pull N of T". The lasting wipe forces
        // depleted and adds one wipe's penalty (500 ticks).
        var result = Shaped(layoutRun: false, shape: run =>
        {
            run.State.RunTimerTicks = 3000;
            return run with
            {
                Encounter = run.Encounter with { Depleted = true, PenaltyTicks = 500 },
                RunEnd = new RunEndState(RunResult.Depleted, 3000, 2000),
            };
        });

        Assert.Null(result.Run.Content.Layout);
        var outcome = Summary.OutcomeOf(result);
        Assert.False(outcome.Timed);
        Assert.Equal("untimed (wiped)", outcome.Label);
    }

    [Fact(DisplayName = "Outcome label: a layout run the party abandons is the sim's Run abandoned, labelled untimed (abandoned)")]
    public void Layout_run_abandoned_is_labelled_abandoned_not_wiped()
    {
        // RunEndTitle.Of: a RunResult.Abandoned run is "Run abandoned", with no depletion and no wipe.
        var result = Shaped(layoutRun: true, shape: run =>
        {
            run.State.RunTimerTicks = 3000;
            return run with { RunEnd = new RunEndState(RunResult.Abandoned, 3000, null) };
        });

        Assert.NotNull(result.Run.Content.Layout);
        Assert.False(result.Run.Encounter.Depleted);
        var outcome = Summary.OutcomeOf(result);
        Assert.False(outcome.Timed);
        Assert.Equal("untimed (abandoned)", outcome.Label);
    }

    [Fact(DisplayName = "Outcome label: a cleared layout run inside 60% of par keeps the exact timed +3 label")]
    public void Cleared_run_within_sixty_percent_is_timed_plus_three()
    {
        var result = Shaped(layoutRun: true, shape: run =>
        {
            var par = run.Encounter.ParTicks;
            run.State.RunTimerTicks = par * 6 / 10;
            return run with
            {
                Encounter = run.Encounter with { PullIndex = run.Dungeon.Pulls.Count },
                RunEnd = new RunEndState(RunResult.Timed, par * 6 / 10, null),
            };
        });

        Assert.NotNull(result.Run.Content.Layout);
        var outcome = Summary.OutcomeOf(result);
        Assert.True(outcome.Timed);
        Assert.Equal("timed +3", outcome.Label);
    }

    /// <summary>A Hollow Vault run at key 2 built as <see cref="PlaytestRunner.Play"/> builds it, then shaped to the end under test.</summary>
    /// <param name="layoutRun">True to keep the dungeon's layout (a layout run), false to play it without one.</param>
    /// <param name="shape">Sets the end state on the fresh run.</param>
    /// <returns>The playtest result over the shaped run.</returns>
    private static PlaytestResult Shaped(bool layoutRun, System.Func<Run, Run> shape)
    {
        var registry = C.ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, KeyLevel, HostRunSetup.Roster[0].ClassId, withBots: true);
        var content = layoutRun ? setup.Content : setup.Content with { Layout = null };
        var created = RunRoot.CreateRun(Seed, setup.Party, setup.Dungeon, content, null);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        return new PlaytestResult(shape(run), string.Empty, new RunStats(registry, setup.Party), setup, registry);
    }
}
