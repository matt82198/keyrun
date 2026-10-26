using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// Cross-reference and shape invariants, ported from content/validate.ts. Runs inside
/// <see cref="ContentLoader.Load(IContentSource)"/>; the first violation throws
/// <see cref="ContentLoadException"/>. Each invariant is one <see cref="IContentRule"/> file under Loading/Rules/,
/// run in the order of <see cref="ContentRules.All"/>.
/// <para>
/// Ported: effect refs from ability effects/variants and combo effects and break-bar payloads;
/// class slot and talent-fork ability refs; combo setup/payoff class refs and setup != payoff;
/// enemy ability and script refs; dungeon enemy refs; facet ability refs. The 5-rotational-slot
/// shape and the "patch may not carry id" rule are structural here (typed slots; no <c>id</c>
/// member on <see cref="AbilityPatch"/>/<see cref="FacetPatch"/>, so the load fails first).
/// </para>
/// <para>
/// Added beyond validate.ts: effect refs inside talent and facet patches, <c>breakBarId</c>
/// must name a declared break bar, a script telegraph with <c>spansCast</c> must cast an ability with castTicks &gt; 0,
/// a script telegraph with <c>anchor</c> "target" must be a circle, and a Window op on an ability or talent must carry its
/// <c>setupClass</c> (the sim union requires it; only facet <c>effectsAppend</c> may omit it).
/// </para>
/// <para>
/// Not ported: sim/combat/abilities.ts <c>validateAbilityDef</c> (the sim's own ability checks);
/// it belongs with the sim port (lanes 1C/2A) and should be called from here once it exists.
/// </para>
/// </summary>
public static class ContentValidator
{
    /// <summary>Enemy-side pseudo-class used as a combo setup row (sim/combat/combos.ts).</summary>
    public const string BossPseudoClass = "boss";

    /// <summary>Combo payoffClass meaning "any class other than setupClass".</summary>
    public const string AnyOtherClass = "*";

    /// <summary>Validates <paramref name="reg"/>.</summary>
    /// <param name="reg">A loaded registry.</param>
    /// <exception cref="ContentLoadException">The first violated invariant.</exception>
    public static void Validate(ContentRegistry reg)
    {
        System.ArgumentNullException.ThrowIfNull(reg);
        EffectValidator.Check(reg);
        foreach (IContentRule rule in ContentRules.All)
            rule.Check(reg);
    }

    internal static void Fail(string message) => throw new ContentLoadException("content: " + message);

    internal static void CheckOps(IEnumerable<EffectOp>? ops, ContentRegistry reg, string where, bool requireSetupClass, bool riderOps = false)
    {
        if (ops is null) return;
        foreach (EffectOp op in ops)
        {
            if (!riderOps && op is PartyEffectOp or CloudOp or CloserBurnOp)
                Fail(where + " uses the C#-only combo op \"" + (op switch { CloudOp => "cloud", CloserBurnOp => "closerBurn", _ => "partyEffect" })
                    + "\", allowed only in " + ContentLoader.ComboRidersFile);
            if (op is CloserBurnOp burn && !(burn.MaxHealthBp > 0 && burn.MaxHealthBp <= burn.CapBp && burn.CapBp < 10000))
                Fail(where + " closerBurn needs 0 < maxHealthBp <= capBp < 10000 (the burn alone must never kill a closer at full health), got maxHealthBp "
                    + burn.MaxHealthBp + " and capBp " + burn.CapBp);
            if (op is PartyEffectOp party && party.DurationTicks <= 0)
                Fail(where + " partyEffect durationTicks must be positive, got " + party.DurationTicks);
            if (op is CloudOp cloud && cloud.DurationTicks <= 0)
                Fail(where + " cloud durationTicks must be positive, got " + cloud.DurationTicks);
            if (op is CloudOp sized && sized.RadiusMm <= 0)
                Fail(where + " cloud radiusMm must be positive, got " + sized.RadiusMm);
            string? effectId = op switch
            {
                ApplyEffectOp apply => apply.EffectId,
                RemoveEffectOp remove => remove.EffectId,
                PartyEffectOp party2 => party2.EffectId,
                CloudOp cloud2 => cloud2.EffectId,
                _ => null,
            };
            if (effectId is not null && effectId != "*" && !reg.Effects.ContainsKey(effectId))
                Fail(where + " references unknown effect \"" + effectId + "\"");
            if (requireSetupClass && op is WindowOp window && window.SetupClass is null)
                Fail(where + " window \"" + window.WindowId + "\" has no setupClass");
        }
    }
}
