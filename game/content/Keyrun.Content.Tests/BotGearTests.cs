using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// BIBLE s6 parity: every bot seat wears a default loadout (one item per gear slot) and its Facets resolve through the
/// same <see cref="GearFacets.Resolve"/> the human's gear goes through. Run against the shipped content, for every class
/// (any class can sit in a bot seat).
/// </summary>
public sealed class BotGearTests
{
    // The host's refuse check (game/src/Hud/Screen.cs RunLoadout, game/src/Host/HostRunSetup.cs bot loadouts).
    private static string? HostRefuse(AbilityDef a) => a.Shape.Width is not null ? "line width is not in the sim shape" : null;

    public static IEnumerable<object[]> Classes() =>
        ContentLoader.Load().Classes.Keys.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Classes))]
    public void Every_bot_seat_resolves_one_facet_per_slot_and_its_kit_carries_them(string classId)
    {
        var reg = ContentLoader.Load();
        var cls = reg.Classes[classId];

        var loadout = BotGear.DefaultLoadout(reg, cls);
        var r = BotGear.Resolve(reg, cls, HostRefuse);

        // One item in every slot, in canonical slot order, each a Facet of this class's kit.
        Assert.Equal(GearFacets.SlotOrder.Select(GearFacets.SlotId), loadout.Select(g => g.Slot));
        var kit = GearFacets.Kit(cls);
        Assert.All(loadout, g =>
        {
            var def = reg.Facets[g.FacetId];
            Assert.Equal(g.Slot, GearFacets.SlotId(def.Slot));
            Assert.Contains(def.AbilityId, kit);
        });

        // Every Facet applied, none skipped.
        Assert.Empty(r.Skipped);
        Assert.Equal(loadout.Select(g => g.FacetId), r.Applied.Select(a => a.FacetId));

        // The resolved kit is each patched ability with its Facets applied in slot order on the content base.
        foreach (var abilityId in r.Applied.Select(a => a.AbilityId).Distinct())
        {
            var expected = loadout
                .Select(g => reg.Facets[g.FacetId])
                .Where(f => f.AbilityId == abilityId)
                .Aggregate(reg.Abilities[abilityId], (a, f) => GearFacets.ApplyPatch(a, f.Patch, cls.Id));
            Assert.Equal(expected, r.Abilities[abilityId]);
            Assert.NotEqual(reg.Abilities[abilityId], r.Abilities[abilityId]);
        }
        Assert.Equal(r.Applied.Select(a => a.AbilityId).Distinct().OrderBy(x => x, StringComparer.Ordinal),
            r.Abilities.Keys.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Classes))]
    public void A_bot_seats_gear_is_deterministic(string classId)
    {
        // Two independent loads (two runs, any seed: the loadout is content data, no PRNG draw) give the same gear.
        var a = ContentLoader.Load();
        var b = ContentLoader.Load();

        var ra = BotGear.Resolve(a, a.Classes[classId], HostRefuse);
        var rb = BotGear.Resolve(b, b.Classes[classId], HostRefuse);

        Assert.NotEmpty(ra.Applied);
        Assert.Equal(BotGear.DefaultLoadout(a, a.Classes[classId]), BotGear.DefaultLoadout(b, b.Classes[classId]));
        Assert.Equal(ra.Applied, rb.Applied);
        Assert.Equal(ra.Skipped, rb.Skipped);
        Assert.Equal(ra.Abilities, rb.Abilities);
    }
}
