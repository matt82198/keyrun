using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;

namespace Keyrun.Sim.Runs;

// G2.2 design/ABILITY-TREES.md s3, s5.2 (ruling R-TREES-1): the sim never reads Keyrun.Content. The host builds the sim
// patches; this composes them in the PIVOT s1.2 order.

/// <summary>A composition the sim refuses (for example a Facet equal to a tree node).</summary>
public sealed class TreeCompositionException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was refused.</param>
    public TreeCompositionException(string message)
        : base(message)
    {
    }
}

/// <summary>An ability after its tree and Facets are applied.</summary>
/// <param name="Ability">The plain ability the run resolves.</param>
public sealed record ComposedAbility(AbilityDef Ability);

/// <summary>Composes base kit, fork, node patches and Facet patches into one ability, in that order.</summary>
public static class TreeComposer
{
    /// <summary>Applies the fork, then each node in order, then each Facet in order.</summary>
    /// <param name="baseAbility">The base kit ability.</param>
    /// <param name="fork">The chosen fork's patch.</param>
    /// <param name="nodes">The unlocked node patches, in tree order.</param>
    /// <param name="facets">The gear Facet patches.</param>
    /// <returns>The composed ability.</returns>
    /// <exception cref="TreeCompositionException">A Facet equals a tree node.</exception>
    public static ComposedAbility Compose(
        AbilityDef baseAbility,
        GearPatch fork,
        IReadOnlyList<GearPatch> nodes,
        IReadOnlyList<GearPatch> facets)
    {
        ArgumentNullException.ThrowIfNull(baseAbility);
        ArgumentNullException.ThrowIfNull(fork);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(facets);
        foreach (GearPatch facet in facets)
        {
            foreach (GearPatch node in nodes)
            {
                if (SamePatch(facet, node))
                {
                    throw new TreeCompositionException("a Facet duplicates a tree node on ability \"" + baseAbility.Id + "\"");
                }
            }
        }
        AbilityDef ability = fork.ApplyTo(baseAbility);
        foreach (GearPatch node in nodes)
        {
            ability = node.ApplyTo(ability);
        }
        foreach (GearPatch facet in facets)
        {
            ability = facet.ApplyTo(ability);
        }
        return new ComposedAbility(ability);
    }

    // R-TREES-3 (e): two patches are the same when every field is equal and every list member matches by sequence, never by
    // reference (a GearPatch record compares its IReadOnlyList members by reference, so two content-equal patches built from
    // distinct lists would not match under record equality).
    private static bool SamePatch(GearPatch a, GearPatch b) =>
        a.CastTicks == b.CastTicks
        && a.GcdTicks == b.GcdTicks
        && a.CooldownTicks == b.CooldownTicks
        && a.Charges == b.Charges
        && Equals(a.Cost, b.Cost)
        && a.Range == b.Range
        && Equals(a.Shape, b.Shape)
        && a.Interruptible == b.Interruptible
        && SameSequence(a.Tags, b.Tags)
        && SameSequence(a.Effects, b.Effects)
        && SameVariants(a.Variants, b.Variants)
        && SameSequence(a.TagAdd, b.TagAdd)
        && SameSequence(a.TagRemove, b.TagRemove)
        && SameSequence(a.EffectsAppend, b.EffectsAppend);

    private static bool SameSequence<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        return a.SequenceEqual(b);
    }

    private static bool SameVariants(
        IReadOnlyDictionary<string, IReadOnlyList<EffectOp>>? a,
        IReadOnlyDictionary<string, IReadOnlyList<EffectOp>>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        if (a.Count != b.Count)
        {
            return false;
        }
        foreach (var (key, ops) in a)
        {
            if (!b.TryGetValue(key, out var other) || !SameSequence(ops, other))
            {
                return false;
            }
        }
        return true;
    }
}
