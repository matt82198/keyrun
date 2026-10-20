using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The key move a finished run shows on the end-of-run panel (BIBLE s8, Matt 2026-10-20): a timed run moves the key by
/// its chest tier (+1, +2 within 80% of par, +3 within 60% of par), an untimed run keeps the key at the level it was
/// played at (no depletion drop), and the key never passes the band's top (<see cref="ScreenState.MaxKey"/>). The
/// level shown is the level the between-runs screen opens at next.
/// </summary>
public sealed class KeyMoveTests
{
    private const int Par = 14400;

    // 60% and 80% of a 12-minute par, in whole ticks.
    private const int PlusThreeEdge = 8640;
    private const int PlusTwoEdge = 11520;

    [Theory]
    [InlineData(PlusThreeEdge, 10, 3)]
    [InlineData(PlusThreeEdge + 1, 9, 2)]
    [InlineData(PlusTwoEdge, 9, 2)]
    [InlineData(PlusTwoEdge + 1, 8, 1)]
    [InlineData(Par, 8, 1)]
    [InlineData(Par + 1, 8, 1)]
    public void A_timed_run_moves_the_key_by_its_chest_tier_at_each_exact_edge_and_one_tick_over(int finishTick, int to, int tier)
    {
        var move = KeyMove.Of(RunResult.Timed, 7, finishTick, Par);
        Assert.Equal((7, to, tier), (move.From, move.To, move.Tier));
    }

    [Fact]
    public void A_par_that_is_not_a_multiple_of_ten_keeps_the_edges_in_whole_ticks()
    {
        // 80% of 14401 is 11520.8: 11520 is inside +2, 11521 is not.
        Assert.Equal(9, KeyMove.Of(RunResult.Timed, 7, 11520, 14401).To);
        Assert.Equal(8, KeyMove.Of(RunResult.Timed, 7, 11521, 14401).To);
    }

    [Theory]
    [InlineData(RunResult.Depleted)]
    [InlineData(RunResult.Abandoned)]
    public void An_untimed_run_keeps_the_key_at_the_same_level_with_no_drop(RunResult result)
    {
        var move = KeyMove.Of(result, 7, 100, Par);
        Assert.Equal((7, 7, 0), (move.From, move.To, move.Tier));
        Assert.Equal("Key +7 holds  (not timed -- no drop)", move.Line);
    }

    [Theory]
    [InlineData(17, PlusThreeEdge, 20)]
    [InlineData(18, PlusThreeEdge, 20)]
    [InlineData(19, PlusTwoEdge, 20)]
    [InlineData(20, Par, 20)]
    public void The_key_never_passes_the_band_cap(int key, int finishTick, int to)
    {
        Assert.Equal(to, KeyMove.Of(RunResult.Timed, key, finishTick, Par).To);
        Assert.Equal(ScreenState.MaxKey, to);
    }

    [Fact]
    public void The_panel_line_names_the_move_the_tier_and_the_cap()
    {
        Assert.Equal("Key +7 -> +10  (timed +3)", KeyMove.Of(RunResult.Timed, 7, PlusThreeEdge, Par).Line);
        Assert.Equal("Key +7 -> +8  (timed +1)", KeyMove.Of(RunResult.Timed, 7, Par, Par).Line);
        Assert.Equal("Key +19 -> +20  (timed +2, key cap)", KeyMove.Of(RunResult.Timed, 19, PlusTwoEdge, Par).Line);
        Assert.Equal("Key +20 holds  (timed +1, key cap)", KeyMove.Of(RunResult.Timed, 20, Par, Par).Line);
        Assert.Equal("HUD runend key from=7 to=10 tier=3", KeyMove.Of(RunResult.Timed, 7, PlusThreeEdge, Par).LogLine());
    }

    private static Run BuildRun(int keyLevel) => RunRoot.CreateRun(
        424242,
        new[]
        {
            RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 500, 100),
            RunTestKit.Member(2, "cinder", ActorRole.Damage, 1000, 0, 250, 100),
        },
        RunTestKit.OnePull("key-move-fixture", "the-only-pull", RunTestKit.Spawn(10, "dummy", 2000, 0, 1000)),
        RunTestKit.Content(lockoutTicks: 0) with { KeyLevel = keyLevel, ParTicks = Par });

    [Fact]
    public void ForRun_is_null_while_the_run_is_live()
    {
        Assert.Null(KeyMove.ForRun(BuildRun(7)));
    }

    [Fact]
    public void ForRun_moves_a_fast_timed_clear_up_three_levels()
    {
        var run = BuildRun(7);
        run.State.Actors[10].Alive = false;
        run.State.Actors[10].Health = 0;
        var ended = RunRoot.StepRun(run, System.Array.Empty<Command>()).Run;
        Assert.Equal(RunResult.Timed, ended.RunEnd.Result);

        var move = KeyMove.ForRun(ended);
        Assert.NotNull(move);
        Assert.Equal((7, 10, 3), (move!.From, move.To, move.Tier));
    }

    [Fact]
    public void ForRun_keeps_a_wiped_key_at_its_level()
    {
        var run = BuildRun(7);
        foreach (var actor in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player))
        {
            actor.Alive = false;
        }
        var (ended, _) = RunTestKit.StepUntil(run, RunWiring.WipeGraceTicks + 5);
        Assert.Equal(RunResult.Depleted, ended.RunEnd.Result);

        var move = KeyMove.ForRun(ended);
        Assert.Equal((7, 7, 0), (move!.From, move.To, move.Tier));
    }
}
