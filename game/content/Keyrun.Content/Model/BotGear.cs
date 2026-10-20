using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// A bot seat's gear (BIBLE s6 parity: a bot wears gear the way the human does). Each bot class gets a deterministic
/// default loadout from content data, one item per gear slot, and its Facets resolve through the one gear layer,
/// <see cref="GearFacets.Resolve"/>. There is no second resolver.
/// </summary>
public static class BotGear
{
    /// <summary>
    /// The bot's default loadout for <paramref name="cls"/>: (slot id, facet id) in canonical slot order
    /// (<see cref="GearFacets.SlotOrder"/>). Each slot holds the slot's first Facet, in content (registry) order, whose
    /// ability is in the class kit: a tier-1 starter item (tier changes no stat). A slot with no such Facet is left
    /// empty. Pure content data, no PRNG draw, so it is the same for every seed and every run.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="cls">The bot's class.</param>
    /// <returns>The loadout.</returns>
    public static IReadOnlyList<(string Slot, string FacetId)> DefaultLoadout(ContentRegistry registry, ClassDef cls)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(cls);
        var kit = GearFacets.Kit(cls);
        var loadout = new List<(string Slot, string FacetId)>();
        foreach (var slot in GearFacets.SlotOrder)
        {
            var facet = registry.Facets.Values.FirstOrDefault(f => f.Slot == slot && kit.Contains(f.AbilityId));
            if (facet is not null)
            {
                loadout.Add((GearFacets.SlotId(slot), facet.Id));
            }
        }
        return loadout;
    }

    /// <summary>
    /// The bot seat's Facet-patched abilities: <see cref="DefaultLoadout"/> through <see cref="GearFacets.Resolve"/> over
    /// the content abilities (bots pick no talents).
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="cls">The bot's class.</param>
    /// <param name="refuse">The host's check on each patched ability (as the human's gear layer passes it).</param>
    /// <returns>The resolution.</returns>
    public static FacetResolution Resolve(ContentRegistry registry, ClassDef cls, Func<AbilityDef, string?>? refuse = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(cls);
        return GearFacets.Resolve(registry.Facets, registry.Abilities, cls, DefaultLoadout(registry, cls), refuse);
    }
}
