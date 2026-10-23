using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

// G2.2 design/ABILITY-TREES.md s2, s5.3 (ruling R-TREES-3): one class's tree. Points are earned per completion (timed or
// depleted, never abandoned), capped at 20; each node is taken with a free point, a key gate and an optional requires.

/// <summary>A tree node the rules refuse (no free point, key gate not met, requires not owned, or already taken).</summary>
public sealed class TreeUnlockException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was refused.</param>
    public TreeUnlockException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// One class's tree progress: tree points earned, the best key completed with the class, and the owned nodes in the
/// order they were taken. Immutable. Free points are <c>Earned - Nodes.Count</c>, derived and never stored.
/// Equality compares the node sequence by content, so a state read back from a save equals the one written.
/// </summary>
/// <param name="Earned">Tree points earned, 0 to <see cref="MaxPoints"/>.</param>
/// <param name="BestKey">The highest key completed with this class (0 before the first completion).</param>
/// <param name="Nodes">The owned node ids, in the order taken.</param>
public sealed record TreeState(int Earned, int BestKey, IReadOnlyList<string> Nodes)
{
    /// <summary>The most tree points a class can earn (ABILITY-TREES s2).</summary>
    public const int MaxPoints = 20;

    /// <summary>A class that has completed nothing and owns no nodes.</summary>
    public static readonly TreeState Empty = new(0, 0, Array.Empty<string>());

    /// <summary>Whether <paramref name="other"/> has the same points, best key and node sequence.</summary>
    /// <param name="other">The other state.</param>
    /// <returns>True when equal by content.</returns>
    public bool Equals(TreeState? other) =>
        other is not null
        && Earned == other.Earned
        && BestKey == other.BestKey
        && Nodes.SequenceEqual(other.Nodes, StringComparer.Ordinal);

