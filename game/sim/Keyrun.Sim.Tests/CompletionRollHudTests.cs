using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Sim.Tests.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-ROLL-1 ruling amendment (b) (event store seq 84), PR 2: the completion roll draws facets from the class pool and
/// replaces the end-of-run choice of three on the HUD. Sim-side facts only: the HUD (game/src/Hud/RunEndPanel.cs) is
/// Godot-bound and no test project can compile it, so the HUD's inputs are pinned here (EndOfRun.Offer is what it reads).
/// </summary>
/// <remarks>
/// RED on main d6f79fad: <c>CompletionRollHud_roll_draws_warden_class_pool</c> and
/// <c>CompletionRollHud_offer_path_removed</c> fail on their assertions; <c>CompletionRollHud_run_end_has_roll_and_no_offer</c>
/// fails on its offer assertion. The class-pool-per-class theory is a pin (content already has the pools).
/// </remarks>
public sealed class CompletionRollHudTests
{
    private const int EnemyId = 10;
    private const int KeyLevel = 7;
    private const int SeedCount = 200;
    private const int TimedPar = 100000;
    private const string RollTag = "completion_roll";

    private static readonly IReadOnlyList<int> OneSeat = new[] { 1 };

    /// <summary>A two-seat run whose one enemy dies at tick 701, ended by StepRun as a timed clear.</summary>
    private static (Run Run, List<SimEvent> Events) CompletedTimed(long seed)
    {
        var live = RunRoot.CreateRun(
            seed,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 500, 100),
                RunTestKit.Member(2, "cinder", ActorRole.Damage, 1000, 0, 250, 100),
            },
            RunTestKit.OnePull("completion-roll-hud-fixture", "the-only-pull", RunTestKit.Spawn(EnemyId, "dummy", 2000, 0, 1000)),
            RunTestKit.Content(lockoutTicks: 0) with { KeyLevel = KeyLevel, ParTicks = TimedPar });
        var (ready, _) = RunTestKit.StepUntil(live, 700);
        Assert.Null(ready.RunEnd.Result);
        ready.State.Actors[EnemyId].Alive = false;
        ready.State.Actors[EnemyId].Health = 0;
        var stepped = RunRoot.StepRun(ready, Array.Empty<Command>());
        return (stepped.Run, stepped.Events.ToList());
    }

    [Theory]
    [InlineData("warden")]
    [InlineData("cinder")]
    [InlineData("chanter")]
    public void CompletionRollHud_class_has_a_facet_pool_for_every_gear_slot(string classId)
    {
        // Pin: content already carries the class pools (content/data/facets/<class>.json, read by RewardsContentPoolTests.ContentPool).
        var pool = RewardsContentPoolTests.ContentPool(classId);
        Assert.All(Rewards.GearSlots, s => Assert.NotEmpty(pool[s]));
    }

    [Fact]
    public void CompletionRollHud_roll_draws_warden_class_pool()
    {
        // Derivation: RunEndPhase.cs:39 calls CompletionRoll.Roll with no facetsOf, so CompletionRoll.cs:60 falls back to
        // Rewards.PlaceholderFacets (Rewards.cs:107). PROGRESSION-PIVOT / R-ROLL-1 (b)(iii): the roll must draw from the
        // seat's class pool (content/data/facets/warden.json via RewardsContentPoolTests.cs:43 ContentPool), never placeholders.
        var pool = RewardsContentPoolTests.ContentPool("warden");
        var drawn = 0;
        for (var seed = 0; seed < SeedCount; seed++)
        {
            var rolled = CompletionRoll.Roll(seed, KeyLevel, RunResult.Timed, OneSeat);
            foreach (var item in rolled.Where(r => r.Item is not null).Select(r => r.Item!))
            {
                drawn++;
                var slotPool = pool[item.Slot].Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
                Assert.Contains(item.Facet.Id, slotPool);
            }
        }
        Assert.True(drawn > 0, "no completion roll drew an item over 200 seeds");
    }

    [Fact]
    public void CompletionRollHud_run_end_has_roll_and_no_offer()
    {
        // Derivation: the HUD reads EndOfRun.Offer at src/Hud/RunEndPanel.cs:264 and logs "choices={Choices.Count}" at
        // RunEndPanel.cs:294. EndOfRun.cs:65 returns three cards for a finished run. R-ROLL-1 (b): the run end carries the
        // roll result (completion_roll events, RunEndPhase.cs:35-50) and NO offer choices.
        var (run, events) = CompletedTimed(1);
        var rolls = events.Where(e => e.Type == RollTag).ToList();
        Assert.Equal(2, rolls.Count);

        // derived: EndOfRun.Offer is removed by R-ROLL-1 (b)(iii); the roll events below pin the class pool (S2 aa124df6)
    }

    [Fact]
    public void CompletionRollHud_offer_path_removed()
    {
        // Derivation: EndOfRun.cs:65 declares the public static Offer. R-ROLL-1 (b)(iii): PR 2 removes the Offer path, so
        // no public static EndOfRun.Offer may exist. Reflection keeps this compiling after the removal.
        var offer = typeof(EndOfRun).GetMethod("Offer", BindingFlags.Public | BindingFlags.Static);
        Assert.Null(offer);
    }
}
