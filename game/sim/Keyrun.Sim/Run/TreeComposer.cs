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

    // ABILITY-TREES s1.2 chain order. A node set composes in this order, never in TreeState.Nodes order (R-TREES-7).
    private static readonly string[] ChainKinds = { "rank2", "enhanced", "choice", "rank3" };

    /// <summary>
    /// Composes one class's unlocked nodes for one ability into the single <see cref="GearPatch"/> the sim applies. Each
    /// chain kind is looked up as "&lt;baseAbility.Id&gt;.&lt;kind&gt;": an owned node with an edit contributes it. A rank
    /// <see cref="TreeNodeEdit.Delta"/> resolves against <paramref name="baseAbility"/> into a replacement
    /// <see cref="GearPatch.Effects"/> list; an enhanced <see cref="TreeNodeEdit.Patch"/>'s
    /// <see cref="GearPatch.EffectsAppend"/> ops are appended in chain order. An empty set yields the identity patch.
    /// </summary>
    /// <param name="state">The class's tree state; only its owned nodes count.</param>
    /// <param name="baseAbility">The base kit ability the nodes patch.</param>
    /// <param name="edits">Node id to edit, for every node the content ships.</param>
    /// <returns>The composed patch.</returns>
    /// <exception cref="TreeCompositionException">A delta the sim does not run, or one with no damage op to change.</exception>
    public static GearPatch ToGearPatch(
        TreeState state,
        AbilityDef baseAbility,
        IReadOnlyDictionary<string, TreeNodeEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(baseAbility);
        ArgumentNullException.ThrowIfNull(edits);
        var owned = new HashSet<string>(state.Nodes, StringComparer.Ordinal);
        List<EffectOp>? effects = null;
        List<EffectOp>? append = null;
        foreach (string kind in ChainKinds)
        {
            string nodeId = baseAbility.Id + "." + kind;
            if (!owned.Contains(nodeId) || !edits.TryGetValue(nodeId, out TreeNodeEdit? edit))
            {
                continue;
            }
            if (edit.Delta is not null)
            {
                effects ??= baseAbility.Effects.ToList();
                ApplyDelta(effects, edit.Delta, nodeId);
            }
            if (edit.Patch?.EffectsAppend is { } ops)
            {
                (append ??= new List<EffectOp>()).AddRange(ops);
            }
        }
        return new GearPatch { Effects = effects, EffectsAppend = append };
    }

    // Applies one flat delta to the first damage op of the running ops list. Anything else is refused (fail closed).
    private static void ApplyDelta(List<EffectOp> ops, EffectDelta delta, string nodeId)
    {
        if (delta.Op != "damage" || delta.Field != "amount")
        {
            throw new TreeCompositionException($"node \"{nodeId}\" has a delta the sim does not run: {delta.Op}.{delta.Field}");
        }
        int index = ops.FindIndex(op => op is DamageOp);
        if (index < 0)
        {
            throw new TreeCompositionException($"node \"{nodeId}\" changes damage, but the ability has no damage op");
        }
        var damage = (DamageOp)ops[index];
        ops[index] = damage with { Amount = damage.Amount + delta.Delta };
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
        && SameSequence(a.Effects, b.Effects, SameOp)
        && SameVariants(a.Variants, b.Variants)
        && SameSequence(a.TagAdd, b.TagAdd)
        && SameSequence(a.TagRemove, b.TagRemove)
        && SameSequence(a.EffectsAppend, b.EffectsAppend, SameOp);

    // R-TREES-3 (b) residual: a ConditionalOp holds its Then body as a list, so record equality would compare that list by
    // reference. Its non-list fields compare by record equality and Then compares element-wise, recursing into nested
    // conditionals. Every other op compares by record equality.
    private static bool SameOp(EffectOp a, EffectOp b)
    {
        if (a is ConditionalOp ca && b is ConditionalOp cb)
        {
            return ca.If.Equals(cb.If) && ca.Consume == cb.Consume && SameSequence(ca.Then, cb.Then, SameOp);
        }
        return a.Equals(b);
    }

    private static bool SameSequence<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        return a.SequenceEqual(b);
    }

    private static bool SameSequence<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b, Func<T, T, bool> same)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }
        if (a.Count != b.Count)
        {
            return false;
        }
        for (int i = 0; i < a.Count; i++)
        {
            if (!same(a[i], b[i]))
            {
                return false;
            }
        }
        return true;
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
            if (!b.TryGetValue(key, out var other) || !SameSequence(ops, other, SameOp))
            {
                return false;
            }
        }
        return true;
    }
}
