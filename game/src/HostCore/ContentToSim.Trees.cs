using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

// G2.2 design/ABILITY-TREES.md s1.2, s5.2 (ruling R-TREES-8, R-TREES-9): the content tree nodes as the sim's node edits. The
// sim never reads Keyrun.Content (R-TREES-1), so this host adapter is the only place a TreeNodeDef becomes a TreeNodeEdit.
public sealed partial record HostRunSetup
{
    /// <summary>
    /// The sim node edit of every content tree node that carries one, by node id (ABILITY-TREES s1.2): a <c>rank</c> node's
    /// single <c>effectDelta</c> becomes an <see cref="EffectDelta"/>; an <c>enhanced</c> node's <c>conditional</c> becomes a
    /// <see cref="GearPatch"/> whose <see cref="GearPatch.EffectsAppend"/> carries the <see cref="ConditionalOp"/> (R-TREES-9).
    /// A <c>choice</c> node carries no edit and is left out. An <c>enhanced</c> node whose conditional the sim cannot run yet
    /// is left out of this shared map, so one unshipped node cannot break every seat; <see cref="RequireOwnedEdits"/> refuses
    /// a seat that owns it, so it is never dropped silently for the seat that holds it. A rank edit of an unknown shape throws.
    /// </summary>
    /// <param name="registry">The loaded content.</param>
    /// <returns>The node edits, ordinal by node id.</returns>
    /// <exception cref="InvalidOperationException">A node's edit is not a shape the sim runs.</exception>
    public static IReadOnlyDictionary<string, TreeNodeEdit> TreeEdits(C.ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var edits = new Dictionary<string, TreeNodeEdit>(StringComparer.Ordinal);
        foreach (var node in registry.Trees.Values.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            if (node.Edit is null)
            {
                continue;
            }
            if (node.Kind == "rank")
            {
                edits[node.Id] = new TreeNodeEdit(Delta: RankDeltaOf(node));
            }
            else if (node.Kind == "enhanced" && RunnableConditionalOf(node) is { } ops)
            {
                edits[node.Id] = new TreeNodeEdit(Patch: new GearPatch { EffectsAppend = ops });
            }
        }
        return edits;
    }

    /// <summary>
    /// Fails closed when a seat owns a tree node the sim cannot apply: every owned <c>enhanced</c> node's conditional must
    /// convert to a sim <see cref="ConditionalOp"/>, so an unknown shape is refused loudly and names the node. Called by
    /// <see cref="StartFor"/> before a run starts.
    /// </summary>
    /// <param name="registry">The loaded content.</param>
    /// <param name="progression">The seat's progression.</param>
    /// <exception cref="InvalidOperationException">The seat owns an enhanced node whose conditional the sim cannot run.</exception>
    public static void RequireOwnedEdits(C.ContentRegistry registry, Progression progression)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(progression);
        foreach (var owned in progression.Trees.Values.SelectMany(t => t.Nodes))
        {
            if (registry.Trees.TryGetValue(owned, out var node) && node.Kind == "enhanced")
            {
                _ = ConditionalOf(node);  // throws, naming the node, when the sim cannot run it
            }
        }
    }

    // The conditional's sim ops, or null when the sim cannot run this node's conditional yet (see TreeEdits and RequireOwnedEdits).
    private static IReadOnlyList<EffectOp>? RunnableConditionalOf(C.TreeNodeDef node)
    {
        try
        {
            return ConditionalOf(node);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // An enhanced node's edit is exactly one conditional (ABILITY-TREES s5.2): "if" a closed condition, "consume", and "then"
    // ops the sim runs. Only the resource op is in the shipped content so far; any other body op is refused.
    private static IReadOnlyList<EffectOp> ConditionalOf(C.TreeNodeDef node)
    {
        using var doc = JsonDocument.Parse(node.Edit ?? throw new InvalidOperationException($"run setup: tree node \"{node.Id}\" has no edit"));
        if (!doc.RootElement.TryGetProperty("conditional", out var cond) || cond.ValueKind != JsonValueKind.Object
            || !cond.TryGetProperty("if", out var when) || when.ValueKind != JsonValueKind.Object
            || !cond.TryGetProperty("then", out var then) || then.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"run setup: tree node \"{node.Id}\" must carry a conditional with if and then");
        }
        var kind = ConditionKindOf(when.GetProperty("kind").GetString(), node.Id);
        var id = when.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
        var threshold = when.TryGetProperty("n", out var nEl) ? nEl.GetInt64() : 0L;
        var consume = cond.TryGetProperty("consume", out var consumeEl) && consumeEl.ValueKind == JsonValueKind.True;
        var body = new List<EffectOp>();
        foreach (var op in then.EnumerateArray())
        {
            var name = op.GetProperty("op").GetString();
            if (name != "resource")
            {
                throw new InvalidOperationException($"run setup: tree node \"{node.Id}\" conditional op \"{name}\" is not one the sim runs");
            }
            body.Add(new ResourceOp(op.GetProperty("amount").GetInt64()));
        }
        return new EffectOp[]
        {
            new ConditionalOp
            {
                If = new OpCondition(kind, id, threshold),
                Consume = consume,
                Then = body,
            },
        };
    }

    // The closed condition list (ABILITY-TREES s1.3); an unknown kind is refused.
    private static ConditionKind ConditionKindOf(string? kind, string nodeId) => kind switch
    {
        "selfHas" => ConditionKind.SelfHas,
        "targetHas" => ConditionKind.TargetHas,
        "selfResourceAtLeast" => ConditionKind.SelfResourceAtLeast,
        "targetCasting" => ConditionKind.TargetCasting,
        "hitCountAtLeast" => ConditionKind.HitCountAtLeast,
        _ => throw new InvalidOperationException($"run setup: tree node \"{nodeId}\" has an unknown condition \"{kind}\""),
    };

    // A rank node's edit is exactly one flat delta on one numeric field (ABILITY-TREES s1.2); anything else is refused.
    private static EffectDelta RankDeltaOf(C.TreeNodeDef node)
    {
        using var doc = JsonDocument.Parse(node.Edit!);
        if (!doc.RootElement.TryGetProperty("effectDelta", out var deltas)
            || deltas.ValueKind != JsonValueKind.Array
            || deltas.GetArrayLength() != 1)
        {
            throw new InvalidOperationException($"run setup: tree node \"{node.Id}\" must carry exactly one effectDelta");
        }
        var only = deltas[0];
        var op = only.GetProperty("op").GetString() ?? throw new InvalidOperationException($"run setup: tree node \"{node.Id}\" has no op");
        var field = only.GetProperty("field").GetString() ?? throw new InvalidOperationException($"run setup: tree node \"{node.Id}\" has no field");
        return new EffectDelta(op, field, only.GetProperty("delta").GetInt64());
    }
}
