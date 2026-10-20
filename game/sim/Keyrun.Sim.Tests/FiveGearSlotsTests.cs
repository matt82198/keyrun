using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE s7 "Five slots: Weapon, Armor, Charm, Ring, Keystone" and "Tier is power": the character holds one item per
/// slot, the stats of all five sum into the integer damage pipeline (attacker damageDoneBp, target damageTakenBp), a run
/// created with the summed gear applies it to the wearer, and the end-of-run choice of three draws from every slot.
/// </summary>
public sealed class FiveGearSlotsTests
{
    private static Item ItemIn(string slot, int tier) =>
        new($"reward-{slot}-f-t{tier}-0", slot, tier, new Facet(slot + "-facet", "text", "ability-1"), Rewards.StartingCharges);

    // A stand-in stat table: damageDoneBp 100 per tier for weapon, charm and ring; damageTakenBp -100 per tier for
    // armor and keystone (the content table is Keyrun.Content's; the sim takes it as a function).
    private static GearStats Table(string slot, int tier) => slot switch
    {
        "weapon" or "charm" or "ring" => new GearStats(100 * tier, 0),
        "armor" or "keystone" => new GearStats(0, -100 * tier),
        _ => throw new ArgumentException(slot),
    };

    private static Progression AllFive() => Rewards.GearSlots
        .Select((slot, i) => ItemIn(slot, i + 1))
        .Aggregate(Progression.Start(1).BankRun(RunResult.Timed), (p, item) => p.Take(item));

    [Fact]
    public void Equipping_one_item_in_each_of_the_five_slots_holds_all_five()
    {
        var p = AllFive();
        Assert.Equal(5, p.Equipped.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Rewards.GearSlots.Select(s => p.Equipped[s].Tier));

        // A second weapon replaces the first; the other four stay.
        var replaced = p.Take(ItemIn("weapon", 7));
        Assert.Equal(5, replaced.Equipped.Count);
        Assert.Equal(7, replaced.Equipped["weapon"].Tier);
        Assert.Equal(2, replaced.Equipped["armor"].Tier);
    }

    [Fact]
    public void The_stats_of_all_five_slots_sum()
    {
        // weapon t1 + charm t3 + ring t4 = 800 done; armor t2 + keystone t5 = -700 taken.
        var total = GearPower.Sum(AllFive().Equipped, Table);
        Assert.Equal(new GearStats(800, -700), total);
    }

    [Fact]
    public void An_empty_slot_contributes_nothing()
    {
        var p = Progression.Start(1).BankRun(RunResult.Timed).Take(ItemIn("ring", 2));
        Assert.Equal(new GearStats(200, 0), GearPower.Sum(p.Equipped, Table));
        Assert.Equal(default, GearPower.Sum(Progression.Start(1).Equipped, Table));
    }

    [Fact]
    public void Gear_damage_done_raises_the_attackers_hit_and_gear_damage_taken_lowers_the_targets()
    {
        var src = Actor(1, ActorKind.Player, maxHealth: 10000);
        var dst = Actor(2, ActorKind.Enemy, maxHealth: 10000);
        GearPower.Apply(src, new GearStats(2000, 0));
        var hit = Mitigation.DealDamage(0, src, dst, 1000, NoDefs, _ => { }, 1);
        Assert.Equal(1200, hit.ModifiedAmount);

        var tank = Actor(3, ActorKind.Player, maxHealth: 10000);
        GearPower.Apply(tank, new GearStats(0, -2500));
        var taken = Mitigation.DealDamage(0, dst, tank, 1000, NoDefs, _ => { }, 2);
        Assert.Equal(750, taken.ModifiedAmount);
    }

    [Fact]
    public void A_run_created_with_gear_applies_it_to_the_wearer_only()
    {
        var party = new[]
        {
            RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 1000, 100),
            RunTestKit.Member(2, "cinder", ActorRole.Healer, 500, 0, 1000, 100),
        };
        var dungeon = RunTestKit.OnePull("d", "p0", RunTestKit.Spawn(10, "e", 2000, 0, 5000));
        var options = new RunOptions { Gear = new Dictionary<int, GearStats> { [1] = new GearStats(800, -700) } };
        var run = RunRoot.CreateRun(7, party, dungeon, RunTestKit.Content(), options);
        Assert.Equal(800, run.State.Actors[1].GearDamageDoneBp);
        Assert.Equal(-700, run.State.Actors[1].GearDamageTakenBp);
        Assert.Equal(0, run.State.Actors[2].GearDamageDoneBp);
        Assert.Equal(0, run.State.Actors[10].GearDamageTakenBp);
    }

    [Fact]
    public void Gear_for_an_actor_not_in_the_party_is_refused()
    {
        var party = new[] { RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 1000, 100) };
        var dungeon = RunTestKit.OnePull("d", "p0", RunTestKit.Spawn(10, "e", 2000, 0, 5000));
        var options = new RunOptions { Gear = new Dictionary<int, GearStats> { [9] = new GearStats(100, 0) } };
        Assert.Throws<ArgumentException>(() => RunRoot.CreateRun(7, party, dungeon, RunTestKit.Content(), options));
    }

    [Fact]
    public void A_queued_request_carries_the_gear_into_its_run_options()
    {
        var request = new QueueRequest("d", 2, "c") { Gear = new Dictionary<int, GearStats> { [1] = new GearStats(300, 0) } };
        Assert.Equal(new GearStats(300, 0), request.ToRunOptions().Gear![1]);
    }

    [Fact]
    public void The_choice_of_three_draws_from_several_slots_and_across_seeds_from_all_five()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var multiSlotRolls = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            var three = Rewards.RollRewardChoices(seed, 6, RunResult.Timed, Array.Empty<string>());
            Assert.Equal(3, three.Count);
            seen.UnionWith(three.Select(i => i.Slot));
            if (three.Select(i => i.Slot).Distinct().Count() > 1)
            {
                multiSlotRolls++;
            }
        }
        Assert.Equal(Rewards.GearSlots.OrderBy(s => s, StringComparer.Ordinal), seen.OrderBy(s => s, StringComparer.Ordinal));
        Assert.True(multiSlotRolls > 20, $"only {multiSlotRolls} of 40 rolls offered more than one slot");
    }
}
