using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// BIBLE s7 "Facets are the chase": a reward the player takes at run end must carry a content Facet the gear layer
/// can honour (a known facet id, in the item's slot, patching an ability in the wearer's kit), never a placeholder
/// id the gear layer skips as unknown. Drives the run-end roll over the shipped content registry the way the host
/// builds the pool (RunEndPanel.cs RunEndView.Of over Screen.SlotPool), then equips every card via Progression.Take.
/// </summary>
public sealed class RewardDropFacetsTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static HashSet<string> Kit(ClassDef cls)
    {
        var a = cls.Abilities;
        return new HashSet<string>(new[] { a.Rot1, a.Rot2, a.Rot3, a.Rot4, a.Rot5, a.Interrupt, a.Defensive, a.Utility, a.Movement }, StringComparer.Ordinal);
    }

    // Screen.SlotPool, over the registry: the slot's content facets that patch a kit ability, by id.
    private static IReadOnlyList<Facet> SlotPool(IReadOnlySet<string> kit, string slot) =>
        Reg.Facets.Select(kv => kv.Value)
            .Where(f => f.Slot.ToString().ToLowerInvariant() == slot && kit.Contains(f.AbilityId))
            .OrderBy(f => f.Id, StringComparer.Ordinal)
            .Select(f => new Facet(f.Id, f.Text, f.AbilityId))
            .ToList();

    [Theory]
    [InlineData("warden")]
    [InlineData("cinder")]
    [InlineData("chanter")]
    public void Every_taken_reward_equips_a_content_facet_the_gear_layer_can_honour(string classId)
    {
        var kit = Kit(Reg.Classes[classId]);
        var pools = Rewards.GearSlots.ToDictionary(s => s, s => SlotPool(kit, s), StringComparer.Ordinal);
        // An abandoned run offers no card to take (STATE.md D45; Sim AbandonedRunGetsNothingTests), so only the
        // finished results are equipped here.
        foreach (var result in new[] { RunResult.Timed, RunResult.Depleted })
        {
            for (var seed = 0; seed < 150; seed++)
            {
                foreach (var card in Rewards.RollRewardChoices(seed, 2 + (seed % 19), result, Rewards.GearSlots, pools))
                {
                    var equipped = Progression.Start(seed).BankRun(result).Take(card).Equipped[card.Slot];
                    Assert.True(Reg.Facets.TryGetValue(equipped.Facet.Id, out var def), $"unknown facet id {equipped.Facet.Id}");
                    Assert.Equal(card.Slot, def!.Slot.ToString().ToLowerInvariant());
                    Assert.Equal(def.AbilityId, equipped.Facet.AbilityId);
                    Assert.Contains(def.AbilityId, kit);
                    Assert.True(Reg.Abilities.ContainsKey(def.AbilityId), $"facet {def.Id} patches unknown ability {def.AbilityId}");
                }
            }
        }
    }
}
