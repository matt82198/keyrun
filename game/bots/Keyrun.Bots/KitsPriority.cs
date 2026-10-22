using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;

namespace Keyrun.Bots;

/// <summary>
/// Port of bots/src/kit-priority.ts: the tag-based tiering behind a class's base rotation order.
/// </summary>
public static class KitPriority
{
    /// <summary>kit-priority.ts SETUP_TAG_PREFIX: a Setup ability is tagged "setup:&lt;windowId&gt;".</summary>
    public const string SetupTagPrefix = "setup:";

    /// <summary>kit-priority.ts PAYOFF_TAG.</summary>
    public const string PayoffTag = "payoff";

    /// <summary>kit-priority.ts abilityTags: the ability's tags (empty when none).</summary>
    /// <param name="def">Ability.</param>
    /// <returns>Tags.</returns>
    public static IReadOnlyList<string> AbilityTags(AbilityDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        return (IReadOnlyList<string>?)def.Tags ?? Array.Empty<string>();
    }

    /// <summary>True when <paramref name="def"/> carries <paramref name="tag"/> exactly.</summary>
    internal static bool HasTag(AbilityDef def, string tag) => AbilityTags(def).Any(t => string.Equals(t, tag, StringComparison.Ordinal));

    /// <summary>The first tag starting with <paramref name="prefix"/>, or null.</summary>
    internal static string? TagWithPrefix(AbilityDef def, string prefix) =>
        AbilityTags(def).FirstOrDefault(t => t.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// R-AOE-1 (always be casting, "spamming means a CD is required"): the filler is the rotational with no cooldown and
    /// no cost. It is always ready, so it goes LAST in the priority list and fills every global no other button can.
    /// </summary>
    internal static bool IsFiller(AbilityDef def, string resource) =>
        def.CooldownTicks == 0 && KitResources.CostAmount(def, resource) == 0 && !Keyrun.Bots.Shared.AoeGate.IsShapedAoeDamage(def);

    /// <summary>kit-priority.ts tier: cost-paying finishers 0, Payoffs 1, Setups 2, everything else 3, the filler 4 (R-AOE-1).</summary>
    private static int Tier(AbilityDef def, string resource)
    {
        if (IsFiller(def, resource))
        {
            return 4;
        }
        if (KitResources.CostAmount(def, resource) > 0)
        {
            return 0;
        }
        if (HasTag(def, PayoffTag))
        {
            return 1;
        }
        return TagWithPrefix(def, SetupTagPrefix) is not null ? 2 : 3;
    }

    /// <summary>
    /// kit-priority.ts buildRotationPriority: tier first, then longer cooldown first, then ability id (UTF-16
    /// ordinal, as JS <c>&lt;</c> compares strings).
    /// </summary>
    /// <param name="rotationalDefs">The five rotational abilities.</param>
    /// <param name="resource">The class's loop resource.</param>
    /// <returns>Ability ids in priority order.</returns>
    public static List<string> BuildRotationPriority(IReadOnlyList<AbilityDef> rotationalDefs, string resource)
    {
        ArgumentNullException.ThrowIfNull(rotationalDefs);
        return rotationalDefs
            .OrderBy(d => Tier(d, resource))
            .ThenByDescending(d => d.CooldownTicks)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .Select(d => d.Id)
            .ToList();
    }
}
