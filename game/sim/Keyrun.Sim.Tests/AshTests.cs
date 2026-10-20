using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The BIBLE s7 progression loop (ENGINE-PLAN 3D): Ash banked every run win or lose, the end-of-run pick equipped and
/// capped at Tier 10, and the CRAFTING REROLL: two charges per item, each reroll draws only from that slot's listed
/// pool, a third is refused.
/// </summary>
public sealed class AshTests
{
    // A weapon pool that does NOT contain the starting item's Facet, so "landed in the pool" is not satisfied by a
    // reroll that leaves the Facet alone.
    private static readonly IReadOnlyList<Facet> WeaponPool = new[]
    {
        new Facet("warden.weapon.keenEdge", "Strike becomes a narrow frontal cone.", "warden.strike"),
        new Facet("warden.weapon.wideningBreak", "Guard Break trades range for a wider cone.", "warden.guardBreak"),
        new Facet("warden.weapon.twinStrike", "Reckoning becomes a piercing line.", "warden.reckoning"),
        new Facet("warden.weapon.mobileWard", "Bulwark becomes a self-aura.", "warden.bulwark"),
    };

    private static Item Fresh(int tier = 3) =>
        new("reward-weapon-weapon-edge-t3-0", "weapon", tier, new Facet("weapon-edge", "Strike also applies Sundered", "ability-1"), Rewards.StartingCharges);

    [Fact]
    public void Ash_is_banked_for_every_finished_run_win_or_lose_and_none_for_a_left_run()
    {
        Assert.Equal(Ash.Timed, Ash.ForResult(RunResult.Timed));
        Assert.Equal(Ash.Depleted, Ash.ForResult(RunResult.Depleted));
        Assert.Equal(Ash.Abandoned, Ash.ForResult(RunResult.Abandoned));
        // STATE.md D45: "a player who leaves mid-run gets nothing" (superseded progression.ts ASH_ABANDONED = 10).
        Assert.True(Ash.Timed > Ash.Depleted && Ash.Depleted > 0 && Ash.Abandoned == 0);

        var p = Progression.Start(1).BankRun(RunResult.Timed).BankRun(RunResult.Depleted).BankRun(RunResult.Abandoned);
        Assert.Equal(Ash.Timed + Ash.Depleted, p.AshTotal);
        Assert.Equal(3, p.Runs);
        Assert.Equal(0, p.LastRun!.AshGained);
        Assert.Equal(RunResult.Abandoned, p.LastRun.Result);
    }

