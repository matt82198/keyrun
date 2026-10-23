using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// STATE.md D45 (orchestrator, 2026-10-21, under Matt's "make the rest of the decisions and we will tune later"):
/// "Rewards: every finished run, timed or depleted, offers the choice of three; a player who leaves mid-run gets
/// nothing." Drives a real run to the sim's abandon end (<see cref="StepRunOpts.AbandonRequested"/>, the host's leave
/// signal) and checks it yields no reward offer, no Ash, no tier change and an unchanged key, while a timed clear, an
/// untimed clear and a lasting wipe keep their offer of three and their Ash.
/// </summary>
public sealed class AbandonedRunGetsNothingTests
{
    private const int EnemyId = 10;
    private const int KeyLevel = 7;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<Facet>> Pools = Rewards.GearSlots.ToDictionary(
        s => s,
        s => (IReadOnlyList<Facet>)Enumerable.Range(0, 4).Select(i => new Facet($"{s}.f{i}", $"{s} facet {i}", $"ability.{s}.{i}")).ToList(),
        StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, Item> NoGear = new Dictionary<string, Item>(StringComparer.Ordinal);

    public enum End
    {
        TimedClear,
        UntimedClear,
        Wipe,
        Abandon,
    }

    private static Run Live(int parTicks, long seed) =>
        RunRoot.CreateRun(
            seed,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 500, 100),
                RunTestKit.Member(2, "cinder", ActorRole.Damage, 1000, 0, 250, 100),
            },
            RunTestKit.OnePull("abandon-fixture", "the-only-pull", RunTestKit.Spawn(EnemyId, "dummy", 2000, 0, 1000)),
            RunTestKit.Content(lockoutTicks: 0) with { KeyLevel = KeyLevel, ParTicks = parTicks });

    private static Run Ended(End end, long seed = 424242)
    {
        var (live, _) = RunTestKit.StepUntil(Live(end == End.UntimedClear ? 10 : 100000, seed), 700);
        Assert.Null(live.RunEnd.Result);
        switch (end)
        {
            case End.Abandon:
                // The player leaves mid-run: the enemy is alive and the party is up.
                Assert.True(live.State.Actors[EnemyId].Alive);
                return RunRoot.StepRun(live, Array.Empty<Command>(), new StepRunOpts(AbandonRequested: true)).Run;
            case End.Wipe:
                foreach (var id in new[] { 1, 2 })
                {
                    live.State.Actors[id].Alive = false;
                    live.State.Actors[id].Health = 0;
                }
                return RunTestKit.StepUntil(live, live.State.Tick + RunWiring.WipeGraceTicks + 2).Run;
            default:
                live.State.Actors[EnemyId].Alive = false;
                live.State.Actors[EnemyId].Health = 0;
                return RunRoot.StepRun(live, Array.Empty<Command>()).Run;
        }
    }

    private static Item Weapon(int tier) =>
        new("reward-weapon-weapon.f0-t" + tier, "weapon", tier, new Facet("weapon.f0", "weapon facet 0", "ability.weapon.0"), Rewards.StartingCharges);

    [Fact]
    public void An_abandoned_run_rolls_nothing_for_every_seat()
    {
        // Migrated from "offered no cards" (the removed EndOfRun.Offer): the completion roll of an abandoned run is nothing.
        for (var seed = 1L; seed <= 20; seed++)
        {
            var run = Ended(End.Abandon, seed);
            Assert.Equal(RunResult.Abandoned, run.RunEnd.Result);
            var rolls = CompletionRoll.ForRun(run);
            Assert.NotNull(rolls);
            Assert.NotEmpty(rolls!);
            Assert.All(rolls, r => Assert.Null(r.Item));
        }
    }

    [Fact]
    public void An_abandoned_run_banks_no_Ash_but_is_still_recorded_as_a_run()
    {
        Assert.Equal(0, Ash.ForResult(RunResult.Abandoned));
        var before = Progression.Start(1).BankRun(RunResult.Timed);
        var after = before.BankRun(RunResult.Abandoned);
        Assert.Equal(before.AshTotal, after.AshTotal);
        Assert.Equal(before.Runs + 1, after.Runs);
        Assert.Equal(RunResult.Abandoned, after.LastRun!.Result);
        Assert.Equal(0, after.LastRun.AshGained);
        Assert.Equal(EndOfRun.NothingLine, after.LastRun.Text());
    }

    [Fact]
    public void A_reward_cannot_be_taken_after_an_abandoned_run_so_no_tier_moves()
    {
        var geared = Progression.Start(1).BankRun(RunResult.Timed).Take(Weapon(2));
        var left = geared.BankRun(RunResult.Abandoned);
        Assert.Throws<InvalidOperationException>(() => left.Take(Weapon(9)));
        Assert.Equal(2, left.Equipped["weapon"].Tier);
        Assert.Null(left.LastRun!.Taken);
    }

    [Fact]
    public void An_abandoned_run_keeps_the_key_at_the_level_it_was_played_at()
    {
        var move = KeyMove.ForRun(Ended(End.Abandon))!;
        Assert.Equal(KeyLevel, move.From);
        Assert.Equal(KeyLevel, move.To);
        Assert.Equal(0, move.Tier);
    }

    [Theory]
    [InlineData(End.TimedClear, RunResult.Timed, Ash.Timed)]
    [InlineData(End.UntimedClear, RunResult.Depleted, Ash.Depleted)]
    [InlineData(End.Wipe, RunResult.Depleted, Ash.Depleted)]
    public void Timed_untimed_and_wiped_runs_keep_their_offer_of_three_and_their_Ash(End end, RunResult expected, int ash)
    {
        var run = Ended(end);
        Assert.Equal(expected, run.RunEnd.Result);
        // The Rewards choice-of-three primitive (the removed Offer's body) still gives a finished run its three cards.
        var offer = Rewards.RollRewardChoices(EndOfRun.RewardSeed(run), run.Encounter.KeyLevel, run.RunEnd.Result!.Value, Rewards.GearSlots, Pools, NoGear);
        Assert.Equal(3, offer.Count);
        Assert.True(ash > 0);
        var banked = Progression.Start(1).BankRun(expected);
        Assert.Equal(ash, banked.AshTotal);
        var taken = banked.Take(offer[0]);
        Assert.Equal(offer[0].Slot, taken.LastRun!.Taken!.Slot);
    }

    [Fact]
    public void A_save_written_before_D45_with_ten_Ash_for_an_abandoned_run_still_loads()
    {
        var old = Progression.Start(5).BankRun(RunResult.Timed);
        // A pre-D45 save recorded the abandoned run's last delta as "+10 Ash"; the history is kept as written.
        var legacy = old with { AshTotal = old.AshTotal + 10, Runs = old.Runs + 1, LastRun = new RunDelta(RunResult.Abandoned, 10, null, null) };
        var loaded = ProgressionSave.Load(ProgressionSave.Serialize(legacy), seed: 9).Progression;
        Assert.Equal(legacy.AshTotal, loaded.AshTotal);
        Assert.Equal(10, loaded.LastRun!.AshGained);
    }

    [Fact]
    public void The_HUD_line_says_nothing_was_granted()
    {
        Assert.Contains("nothing", EndOfRun.NothingLine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RunResult.Abandoned, Ended(End.Abandon).RunEnd.Result);
        Assert.Equal(EndOfRun.NothingLine, EndOfRun.HudLine(Ended(End.Abandon)));
        Assert.Null(EndOfRun.HudLine(Ended(End.TimedClear)));
    }
}
