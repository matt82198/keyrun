using System;
using Keyrun.Hud;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// Finding f-7c7abdf4 (Matt 2026-10-20, re-asked: "an untimed key stays the same level (no depletion)"). Timed keys
/// still move by their chest tier (+1, +2 within 80% of par, +3 within 60%, f-baeb031a). Guards every key-move read
/// the player sees (<see cref="KeyMove.Of"/>, the panel and the reopened screen's one call) across the whole key band
/// and finish range, and the run clock bar's label (<see cref="SlackReading.Label"/>), which must not tell a WoW player
/// the key was "depleted" (read as a key drop) when the key keeps its level.
/// </summary>
public sealed class UntimedKeyGuardTests
{
    private const int Par = 14400;

    private static readonly int[] Finishes = { 1, 8640, 8641, 11520, 11521, Par, Par + 1, 3 * Par };

    [Fact]
    public void An_untimed_or_abandoned_run_at_every_key_and_finish_returns_the_same_key()
    {
        foreach (var result in new[] { RunResult.Depleted, RunResult.Abandoned })
        {
            for (var key = ScreenState.MinKey; key <= ScreenState.MaxKey; key++)
            {
                foreach (var finish in Finishes)
                {
                    var move = KeyMove.Of(result, key, finish, Par);
                    Assert.Equal((key, key, 0), (move.From, move.To, move.Tier));
                    Assert.Equal(key, ScreenState.Open("hollow-vault", new[] { "echo", "tide", "hush", "pact" }, move.To, 0, ScreenState.NoClauseStartKey).KeyLevel);
                }
            }
        }
    }

    [Fact]
    public void No_result_ever_lowers_the_key()
    {
        foreach (var result in new[] { RunResult.Timed, RunResult.Depleted, RunResult.Abandoned })
        {
            for (var key = ScreenState.MinKey; key <= ScreenState.MaxKey; key++)
            {
                foreach (var finish in Finishes)
                {
                    Assert.True(KeyMove.Of(result, key, finish, Par).To >= key, $"{result} +{key} at {finish}");
                    Assert.True(Timer.KeyAfterRun(key, result, finish, Par) >= key, $"{result} +{key} at {finish}");
                }
            }
        }
    }

    [Theory]
    [InlineData(8640, 3)]
    [InlineData(11520, 2)]
    [InlineData(Par, 1)]
    public void A_timed_run_upgrades_by_its_tier(int finish, int tier)
    {
        var move = KeyMove.Of(RunResult.Timed, 5, finish, Par);
        Assert.Equal((5, 5 + tier, tier), (move.From, move.To, move.Tier));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(20)]
    public void The_run_clock_bar_says_the_key_stays_at_its_level_after_a_lasting_wipe(int key)
    {
        var reading = new SlackReading(0, 620, Depleted: true, RunTimerTicks: 2000, ParTicks: Par, KeyLevel: key, PenaltyTicks: 400, Deaths: 5, End: RunEndCause.Wiped);
        Assert.DoesNotContain("deplet", reading.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FormattableString.Invariant($"Wiped -- key stays +{key}"), reading.Label);
    }

    [Fact]
    public void The_run_clock_bar_still_reads_deaths_while_the_run_is_live()
    {
        var reading = new SlackReading(620, 620, Depleted: false, RunTimerTicks: 2000, ParTicks: Par, KeyLevel: 7, PenaltyTicks: 900, Deaths: 3);
        Assert.Equal("Deaths 3 (+45s)", reading.Label);
    }
}