    [Fact]
    public void A_thousand_seeded_rerolls_all_land_in_the_slot_pool()
    {
        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var seed = 0; seed < 1000; seed++)
        {
            var r = Crafting.Reroll(Fresh(), WeaponPool, Prng.SeedState((long)seed));
            Assert.True(r.Ok, $"seed {seed}: {r.Refusal}");
            Assert.Contains(r.Item.Facet, WeaponPool);
            Assert.Equal(Rewards.StartingCharges - 1, r.Item.Charges);
            Assert.Equal("weapon", r.Item.Slot);
            Assert.Equal(3, r.Item.Tier);
            hits[r.Item.Facet.Id] = hits.TryGetValue(r.Item.Facet.Id, out var n) ? n + 1 : 1;

            var r2 = Crafting.Reroll(r.Item, WeaponPool, r.Prng);
            Assert.True(r2!.Ok);
            Assert.Contains(r2.Item.Facet, WeaponPool);
            Assert.Equal(0, r2.Item.Charges);
        }
        // Every pool entry is reachable: the draw is not stuck on one index.
        Assert.Equal(WeaponPool.Count, hits.Count);
        Assert.All(hits.Values, n => Assert.True(n > 150, $"skewed draw: {n} of 1000"));
    }

    [Fact]
    public void The_third_reroll_call_is_refused()
    {
        var prng = Prng.SeedState(7L);
        var first = Crafting.Reroll(Fresh(), WeaponPool, prng);
        var second = Crafting.Reroll(first.Item, WeaponPool, first.Prng);
        var third = Crafting.Reroll(second.Item, WeaponPool, second.Prng);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.False(third.Ok);
        Assert.NotNull(third.Refusal);
        Assert.Same(second.Item, third.Item);
        Assert.Equal(second.Prng, third.Prng);
        Assert.Equal(0, third.Item.Charges);
    }

    [Fact]
    public void The_third_reroll_through_progression_is_refused_and_changes_nothing()
    {
        var p = Progression.Start(99).Take(Fresh());
        var (p1, r1) = p.Reroll("weapon", WeaponPool);
        var (p2, r2) = p1.Reroll("weapon", WeaponPool);
        var (p3, r3) = p2.Reroll("weapon", WeaponPool);
        Assert.True(r1!.Ok);
        Assert.True(r2!.Ok);
        Assert.False(r3!.Ok);
        Assert.Same(p2, p3);
        Assert.Equal(1, p1.Equipped["weapon"].Charges);
        Assert.Equal(0, p2.Equipped["weapon"].Charges);
    }

    [Fact]
    public void Same_seed_gives_the_same_reroll_sequence_twice()
    {
        static IReadOnlyList<string> Sequence(long seed)
        {
            var ids = new List<string>();
            var p = Progression.Start(seed);
            for (var run = 0; run < 6; run++)
            {
                p = p.Take(Fresh(run + 1) with { Id = $"reward-weapon-{run}" });
                for (var i = 0; i < 3; i++)
                {
                    var (next, r) = p.Reroll("weapon", WeaponPool);
                    p = next;
                    ids.Add(r!.Ok ? r.Item.Facet.Id : "refused");
                }
            }
            return ids;
        }

        Assert.Equal(Sequence(42), Sequence(42));
        Assert.Equal(12, Sequence(42).Count(id => id != "refused"));
        // A different seed gives a different sequence somewhere (the seed is actually used).
        Assert.Contains(Enumerable.Range(1, 8), s => !Sequence(s).SequenceEqual(Sequence(42)));
    }

    [Fact]
    public void Reroll_refuses_an_empty_pool_an_empty_slot_and_keeps_the_charge()
    {
        var r = Crafting.Reroll(Fresh(), Array.Empty<Facet>(), Prng.SeedState(1L));
        Assert.False(r.Ok);
        Assert.Equal(Rewards.StartingCharges, r.Item.Charges);

        var p = Progression.Start(1);
        var (same, refused) = p.Reroll("ring", WeaponPool);
        Assert.Null(refused);
        Assert.Same(p, same);
    }

    [Fact]
    public void Taking_a_reward_equips_it_capped_at_tier_ten()
    {
        var p = Progression.Start(1).Take(Fresh(7));
        Assert.Equal(7, p.Equipped["weapon"].Tier);
        var capped = p.Take(Fresh(12) with { Id = "over" });
        Assert.Equal(Progression.MaxTier, capped.Equipped["weapon"].Tier);
        Assert.Equal("over", capped.Equipped["weapon"].Id);
        Assert.Throws<ArgumentException>(() => p.Take(Fresh() with { Slot = "boots" }));
    }

    [Fact]
    public void The_since_last_run_line_names_ash_and_the_tier_change()
    {
        var p = Progression.Start(1).Take(Fresh(1)).BankRun(RunResult.Depleted).Take(Fresh(3) with { Id = "next" });
        Assert.Equal(Ash.Depleted, p.LastRun!.AshGained);
        Assert.Equal(1, p.LastRun.PreviousTier);
        Assert.Equal("next", p.LastRun.Taken!.Id);
        Assert.Equal("+30 Ash, weapon tier 3 (was 1)", p.LastRun.Text());

        var first = Progression.Start(1).BankRun(RunResult.Timed).Take(Fresh(2));
        Assert.Equal("+50 Ash, weapon tier 2 (new)", first.LastRun!.Text());
        Assert.Equal("+30 Ash", Progression.Start(1).BankRun(RunResult.Depleted).LastRun!.Text());
        Assert.Equal(EndOfRun.NothingLine, Progression.Start(1).BankRun(RunResult.Abandoned).LastRun!.Text());
    }
}
