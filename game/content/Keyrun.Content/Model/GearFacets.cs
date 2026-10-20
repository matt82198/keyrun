using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>One equipped Facet the gear layer applied (BIBLE s7).</summary>
/// <param name="Slot">The gear slot the item is in.</param>
/// <param name="FacetId">The content facet id.</param>
/// <param name="AbilityId">The ability it patched.</param>
public sealed record AppliedFacet(GearSlot Slot, string FacetId, string AbilityId);

/// <summary>One equipped Facet the gear layer could not apply, and why (nothing it names is patched).</summary>
/// <param name="Slot">The gear slot string the item claimed.</param>
/// <param name="FacetId">The facet id the item carried.</param>
/// <param name="Reason">Why it was not applied.</param>
public sealed record SkippedFacet(string Slot, string FacetId, string Reason);

/// <summary>The gear layer's result: the patched abilities and what was applied or skipped.</summary>
/// <param name="Abilities">Ability id to its Facet-patched definition, only for the abilities a Facet patched.</param>
/// <param name="Applied">The Facets applied, in canonical slot order.</param>
/// <param name="Skipped">The equipped Facets that were not applied (input problems first, then in slot order).</param>
public sealed record FacetResolution(ValueMap<AbilityDef> Abilities, IReadOnlyList<AppliedFacet> Applied, IReadOnlyList<SkippedFacet> Skipped);

/// <summary>
/// The gear layer of the TS <c>content/apply.ts applyLoadout</c> (BIBLE s7 "Facets retag or reshape an ability"): each
/// equipped item's Facet patch is applied to its ability after the talent layer (<see cref="TalentPicks.Resolve"/>),
/// in canonical slot order (weapon, armor, charm, ring, keystone), so two Facets on one ability merge deterministically
/// whatever order the caller lists the gear in. Pure: never mutates its inputs.
/// </summary>
/// <remarks>
/// <para>Merge (content/apply-patch.ts applyPatch): every direct field the patch sets replaces the base field whole;
/// <c>tagAdd</c> adds tags not already present, <c>tagRemove</c> removes tags, <c>effectsAppend</c> appends ops after
/// the base effects. The direct fields go through <see cref="TalentPicks.ApplyPatch"/>, so an appended op reaches every
/// verse variant too (a verse ability plays its variant, not its Effects; without this a Chanter Facet that appends to
/// a verse ability would change nothing the sim plays). No shipped Facet sets both a direct <c>tags</c>/<c>effects</c>
/// and the matching merge key, so the TS key-order dependence never arises. An appended Window op that omits its
/// setupClass is given the wearer's class (<see cref="WindowOp.SetupClass"/>: "the patch layer fills it").</para>
/// <para>Unlike the TS (which throws), an equipped Facet the content cannot honour is SKIPPED and reported: an unknown
/// facet id (an old save's placeholder Facet), a slot mismatch, an ability outside the class kit, or a patched ability
/// the caller's <c>refuse</c> check rejects (the host passes what its sim conversion cannot play). A host must never
/// refuse to start a run over a saved item; the skip is reported so the screen can say so.</para>
/// </remarks>
public static class GearFacets
{
    /// <summary>Canonical slot order (content/schema/facet.ts GEAR_SLOT_IDS).</summary>
    public static readonly IReadOnlyList<GearSlot> SlotOrder = new[] { GearSlot.Weapon, GearSlot.Armor, GearSlot.Charm, GearSlot.Ring, GearSlot.Keystone };

    /// <summary>The slot's content/save string ("weapon", "armor", ...).</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>Its lower-case id.</returns>
    public static string SlotId(GearSlot slot) => slot.ToString().ToLowerInvariant();

    /// <summary>The class kit: its five rotational and four fixed ability ids.</summary>
    /// <param name="cls">The class.</param>
    /// <returns>The ability ids.</returns>
    public static IReadOnlySet<string> Kit(ClassDef cls)
    {
        ArgumentNullException.ThrowIfNull(cls);
        var a = cls.Abilities;
        return new HashSet<string>(new[] { a.Rot1, a.Rot2, a.Rot3, a.Rot4, a.Rot5, a.Interrupt, a.Defensive, a.Utility, a.Movement }, StringComparer.Ordinal);
    }

