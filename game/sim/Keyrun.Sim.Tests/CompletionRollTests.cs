using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-ROLL-1 (event store seq 80; BIBLE s1 completion roll; design/PROGRESSION-PIVOT.md s2.1-2.3): the end-of-run
/// choice of three is replaced by one completion roll per party seat. Draw 0 for a seat is
/// <c>Prng.NextInt(Prng.SeedState(EndOfRun.RewardSeed(run) * 31 + seat), 10000) &lt; dropBp</c>, dropBp 6000 for a timed
/// clear and 3500 for a depleted run; a roll that misses is outcome "nothing", never a coin flip.
/// </summary>
/// <remarks>
/// RED on main 07e2be4b: no <c>completion_roll</c> event exists yet, so every fact below fails on its assertion.
/// API S2 must add (declared here so the facts compile against existing types only):
/// <list type="bullet">
/// <item>Event tag <c>"completion_roll"</c>, emitted by the run-end phase at the <c>run_end</c> tick, exactly one per
/// party seat. Seat = the party member's actor id (seats 1 and 2 in this fixture).</item>
/// <item>Fields: <c>seat</c> (JsInt), <c>outcome</c> (JsString, <c>"item"</c> or <c>"nothing"</c>), <c>itemId</c>
/// (JsString, present only when outcome is <c>"item"</c>); the tick is <see cref="SimEvent.Tick"/>.</item>
/// <item>Table <c>game/content/data/loot/completion.json</c> (asserted in Keyrun.Content.Tests CompletionLootTests):
/// <c>dropBpTimed</c> 6000, <c>dropBpDepleted</c> 3500, <c>slots</c> of five.</item>
/// <item>Its <c>run_end</c> event stays <c>{result, tick}</c>; the roll is a separate event.</item>
/// </list>
/// The choice of three is deliberately not asserted absent here (PR 2 removes it).
/// </remarks>
public sealed class CompletionRollTests
{
    private const int EnemyId = 10;
    private const string RollTag = "completion_roll";
    private const long DropDenominator = 10000;

    // Mirrors game/content/data/loot/completion.json; CompletionLootTests asserts these exact values.
    private const long DropBpTimed = 6000;
    private const long DropBpDepleted = 3500;

    // A par far past the kill tick makes a clear timed; a par of 10 ticks makes the same kill an untimed (depleted) clear.
    private const int TimedPar = 100000;
    private const int DepletedPar = 10;

    /// <summary>A two-seat run (tank seat 1, damage seat 2) whose one enemy dies at tick 701, ended by StepRun.</summary>
    private static (Run Run, List<SimEvent> Events) Completed(long seed, int parTicks)
    {
        var live = RunRoot.CreateRun(
            seed,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 500, 100),
                RunTestKit.Member(2, "cinder", ActorRole.Damage, 1000, 0, 250, 100),
            },
            RunTestKit.OnePull("completion-roll-fixture", "the-only-pull", RunTestKit.Spawn(EnemyId, "dummy", 2000, 0, 1000)),
            RunTestKit.Content(lockoutTicks: 0) with { KeyLevel = 7, ParTicks = parTicks });
        var (ready, _) = RunTestKit.StepUntil(live, 700);
        Assert.Null(ready.RunEnd.Result);
        ready.State.Actors[EnemyId].Alive = false;
        ready.State.Actors[EnemyId].Health = 0;
        var stepped = RunRoot.StepRun(ready, Array.Empty<Command>());
        return (stepped.Run, stepped.Events.ToList());
    }

    private static List<SimEvent> Rolls(IEnumerable<SimEvent> events) => events.Where(e => e.Type == RollTag).ToList();

    private static string? Str(SimEvent e, string key) => (e.Get(key) as JsString)?.Value;

    private static long? Int(SimEvent e, string key) => (e.Get(key) as JsInt)?.Value;

    [Fact]
    public void A_completed_run_emits_one_completion_roll_per_seat_at_the_run_end_tick()
    {
        var (run, events) = Completed(1, TimedPar);
        Assert.Equal(RunResult.Timed, run.RunEnd.Result);

        var rolls = Rolls(events);
        Assert.Equal(2, rolls.Count);
        Assert.Equal(new long[] { 1, 2 }, rolls.Select(r => Int(r, "seat") ?? -1).OrderBy(s => s).ToArray());

        foreach (var roll in rolls)
        {
            Assert.Equal(run.RunEnd.ResultTick, roll.Tick);
            var outcome = Str(roll, "outcome");
            Assert.True(outcome is "item" or "nothing", "outcome must be \"item\" or \"nothing\", was " + outcome);
            if (outcome == "item")
            {
                Assert.False(string.IsNullOrEmpty(Str(roll, "itemId")), "an item roll must carry itemId");
            }
            else
            {
                Assert.Null(roll.Get("itemId"));
            }
        }
    }

    [Fact]
    public void Roll_outcomes_are_deterministic_for_a_seed_and_split_across_seeds_one_to_ten()
    {
        var first = Rolls(Completed(1, TimedPar).Events).Select(r => r.ToCanonicalJson()).ToList();
        var second = Rolls(Completed(1, TimedPar).Events).Select(r => r.ToCanonicalJson()).ToList();
        Assert.NotEmpty(first);
        Assert.Equal(first, second);

        var outcomes = new List<string>();
        for (var seed = 1L; seed <= 10; seed++)
        {
            outcomes.AddRange(Rolls(Completed(seed, TimedPar).Events).Select(r => Str(r, "outcome") ?? "<missing>"));
        }
        Assert.Contains("nothing", outcomes);
        Assert.Contains("item", outcomes);
    }

    [Theory]
    [InlineData(TimedPar, RunResult.Timed, DropBpTimed)]
    [InlineData(DepletedPar, RunResult.Depleted, DropBpDepleted)]
    public void Each_seat_draws_its_first_roll_from_SeedState_of_RewardSeed_times_31_plus_seat(int parTicks, RunResult expectedResult, long dropBp)
    {
        var (run, events) = Completed(1, parTicks);
        Assert.Equal(expectedResult, run.RunEnd.Result);

        var rolls = Rolls(events);
        foreach (var seat in new long[] { 1, 2 })
        {
            var roll = rolls.SingleOrDefault(r => Int(r, "seat") == seat);
            Assert.NotNull(roll);

            var state = Prng.SeedState(unchecked(EndOfRun.RewardSeed(run) * 31 + seat));
            var (raw, _) = Prng.NextInt(state, DropDenominator);
            var expected = raw < dropBp ? "item" : "nothing";
            Assert.Equal(expected, Str(roll!, "outcome"));
        }
    }
}
