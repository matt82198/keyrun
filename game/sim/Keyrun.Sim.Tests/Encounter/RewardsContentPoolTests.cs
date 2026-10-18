using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// The content-pool reward roll (lane 3C-c, NEW behaviour, no TS oracle: the TS root is frozen, game/CLAUDE.md
/// "Oracle rule"). The pool is the shipped content facet table, read straight from content/data/facets/warden.json
/// (4 facets per slot), so the rolled cards name real facets whose ability ids resolve in content/data/abilities.
/// The old placeholder overload and its pinned ids stay covered, unchanged, by <see cref="RewardsTests"/>.
/// </summary>
public sealed class RewardsContentPoolTests
{
    // The placeholder FACET_POOLS ids (rewards.ts:47) and ability ids: none may come out of a content-pool roll.
    private static readonly string[] PlaceholderFacetIds =
    {
        "weapon-edge", "weapon-haft", "weapon-brand", "armor-brace", "armor-anchor", "armor-ward",
        "charm-ember", "charm-flare", "charm-cinder", "ring-verse", "ring-chorus", "ring-coda",
        "keystone-echo", "keystone-lock", "keystone-tempo",
    };

    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "content", "data", "facets")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("could not locate content/data/facets above " + AppContext.BaseDirectory);
    }

    // The class-scoped pool, as the HUD builds it (Screen.cs SlotPool): that class file's facets per slot, by id.
    private static IReadOnlyDictionary<string, IReadOnlyList<Facet>> ContentPool(string classId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.Value, "content", "data", "facets", classId + ".json")));
        var facets = doc.RootElement.EnumerateArray()
            .Select(e => (Slot: e.GetProperty("slot").GetString()!, Facet: new Facet(e.GetProperty("id").GetString()!, e.GetProperty("text").GetString()!, e.GetProperty("abilityId").GetString()!)))
            .ToList();
        return Rewards.GearSlots.ToDictionary(
            s => s,
            s => (IReadOnlyList<Facet>)facets.Where(f => f.Slot == s).Select(f => f.Facet).OrderBy(f => f.Id, StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal);
    }

    private static HashSet<string> ContentAbilityIds(string classId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.Value, "content", "data", "abilities", classId + ".json")));
        return doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void The_fixture_pool_is_the_shipped_table_with_four_facets_per_slot()
    {
        var pool = ContentPool("warden");
        Assert.All(Rewards.GearSlots, s => Assert.Equal(4, pool[s].Count));
        Assert.Contains(pool["weapon"], f => f.Id == "warden.weapon.keenEdge" && f.AbilityId == "warden.strike");
    }

    [Theory]
    [InlineData("warden")]
    [InlineData("cinder")]
    [InlineData("chanter")]
    public void Every_card_comes_from_the_pool_passed_names_its_real_facet_and_resolves_its_ability(string classId)
    {
        var pool = ContentPool(classId);
        var abilities = ContentAbilityIds(classId);
        for (var seed = 0; seed < 200; seed++)
        {
            var items = Rewards.RollRewardChoices(seed, 10, RunResult.Timed, Rewards.GearSlots, pool);
            Assert.Equal(3, items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                Assert.Contains(item.Facet, pool[item.Slot]);
                Assert.DoesNotContain(item.Facet.Id, PlaceholderFacetIds);
                Assert.StartsWith(classId + ".", item.Facet.Id, StringComparison.Ordinal);
                Assert.Contains(item.Facet.AbilityId, abilities);
                Assert.Equal($"reward-{item.Slot}-{item.Facet.Id}-t{item.Tier}-{i}", item.Id);
                Assert.Equal(Rewards.StartingCharges, item.Charges);
            }
        }
    }

    [Fact]
    public void The_draw_spans_all_four_facets_of_every_slot()
    {
        var pool = ContentPool("warden");
        var seen = Enumerable.Range(0, 400)
            .SelectMany(seed => Rewards.RollRewardChoices(seed, 10, RunResult.Timed, Rewards.GearSlots, pool))
            .Select(i => i.Facet.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(Rewards.GearSlots, s => Assert.All(pool[s], f => Assert.Contains(f.Id, seen)));
    }

    [Fact]
    public void Owned_slots_limit_the_slot_draw_as_in_the_placeholder_roll()
    {
        var pool = ContentPool("warden");
        for (var seed = 0; seed < 100; seed++)
        {
            Assert.All(Rewards.RollRewardChoices(seed, 6, RunResult.Depleted, new[] { "ring", "charm" }, pool), i => Assert.Contains(i.Slot, new[] { "ring", "charm" }));
        }
    }

    [Fact]
    public void Same_seed_and_pool_give_the_same_cards()
    {
        var pool = ContentPool("warden");
        for (var seed = 0; seed < 50; seed++)
        {
            Assert.Equal(
                Rewards.RollRewardChoices(seed, 12, RunResult.Timed, Rewards.GearSlots, pool),
                Rewards.RollRewardChoices(seed, 12, RunResult.Timed, Rewards.GearSlots, ContentPool("warden")));
        }
    }

    [Fact]
    public void The_cards_change_with_the_seed()
    {
        var pool = ContentPool("warden");
        var distinct = Enumerable.Range(0, 20)
            .Select(seed => string.Join(" ", Rewards.RollRewardChoices(seed, 12, RunResult.Timed, Rewards.GearSlots, pool).Select(i => i.Id)))
            .Distinct(StringComparer.Ordinal)
            .Count();
        Assert.True(distinct > 10, $"only {distinct} distinct rolls over 20 seeds");
    }

    [Fact]
    public void The_three_cards_never_repeat_slot_facet_and_tier()
    {
        var pool = ContentPool("warden");
        for (var seed = 0; seed < 200; seed++)
        {
            var items = Rewards.RollRewardChoices(seed, 15, RunResult.Timed, Rewards.GearSlots, pool);
            Assert.Equal(3, items.Select(i => $"{i.Slot}:{i.Facet.Id}:{i.Tier}").Distinct().Count());
        }
    }

    [Fact]
    public void A_slot_with_no_pool_entry_or_an_empty_pool_fails_closed()
    {
        var pool = ContentPool("warden");
        var missing = pool.Where(kv => kv.Key != "ring").ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        Assert.Throws<ArgumentException>(() => Rewards.RollRewardChoices(1, 6, RunResult.Timed, new[] { "ring" }, missing));
        var empty = new Dictionary<string, IReadOnlyList<Facet>>(pool, StringComparer.Ordinal) { ["ring"] = Array.Empty<Facet>() };
        Assert.Throws<ArgumentException>(() => Rewards.RollRewardChoices(1, 6, RunResult.Timed, new[] { "ring" }, empty));
    }
}
