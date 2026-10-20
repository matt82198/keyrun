using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// The run-end title against par (BIBLE s8). Repro: <see cref="Timer.FinishRun"/> returns Timed for a clean run that
/// finished past par, and the title must not call that "within par".
/// </summary>
public sealed class RunEndTitleTests
{
    private const int Par = 10000;

    private static EncounterState CleanEncounter() =>
        EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(2), par: Par, keyLevel: 2);

    [Fact]
    public void A_clean_run_past_par_still_finishes_timed_so_the_title_cannot_trust_the_result_alone()
    {
        var encounter = CleanEncounter();
        Assert.Equal(RunResult.Timed, Timer.FinishRun(encounter, Par + 500, completed: true, _ => { }));
        Assert.True(Timer.IsOverPar(Par + 500, encounter));
    }

    [Fact]
    public void A_timed_clear_at_or_before_par_is_within_par()
    {
        Assert.Equal(RunEndTitle.WithinPar, RunEndTitle.Of(RunResult.Timed, true, Par - 1, CleanEncounter(), 0, 0));
        Assert.Equal(RunEndTitle.WithinPar, RunEndTitle.Of(RunResult.Timed, true, Par, CleanEncounter(), 0, 0));
        Assert.Equal("Run cleared -- within par", RunEndTitle.WithinPar);
    }

    [Fact]
    public void A_timed_clear_past_par_says_over_par_not_within_par()
    {
        var title = RunEndTitle.Of(RunResult.Timed, true, Par + 1, CleanEncounter(), 0, 0);
        Assert.Equal(RunEndTitle.OverPar, title);
        Assert.Equal("Run cleared -- over par", title);
    }

    [Fact]
    public void A_depleted_clear_keeps_the_depleted_title_on_either_side_of_par()
    {
        var encounter = CleanEncounter() with { SlackTicks = 0, Depleted = true };
        Assert.Equal("Run cleared -- key depleted", RunEndTitle.Of(RunResult.Depleted, true, Par - 1, encounter, 0, 0));
        Assert.Equal("Run cleared -- key depleted", RunEndTitle.Of(RunResult.Depleted, true, Par + 1, encounter, 0, 0));
    }

    [Fact]
    public void A_wipe_and_an_abandon_keep_their_titles_past_par()
    {
        Assert.Equal("Party wiped on pull 2 of 4 -- key depleted", RunEndTitle.Of(RunResult.Depleted, false, Par + 1, CleanEncounter(), 2, 4));
        Assert.Equal("Run abandoned", RunEndTitle.Of(RunResult.Abandoned, false, Par + 1, CleanEncounter(), 1, 4));
        Assert.Equal("Run abandoned", RunEndTitle.Of(RunResult.Abandoned, true, Par + 1, CleanEncounter(), 0, 0));
    }
}
