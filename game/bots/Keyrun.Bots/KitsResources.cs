using System;
using System.Linq;
using Keyrun.Content;

namespace Keyrun.Bots;

/// <summary>
/// Port of bots/src/kit-resources.ts: loop-kind-agnostic resource economics of one ability (what it costs, what
/// it gives back, whether it heals), used by <see cref="Kits.BuildClassKit"/>.
/// </summary>
public static class KitResources
{
    /// <summary>
    /// kit-resources.ts resourceEffectDelta: the sum of every <c>{op: "resource"}</c> delta in the ability's BASE
    /// effects. Variants (per-stance replacements) are deliberately not summed.
    /// </summary>
    /// <param name="def">Ability.</param>
    /// <returns>Net delta.</returns>
    public static long ResourceEffectDelta(AbilityDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        long delta = 0;
        foreach (var op in def.Effects)
        {
            if (op is ResourceOp r)
            {
                delta += r.Delta;
            }
        }
        return delta;
    }

    /// <summary>kit-resources.ts costAmount: the cost amount when it is paid in <paramref name="resource"/>, else 0.</summary>
    /// <param name="def">Ability.</param>
    /// <param name="resource">The class's loop resource.</param>
    /// <returns>Cost.</returns>
    public static long CostAmount(AbilityDef def, string resource)
    {
        ArgumentNullException.ThrowIfNull(def);
        return def.Cost is not null && string.Equals(def.Cost.Resource, resource, StringComparison.Ordinal) ? def.Cost.Amount : 0;
    }

    /// <summary>kit-resources.ts healsOnCast: a heal op in the base effects or in any variant.</summary>
    /// <param name="def">Ability.</param>
    /// <returns>True when casting it can heal.</returns>
    public static bool HealsOnCast(AbilityDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        if (def.Effects.Any(op => op is HealOp))
        {
            return true;
        }
        return def.Variants is not null && def.Variants.Values.Any(variant => variant.Any(op => op is HealOp));
    }
}
