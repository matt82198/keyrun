using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-K4-16 (event store seq 151): the completion roll's reward seed can be pinned by a reward-seed override, so the scene
/// tests that roll after a run are independent of the tick the run ended on. Without the override the seed stays
/// <c>EndOfRun.RewardSeedOf(seed, ResultTick)</c> (seed x 1000003 + the end tick), which moves whenever ResultTick moves.
/// </summary>
/// <remarks>
/// RED on bd76b66d: the override symbol <c>StepRunOpts.RewardSeedOverride</c> does not exist yet, so this file does not
/// compile. S2 adds it as an optional <c>long?</c> parameter of <c>StepRunOpts</c> (game/sim/Keyrun.Sim/Run/RunTypes.cs:248)
/// and reads it in <c>RunEndPhase.EmitCompletionRolls</c> (game/sim/Keyrun.Sim/Run/Phases/RunEndPhase.cs:37) in place of
/// <c>EndOfRun.RewardSeedOf(state.Seed, state.Tick)</c>.
/// The override value 1 is the smallest positive reward seed whose seat 1 draw hits at dropBp 3500: seat 1 is seed 1 x 31 + 1
/// = 32, and <c>Prng.NextInt(Prng.SeedState(32), 10000)</c> is 217 (&lt; 3500). Verified against prng.vectors.json.
/// </remarks>
public sealed class RewardSeedOverrideTests
{
    private const int EnemyId = 10;
    private const string RollTag = "completion_roll";
    private const long DropDenominator = 10000;
    private const long DropBpTimed = 6000;

    // A par far past the kill tick makes the clear timed, so the roll's drop chance is DropBpTimed.
    private const int TimedPar = 100000;

    // The override that pins the roll: seat 1 hits at dropBp 3500 (raw 217). See the remarks above.
    private const long PinnedRewardSeed = 1;

    /// <summary>
    /// A two-seat run (tank seat 1, damage seat 2) whose one enemy dies on the tick the run is stepped to, ended by StepRun
    /// with the given options. <paramref name="killAtTick"/> sets the run-end tick (the kill is the step after it).
    /// </summary>
    private static (Run Run, List<SimEvent> Events) Completed(long seed, int killAtTick, StepRunOpts? opts)
    {
        var live = RunRoot.CreateRun(
            seed,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 500, 100),
                RunTestKit.Member(2, "cinder", ActorRole.Damage, 1000, 0, 250, 100),
            },
            RunTestKit.OnePull("reward-seed-override-fixture", "the-only-pull", RunTestKit.Spawn(EnemyId, "dummy", 2000, 0, 1000)),
            RunTestKit.Content(lockoutTicks: 0) with { KeyLevel = 7, ParTicks = TimedPar });
        var (ready, _) = RunTestKit.StepUntil(live, killAtTick);
        Assert.Null(ready.RunEnd.Result);
        ready.State.Actors[EnemyId].Alive = false;
        ready.State.Actors[EnemyId].Health = 0;
        var stepped = RunRoot.StepRun(ready, Array.Empty<Command>(), opts);
        return (stepped.Run, stepped.Events.ToList());
    }

    private static List<SimEvent> Rolls(IEnumerable<SimEvent> events) => events.Where(e => e.Type == RollTag).ToList();

    private static string? Str(SimEvent e, string key) => (e.Get(key) as JsString)?.Value;

    private static long? Int(SimEvent e, string key) => (e.Get(key) as JsInt)?.Value;

    /// <summary>The roll outcome of every seat, keyed by seat, with the item id (or null) and no tick, so runs that end on different ticks compare equal.</summary>
    private static string Outcomes(IEnumerable<SimEvent> events) =>
        string.Join(";", Rolls(events).OrderBy(r => Int(r, "seat")).Select(r => $"{Int(r, "seat")}:{Str(r, "outcome")}:{Str(r, "itemId") ?? "-"}"));

    [Fact]
    public void A_reward_seed_override_pins_the_completion_rolls_across_different_run_end_ticks()
    {
        var opts = new StepRunOpts(RewardSeedOverride: PinnedRewardSeed);
        var early = Completed(1, 700, opts);
        var late = Completed(1, 740, opts);

        Assert.Equal(RunResult.Timed, early.Run.RunEnd.Result);
        Assert.Equal(RunResult.Timed, late.Run.RunEnd.Result);
        Assert.NotEqual(early.Run.RunEnd.ResultTick, late.Run.RunEnd.ResultTick);

        Assert.Equal(Outcomes(early.Events), Outcomes(late.Events));
    }

    [Fact]
    public void A_reward_seed_override_rolls_each_seat_from_SeedState_of_the_override_times_31_plus_seat()
    {
        var (run, events) = Completed(1, 700, new StepRunOpts(RewardSeedOverride: PinnedRewardSeed));
        Assert.NotEqual(PinnedRewardSeed, EndOfRun.RewardSeedOf(1, run.RunEnd.ResultTick!.Value));

        foreach (var seat in new long[] { 1, 2 })
        {
            var roll = Rolls(events).SingleOrDefault(r => Int(r, "seat") == seat);
            Assert.NotNull(roll);

            var (raw, _) = Prng.NextInt(Prng.SeedState(unchecked((PinnedRewardSeed * 31) + seat)), DropDenominator);
            var expected = raw < DropBpTimed ? "item" : "nothing";
            Assert.Equal(expected, Str(roll!, "outcome"));
        }

        var seatOne = Rolls(events).Single(r => Int(r, "seat") == 1);
        Assert.Equal("item", Str(seatOne, "outcome"));
    }

    [Fact]
    public void Without_an_override_the_completion_rolls_keep_the_run_end_tick_reward_seed()
    {
        var (run, events) = Completed(1, 700, null);
        Assert.Equal(RunResult.Timed, run.RunEnd.Result);

        var rewardSeed = EndOfRun.RewardSeed(run);
        Assert.Equal(EndOfRun.RewardSeedOf(1, run.RunEnd.ResultTick!.Value), rewardSeed);

        foreach (var seat in new long[] { 1, 2 })
        {
            var roll = Rolls(events).SingleOrDefault(r => Int(r, "seat") == seat);
            Assert.NotNull(roll);

            var (raw, _) = Prng.NextInt(Prng.SeedState(unchecked((rewardSeed * 31) + seat)), DropDenominator);
            var expected = raw < DropBpTimed ? "item" : "nothing";
            Assert.Equal(expected, Str(roll!, "outcome"));
        }
    }

    [Fact]
    public void A_pinned_run_records_the_reward_seed_it_used_and_the_panel_roll_matches_the_completion_roll_events()
    {
        var (run, events) = Completed(1, 700, new StepRunOpts(RewardSeedOverride: PinnedRewardSeed));
        Assert.Equal<long?>(PinnedRewardSeed, run.State.RewardSeed);

        var panel = CompletionRoll.ForRun(run)!
            .OrderBy(r => r.Seat)
            .Select(r => $"{r.Seat}:{(r.Item is null ? "nothing" : "item")}:{r.Item?.Id ?? "-"}");
        Assert.Equal(Outcomes(events), string.Join(";", panel));
    }

    [Fact]
    public void Without_an_override_the_recorded_reward_seed_is_the_run_end_tick_reward_seed()
    {
        var (run, _) = Completed(1, 700, null);
        Assert.Equal<long?>(EndOfRun.RewardSeed(run), run.State.RewardSeed);
    }
}
