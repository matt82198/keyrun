using System.Collections.Generic;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// Finding f-7c7abdf4 (BIBLE s8, Matt 2026-10-20: "an untimed key stays at the same level, with no drop"): the key the
/// end-of-run screen reopens at. <see cref="Timer.KeyAfterRun"/> is the one call Screen.OnStepped makes, and the band
/// it feeds is <see cref="ScreenState.Open"/>, so these cases drive the same chain the player sees.
/// </summary>
public sealed class KeyAfterRunTests
{
    private const int Par = 10000;

    private const string Dungeon = "hollow-vault";

    private static readonly IReadOnlyList<string> Four = new[] { "echo", "tide", "hush", "pact" };

    private static int Reopened(int keyLevel, RunResult result, int finishTick) =>
        ScreenState.Open(Dungeon, Four, Timer.KeyAfterRun(keyLevel, result, finishTick, Par), 0, ScreenState.NoClauseStartKey).KeyLevel;

    [Fact]
    public void An_untimed_run_keeps_the_key_at_the_level_it_was_played_at_for_every_key_and_finish()
    {
        foreach (var result in new[] { RunResult.Depleted, RunResult.Abandoned })
        {
            for (var key = ScreenState.MinKey; key <= ScreenState.MaxKey; key++)
            {
                foreach (var finish in new[] { 1, 5000, 6000, 8000, Par, Par + 1, 3 * Par })
                {
                    Assert.Equal(key, Timer.KeyAfterRun(key, result, finish, Par));
                    Assert.Equal(key, Reopened(key, result, finish));
                }
            }
        }
    }

    [Fact]
    public void A_depleted_run_through_FinishRun_reopens_the_band_at_the_same_key()
    {
        var encounter = EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(7), par: Par, keyLevel: 7);
        var finish = Par + Timer.InitialSlackTicks(7) + 1;
        var result = Timer.FinishRun(encounter, finish, completed: true, _ => { });
        Assert.Equal(RunResult.Depleted, result);
        Assert.Equal(7, Reopened(7, result, finish));
    }

    [Fact]
    public void A_timed_run_still_upgrades_by_3_2_and_1_and_clamps_at_the_top_key()
    {
        Assert.Equal(10, Reopened(7, RunResult.Timed, 6000));
        Assert.Equal(9, Reopened(7, RunResult.Timed, 6001));
        Assert.Equal(9, Reopened(7, RunResult.Timed, 8000));
        Assert.Equal(8, Reopened(7, RunResult.Timed, 8001));
        Assert.Equal(8, Reopened(7, RunResult.Timed, Par));
        Assert.Equal(ScreenState.MaxKey, Reopened(19, RunResult.Timed, 1));
        Assert.Equal(ScreenState.MinKey + 3, Reopened(ScreenState.MinKey, RunResult.Timed, 1));
    }

    [Fact]
    public void NextKeyLevel_never_lowers_the_key_even_for_a_negative_tier()
    {
        Assert.Equal(7, Timer.NextKeyLevel(7, -1));
        Assert.Equal(7, Timer.NextKeyLevel(7, 0));
    }
}
