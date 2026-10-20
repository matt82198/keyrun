using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;

namespace Keyrun.Bots;

/// <summary>kit-heals.ts HealKit.secondGroupHeal: a second circle heal around the caster (Chanter's Hymn).</summary>
/// <param name="AbilityId">Ability.</param>
/// <param name="RadiusMm">Its radius.</param>
public sealed record SecondGroupHeal(string AbilityId, long RadiusMm);

/// <summary>
/// bots/src/kit-heals.ts HealKit: heals split by reach. selfHeal only heals the caster; groupHeal heals friendlies
/// in a circle around the caster; targetHeal reaches anyone (directly, or through a periodic-heal effect).
/// </summary>
/// <param name="SelfHealAbilityId">Self-shaped heal, or null.</param>
/// <param name="GroupHealAbilityId">First circle heal, or null.</param>
/// <param name="GroupHealRadiusMm">Its radius (0 when none).</param>
/// <param name="TargetHealAbilityId">Target-shaped heal, or null.</param>
public sealed record HealKit(string? SelfHealAbilityId, string? GroupHealAbilityId, long GroupHealRadiusMm, string? TargetHealAbilityId)
{
    /// <summary>A second circle heal, or null (TS: absent).</summary>
    public SecondGroupHeal? SecondGroupHeal { get; init; }

    /// <summary>kit-resolve.ts NO_HEALS.</summary>
    public static HealKit None { get; } = new(null, null, 0, null);
}

/// <summary>
/// Port of bots/src/kit-heals.ts: shape-aware heal classification and the taunt lookup for
/// <see cref="Kits.BuildClassKit"/>. A variant ability counts through its "mend" variant.
/// </summary>
public static class KitHeals
{
    private static IReadOnlyList<EffectOp> CastOps(AbilityDef def) =>
        def.Variants is not null && def.Variants.TryGetValue("mend", out var mend) ? mend : def.Effects;

    private static bool OpHeals(EffectOp op, IReadOnlyDictionary<string, EffectDef>? effects)
    {
        if (op is HealOp)
        {
            return true;
        }
        if (op is ApplyEffectOp apply && effects is not null)
        {
            return effects.TryGetValue(apply.EffectId, out var e) && e.Periodic?.Op is HealOp;
        }
        return false;
    }

    private static bool Heals(AbilityDef def, IReadOnlyDictionary<string, EffectDef>? effects) => CastOps(def).Any(op => OpHeals(op, effects));

    /// <summary>
    /// True when <paramref name="def"/> is a verse swap (every variant moves the verse resource, the rule
    /// Keyrun.Sim Abilities.ResolveVerse uses) whose cast from <paramref name="verse"/> plays no heal. The sim plays a
    /// swap's DESTINATION variant, so Chanter Refrain cast from Mend shields and flips into Ward: classified as the
    /// self heal through its "mend" variant, it is only a heal when cast from Ward. A non-swap ability is never one.
    /// </summary>
    /// <param name="def">The ability.</param>
    /// <param name="verse">The caster's current verse ("mend" or "ward", Keyrun.Sim Actors.Verse).</param>
    /// <param name="effects">Effect registry (for periodic heals), or null.</param>
    /// <returns>Whether casting it now would swap verse without healing.</returns>
    public static bool SwapsVerseWithoutHealing(AbilityDef def, string verse, IReadOnlyDictionary<string, EffectDef>? effects)
    {
        ArgumentNullException.ThrowIfNull(def);
        if (def.Variants is not { Count: > 0 } variants || !variants.Values.All(ops => ops.Any(op => op is ResourceOp)))
        {
            return false;
        }
        var destination = string.Equals(verse, Keyrun.Sim.Combat.Abilities.MendVerse, StringComparison.Ordinal)
            ? Keyrun.Sim.Combat.Abilities.WardVerse
            : Keyrun.Sim.Combat.Abilities.MendVerse;
        IReadOnlyList<EffectOp> played = variants.TryGetValue(destination, out var resolved) ? resolved : def.Effects;
        return !played.Any(op => OpHeals(op, effects));
    }

    /// <summary>
    /// kit-heals.ts classifyHeals: walks the rotational abilities in slot order; the first self heal, the first and
    /// second circle heals, and the first target heal win.
    /// </summary>
    /// <param name="defs">Rotational abilities.</param>
    /// <param name="effects">Effect registry (for periodic heals), or null.</param>
    /// <returns>The heal kit.</returns>
    public static HealKit ClassifyHeals(IReadOnlyList<AbilityDef> defs, IReadOnlyDictionary<string, EffectDef>? effects = null)
    {
        ArgumentNullException.ThrowIfNull(defs);
        var kit = HealKit.None;
        foreach (var def in defs)
        {
            if (!Heals(def, effects))
            {
                continue;
            }
            var kind = def.Shape.Kind;
            if (kind == ShapeKind.Self && kit.SelfHealAbilityId is null)
            {
                kit = kit with { SelfHealAbilityId = def.Id };
            }
            else if (kind == ShapeKind.Circle && kit.GroupHealAbilityId is null)
            {
                kit = kit with { GroupHealAbilityId = def.Id, GroupHealRadiusMm = def.Shape.Radius ?? 0 };
            }
            else if (kind == ShapeKind.Circle && kit.SecondGroupHeal is null)
            {
                kit = kit with { SecondGroupHeal = new SecondGroupHeal(def.Id, def.Shape.Radius ?? 0) };
            }
            else if (kind == ShapeKind.Target && kit.TargetHealAbilityId is null)
            {
                kit = kit with { TargetHealAbilityId = def.Id };
            }
        }
        return kit;
    }

    /// <summary>kit-heals.ts tauntAbilityId: the utility ability when it is target-shaped and carries a threat op.</summary>
    /// <param name="utility">The class's utility ability, or null.</param>
    /// <returns>Its id, or null.</returns>
    public static string? TauntAbilityId(AbilityDef? utility)
    {
        if (utility is null || utility.Shape.Kind != ShapeKind.Target)
        {
            return null;
        }
        return utility.Effects.Any(op => op is ThreatOp) ? utility.Id : null;
    }
}
