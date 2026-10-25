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
    /// A <c>choice</c> node carries no edit and is left out. An <c>enhanced</c> node whose conditional body uses an op the sim
    /// does not run yet is left out of this shared map, so one unshipped node cannot break every seat; <see cref="StartFor"/>
    /// then reports the owned one as skipped (RunStart.SkippedTreeNodes, R-TREES-10b). A malformed edit of either kind throws.
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
            else if (node.Kind == "enhanced" && TryConditionalOf(node, out var ops))
            {
                edits[node.Id] = new TreeNodeEdit(Patch: new GearPatch { EffectsAppend = ops });
            }
        }
        return edits;
    }

    /// <summary>
    /// Fails closed on malformed content: every owned <c>enhanced</c> node must carry a well-formed conditional (shape and
    /// condition kind), so a malformed edit is refused loudly and names the node. An owned node whose conditional body uses an
    /// op the sim does not run is not an error here; it is skipped at run start (RunStart.SkippedTreeNodes, R-TREES-10b).
    /// Called by <see cref="StartFor"/> before a run starts.
    /// </summary>
    /// <param name="registry">The loaded content.</param>
    /// <param name="progression">The seat's progression.</param>
    /// <exception cref="InvalidOperationException">The seat owns an enhanced node whose conditional is malformed.</exception>
    public static void RequireOwnedEdits(C.ContentRegistry registry, Progression progression)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(progression);
        foreach (var owned in progression.Trees.Values.SelectMany(t => t.Nodes))
        {
            if (registry.Trees.TryGetValue(owned, out var node) && node.Kind == "enhanced")
            {
                _ = TryConditionalOf(node, out _);  // throws, naming the node, when the conditional is malformed
            }
        }
    }

    /// <summary>
    /// The owned tree node ids whose content edit the host could not map to a sim node edit (an <c>enhanced</c> node whose
    /// conditional body uses an op the sim does not run, see <see cref="TreeEdits"/>). The sim never sees these, so
    /// <see cref="StartFor"/> reports them as skipped (R-TREES-10b).
    /// </summary>
    /// <param name="registry">The loaded content.</param>
    /// <param name="progression">The seat's progression.</param>
    /// <param name="edits">The mapped node edits from <see cref="TreeEdits"/>.</param>
    /// <returns>The unmapped owned node ids, in the seat's order.</returns>
    public static IReadOnlyList<string> UnmappedOwnedNodes(
        C.ContentRegistry registry,
        Progression progression,
        IReadOnlyDictionary<string, TreeNodeEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(edits);
        return progression.Trees.Values
            .SelectMany(t => t.Nodes)
            .Distinct(StringComparer.Ordinal)
            .Where(id => registry.Trees.TryGetValue(id, out var node) && node.Edit is not null && !edits.ContainsKey(id))
            .ToList();
    }

    // An enhanced node's edit is exactly one conditional (ABILITY-TREES s5.2): "if" a closed condition, "consume", and "then"
    // ops. Returns true with the sim ops when every body op is one the sim runs (only "resource" is shipped so far), and false
    // when a body op is valid content the sim does not run yet. A malformed shape or an unknown condition kind throws, so bad
    // content still fails closed.
    private static bool TryConditionalOf(C.TreeNodeDef node, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IReadOnlyList<EffectOp>? ops)
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
            if (op.GetProperty("op").GetString() != "resource")
            {
                ops = null;
                return false;
            }
            body.Add(new ResourceOp(op.GetProperty("amount").GetInt64()));
        }
        ops = new EffectOp[]
        {
            new ConditionalOp
            {
                If = new OpCondition(kind, id, threshold),
                Consume = consume,
                Then = body,
            },
        };
        return true;
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
