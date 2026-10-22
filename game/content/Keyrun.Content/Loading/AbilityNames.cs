using System;
using System.Runtime.CompilerServices;

namespace Keyrun.Content;

/// <summary>
/// The ONE resolver for the ability name a player reads (finding f-b24741e2, Matt 2026-10-22: "cooler names for the
/// players"). A player kit ability reads its ability-text.json display name (<see cref="AbilityTextTable"/>); any other
/// ability (an enemy cast, already themed in its own content) keeps its content <c>name</c>; an id the registry does not
/// know falls back to the id. Cast bars, the party and unit frames, the run log, the pre-run screen and the talent panel
/// all call this (through Keyrun.Host.Names.Ability), so one ability never has two names on screen. The ability id and
/// content name stay as they are for bots, tests and fixtures; only text shown to the player goes through here.
/// </summary>
public static class AbilityNames
{
    // One table per registry, read once (the registry is the one the table is checked against).
    private static readonly ConditionalWeakTable<ContentRegistry, AbilityTextTable> Tables = new();

    /// <summary>The name to show the player for an ability.</summary>
    /// <param name="registry">The loaded content, or null (then the id is returned).</param>
    /// <param name="abilityId">The ability id.</param>
    /// <returns>The display name, else the content name, else the id.</returns>
    public static string Display(ContentRegistry? registry, string abilityId)
    {
        ArgumentNullException.ThrowIfNull(abilityId);
        if (registry is null)
        {
            return abilityId;
        }
        var table = Tables.GetValue(registry, r => AbilityTextLoader.Read(EmbeddedContentSource.Instance, r));
        var contentName = registry.Abilities.TryGetValue(abilityId, out var def) ? def.Name : abilityId;
        return table.NameOr(abilityId, contentName);
    }
}