    /// <summary>
    /// content/apply-patch.ts applyPatch for a Facet patch: the direct fields replace (via
    /// <see cref="TalentPicks.ApplyPatch"/>), then <c>tagAdd</c>/<c>tagRemove</c> retag and <c>effectsAppend</c> appends.
    /// </summary>
    /// <param name="ability">The (possibly talented) ability.</param>
    /// <param name="patch">The Facet's patch.</param>
    /// <param name="setupClass">The wearer's class id, given to an appended Window op that omits its setupClass.</param>
    /// <returns>The patched ability; the id never changes.</returns>
    public static AbilityDef ApplyPatch(AbilityDef ability, FacetPatch patch, string setupClass)
    {
        ArgumentNullException.ThrowIfNull(ability);
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(setupClass);
        var tags = patch.Tags ?? ability.Tags;
        if (patch.TagAdd is { } add)
        {
            tags = new ValueList<string>(tags.Concat(add).Distinct(StringComparer.Ordinal));
        }
        if (patch.TagRemove is { } remove)
        {
            var drop = new HashSet<string>(remove, StringComparer.Ordinal);
            tags = new ValueList<string>(tags.Where(t => !drop.Contains(t)));
        }
        var effects = patch.Effects;
        if (patch.EffectsAppend is { Count: > 0 } append)
        {
            var filled = append.Select(op => op is WindowOp { SetupClass: null } w ? w with { SetupClass = setupClass } : op);
            effects = new ValueList<EffectOp>((effects ?? ability.Effects).Concat(filled));
        }
        var direct = new AbilityPatch
        {
            Name = patch.Name,
            CastTicks = patch.CastTicks,
            GcdTicks = patch.GcdTicks,
            CooldownTicks = patch.CooldownTicks,
            Charges = patch.Charges,
            Cost = patch.Cost,
            Range = patch.Range,
            Shape = patch.Shape,
            Interruptible = patch.Interruptible,
            Tags = tags,
            Effects = effects,
            Variants = patch.Variants,
        };
        return TalentPicks.ApplyPatch(ability, direct);
    }

    /// <summary>
    /// Applies every equipped item's Facet to <paramref name="abilities"/> in canonical slot order and returns the
    /// abilities the Facets patched.
    /// </summary>
    /// <param name="facets">The content Facets (<see cref="ContentRegistry.Facets"/>).</param>
    /// <param name="abilities">The abilities to patch (the content abilities with the talent layer already merged).</param>
    /// <param name="cls">The wearer's class: a Facet on an ability outside its <see cref="Kit"/> is skipped.</param>
    /// <param name="equipped">Equipped items as (slot string, facet id), at most one per slot (a later duplicate slot is skipped).</param>
    /// <param name="refuse">Optional check on each patched ability: a non-null reason skips that Facet (the ability keeps its value before it).</param>
    /// <returns>The patched abilities and what was applied or skipped.</returns>
    public static FacetResolution Resolve(
        IReadOnlyDictionary<string, FacetDef> facets,
        IReadOnlyDictionary<string, AbilityDef> abilities,
        ClassDef cls,
        IEnumerable<(string Slot, string FacetId)> equipped,
        Func<AbilityDef, string?>? refuse = null)
    {
        ArgumentNullException.ThrowIfNull(facets);
        ArgumentNullException.ThrowIfNull(abilities);
        ArgumentNullException.ThrowIfNull(cls);
        ArgumentNullException.ThrowIfNull(equipped);
        var kit = Kit(cls);
        var bySlot = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = new List<SkippedFacet>();
        foreach (var (slot, facetId) in equipped)
        {
            if (!SlotOrder.Any(s => SlotId(s) == slot))
                skipped.Add(new SkippedFacet(slot, facetId, "unknown gear slot"));
            else if (!bySlot.TryAdd(slot, facetId))
                skipped.Add(new SkippedFacet(slot, facetId, "more than one item in the slot"));
        }
        var working = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        var order = new List<string>();
        var applied = new List<AppliedFacet>();
        foreach (var slot in SlotOrder)
        {
            if (!bySlot.TryGetValue(SlotId(slot), out var facetId)) continue;
            string? reason;
            AbilityDef? patched = null;
            if (!facets.TryGetValue(facetId, out var def)) reason = "unknown facet id";
            else if (def.Slot != slot) reason = "facet belongs to slot " + SlotId(def.Slot);
            else if (!kit.Contains(def.AbilityId)) reason = "ability " + def.AbilityId + " is not in the class kit";
            else if (!working.TryGetValue(def.AbilityId, out var current) && !abilities.TryGetValue(def.AbilityId, out current))
                reason = "ability " + def.AbilityId + " is not in the content";
            else
            {
                patched = ApplyPatch(current, def.Patch, cls.Id);
                reason = refuse?.Invoke(patched);
            }
            if (reason is not null || patched is null || def is null)
            {
                skipped.Add(new SkippedFacet(SlotId(slot), facetId, reason ?? "not applied"));
                continue;
            }
            if (!working.ContainsKey(def.AbilityId)) order.Add(def.AbilityId);
            working[def.AbilityId] = patched;
            applied.Add(new AppliedFacet(slot, def.Id, def.AbilityId));
        }
        var result = new ValueMap<AbilityDef>(order.Select(id => new KeyValuePair<string, AbilityDef>(id, working[id])));
        return new FacetResolution(result, applied, skipped);
    }
}
