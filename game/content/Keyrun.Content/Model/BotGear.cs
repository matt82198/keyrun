using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// A bot seat's gear (BIBLE s6 parity: a bot wears gear the way the human does). Each bot class gets a deterministic
/// default loadout from content data, one item per gear slot, and its Facets resolve through the one gear layer,
/// <see cref="GearFacets.Resolve"/>. There is no second resolver.
/// </summary>
public static class BotGear
{
    /// <summary>The bot's default loadout for <paramref name="cls"/>: (slot id, facet id) in canonical slot order.</summary>
    /// <param name="registry">The content.</param>
    /// <param name="cls">The bot's class.</param>
    /// <returns>The loadout.</returns>
    public static IReadOnlyList<(string Slot, string FacetId)> DefaultLoadout(ContentRegistry registry, ClassDef cls)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(cls);
        return Array.Empty<(string, string)>();
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