    /// <summary>
    /// A hash over the points, best key and node sequence, consistent with <see cref="Equals(TreeState?)"/>. It is a plain
    /// fold rather than <c>HashCode</c>, which the sim bans as randomly seeded per process (the sim is deterministic).
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (Earned * 397) ^ BestKey;
            foreach (var node in Nodes)
            {
                foreach (var c in node)
                {
                    hash = (hash * 31) + c;
                }
                hash = (hash * 31) + 1;
            }
            return hash;
        }
    }

    /// <summary>
    /// Banks one run against this tree. A timed or depleted completion earns one point (capped at <see cref="MaxPoints"/>)
    /// and raises <see cref="BestKey"/>; an abandon earns nothing and changes nothing (R-TREES-3 a).
    /// </summary>
    /// <param name="result">How the run ended.</param>
    /// <param name="keyLevel">The key the run was played at.</param>
    /// <returns>The new state.</returns>
    public TreeState Award(RunResult result, int keyLevel)
    {
        if (result == RunResult.Abandoned)
        {
            return this;
        }
        return this with
        {
            Earned = Math.Min(Earned + 1, MaxPoints),
            BestKey = Math.Max(BestKey, keyLevel),
        };
    }

    /// <summary>
    /// Takes a node with a free point. Refused (<see cref="TreeUnlockException"/>) when it is already owned, when no free
    /// point is left, when <paramref name="minKey"/> is above <see cref="BestKey"/>, or when <paramref name="requires"/> is
    /// not owned yet.
    /// </summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="minKey">The key gate.</param>
    /// <param name="requires">The node that must be owned first, or null.</param>
    /// <returns>The new state with the node owned.</returns>
    /// <exception cref="TreeUnlockException">The node is refused.</exception>
    public TreeState Take(string nodeId, int minKey, string? requires)
    {
        ArgumentException.ThrowIfNullOrEmpty(nodeId);
        if (Nodes.Contains(nodeId, StringComparer.Ordinal))
        {
            throw new TreeUnlockException($"{nodeId} is already taken");
        }
        if (Nodes.Count >= Earned)
        {
            throw new TreeUnlockException($"{nodeId} needs a free point and none is left");
        }
        if (minKey > BestKey)
        {
            throw new TreeUnlockException($"{nodeId} needs key {minKey}, best completed key is {BestKey}");
        }
        if (requires is not null && !Nodes.Contains(requires, StringComparer.Ordinal))
        {
            throw new TreeUnlockException($"{nodeId} requires {requires}, which is not owned");
        }
        return this with { Nodes = Nodes.Append(nodeId).ToArray() };
    }

    /// <summary>
    /// Refunds a node and every node after it in <paramref name="chainOrder"/> (ABILITY-TREES s5.4: no cost, no confirm).
    /// A node that is not in the chain drops alone. Points are kept, so the freed ones are free again.
    /// </summary>
    /// <param name="nodeId">The node the player refunds.</param>
    /// <param name="chainOrder">The chain, in order.</param>
    /// <returns>The new state.</returns>
    public TreeState Refund(string nodeId, IReadOnlyList<string> chainOrder)
    {
        ArgumentException.ThrowIfNullOrEmpty(nodeId);
        ArgumentNullException.ThrowIfNull(chainOrder);
        var drop = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var index = -1;
        for (var i = 0; i < chainOrder.Count; i++)
        {
            if (string.Equals(chainOrder[i], nodeId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        if (index >= 0)
        {
            for (var i = index; i < chainOrder.Count; i++)
            {
                drop.Add(chainOrder[i]);
            }
        }
        return this with { Nodes = Nodes.Where(n => !drop.Contains(n)).ToArray() };
    }

    /// <summary>
    /// The fully unlocked state for a node list: every node owned, with the given points and best key. Free points are
    /// derived, so <c>earned - nodes.Count</c> is what is left over.
    /// </summary>
    /// <param name="nodes">The node ids, in chain order.</param>
    /// <param name="earned">Tree points earned.</param>
    /// <param name="bestKey">The best key completed.</param>
    /// <returns>The state.</returns>
    public static TreeState AllUnlocked(IReadOnlyList<string> nodes, int earned, int bestKey)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return new TreeState(earned, bestKey, nodes.ToArray());
    }
}

/// <summary>
/// The node gates of a class tree (ABILITY-TREES s1.2, s2). A node id is "&lt;ability&gt;.&lt;kind&gt;"; the four kinds are
/// the chain, and the sim needs no content to gate them: Rank II and Enhanced are ungated, the choice node needs key 4,
/// Rank III needs key 7. Content validation of which abilities exist stays in the content layer.
/// </summary>
public static class TreeNodeRules
{
    // Kind, key gate and the kind it requires on the same ability (null for the first node).
    private static readonly IReadOnlyDictionary<string, (int MinKey, string? RequiresKind)> Kinds =
        new Dictionary<string, (int, string?)>(StringComparer.Ordinal)
        {
            ["rank2"] = (0, null),
            ["enhanced"] = (0, "rank2"),
            ["choice"] = (4, "enhanced"),
            ["rank3"] = (7, "choice"),
        };

    /// <summary>Looks up a node's key gate and required node.</summary>
    /// <param name="nodeId">The node id.</param>
    /// <param name="minKey">The key gate.</param>
    /// <param name="requires">The node it requires, or null.</param>
    /// <returns>True when the id is a known node.</returns>
    public static bool TryGate(string nodeId, out int minKey, out string? requires)
    {
        minKey = 0;
        requires = null;
        if (string.IsNullOrEmpty(nodeId))
        {
            return false;
        }
        var dot = nodeId.LastIndexOf('.');
        if (dot <= 0 || dot == nodeId.Length - 1)
        {
            return false;
        }
        var ability = nodeId[..dot];
        var kind = nodeId[(dot + 1)..];
        if (!Kinds.TryGetValue(kind, out var rule))
        {
            return false;
        }
        minKey = rule.MinKey;
        requires = rule.RequiresKind is null ? null : ability + "." + rule.RequiresKind;
        return true;
    }
}
