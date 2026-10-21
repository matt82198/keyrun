using System;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// BIBLE s7 "Five slots: Weapon, Armor, Charm, Ring, Keystone" and "Tier is power and it is capped at 10": the C#-only
/// gear table (game/content/data/gear.json) gives every slot a tier-1 base item whose stats live in the existing integer
/// damage pipeline (damageDoneBp / damageTakenBp, basis points), growing by a fixed step per tier. The loader rejects an
/// unknown slot, a tier outside 1..10, a slot with no tier-1 base, and two items for one (slot, tier).
/// </summary>
public sealed class GearItemsTests
{
    private const string GearFile = "gear.json";

    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    [Fact]
    public void The_slot_enum_has_exactly_the_five_bible_slots_in_order()
    {
        Assert.Equal(
            new[] { GearSlot.Weapon, GearSlot.Armor, GearSlot.Charm, GearSlot.Ring, GearSlot.Keystone },
            Enum.GetValues<GearSlot>());
        Assert.Equal(new[] { "weapon", "armor", "charm", "ring", "keystone" }, GearFacets.SlotOrder.Select(GearFacets.SlotId));
    }

    [Fact]
    public void Gear_json_is_listed_and_embedded()
    {
        Assert.Contains(GearFile, ContentLoader.Files);
        Assert.Contains(GearFile, EmbeddedContentSource.Instance.Paths);
    }

    [Fact]
    public void Every_slot_has_a_tier_one_base_item_with_a_pipeline_stat()
    {
        foreach (var slot in Enum.GetValues<GearSlot>())
        {
            var bases = Reg.Gear.Values.Where(g => g.Slot == slot && g.Tier == 1).ToList();
            var item = Assert.Single(bases);
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.True(item.Stats.DamageDoneBp != 0 || item.Stats.DamageTakenBp != 0, slot + " base item has no stat");
        }
    }

    [Fact]
    public void Stats_at_tier_one_are_the_base_and_grow_by_the_per_tier_step()
    {
        var weapon = Reg.Gear.Values.Single(g => g.Slot == GearSlot.Weapon && g.Tier == 1);
        var t1 = GearItems.StatsAt(Reg.Gear, GearSlot.Weapon, 1);
        var t5 = GearItems.StatsAt(Reg.Gear, GearSlot.Weapon, 5);
        Assert.Equal(weapon.Stats, t1);
        Assert.Equal(weapon.Stats.DamageDoneBp + (4 * weapon.PerTier.DamageDoneBp), t5.DamageDoneBp);
        Assert.True(t5.DamageDoneBp > t1.DamageDoneBp, "tier is power: tier 5 must out-damage tier 1");

        var armor = GearItems.StatsAt(Reg.Gear, GearSlot.Armor, 10);
        Assert.True(armor.DamageTakenBp < GearItems.StatsAt(Reg.Gear, GearSlot.Armor, 1).DamageTakenBp, "armor reduces damage taken more at tier 10");
    }

    [Fact]
    public void Stats_at_a_tier_outside_one_to_ten_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GearItems.StatsAt(Reg.Gear, GearSlot.Ring, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GearItems.StatsAt(Reg.Gear, GearSlot.Ring, 11));
    }

    [Fact]
    public void An_unknown_slot_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(GearFile, n => n.AsArray()[0]!["slot"] = "belt"));
        Assert.Contains(GearFile, ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void A_tier_outside_one_to_ten_is_rejected(int tier)
    {
        var ex = LoadFails(new MutableSource().Edit(GearFile, n => n.AsArray()[0]!["tier"] = tier));
        Assert.Contains("tier", ex.Message);
    }

    [Fact]
    public void A_slot_without_a_tier_one_base_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(GearFile, n =>
        {
            var arr = n.AsArray();
            var ring = arr.First(x => (string?)x!["slot"] == "ring")!;
            arr.Remove(ring);
        }));
        Assert.Contains("ring", ex.Message);
    }

    [Fact]
    public void Two_items_for_one_slot_and_tier_are_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(GearFile, n =>
        {
            var arr = n.AsArray();
            var copy = arr[0]!.DeepClone();
            copy["id"] = "gear.duplicate";
            arr.Add(copy);
        }));
        Assert.Contains("more than one", ex.Message);
    }

    [Fact]
    public void A_damage_taken_stat_that_would_heal_or_a_negative_damage_done_is_rejected()
    {
        var heal = LoadFails(new MutableSource().Edit(GearFile, n => n.AsArray()[0]!["stats"] = new JsonObject { ["damageTakenBp"] = 100 }));
        Assert.Contains("damageTakenBp", heal.Message);
        var neg = LoadFails(new MutableSource().Edit(GearFile, n => n.AsArray()[0]!["stats"] = new JsonObject { ["damageDoneBp"] = -100 }));
        Assert.Contains("damageDoneBp", neg.Message);
    }
}
