namespace Keyrun.Content;

/// <summary>
/// One improvement-tree node of a rotational ability (ABILITY-TREES s1.2, s5.1; PIVOT s1.5 field names). The four nodes
/// of an ability are, in chain order, rank, enhanced, choice and rank; each node past the first requires the node
/// before it. <see cref="Id"/> is prefixed by <see cref="AbilityId"/> and the chain's key gates are in <see cref="MinKey"/>.
/// </summary>
public sealed record TreeNodeDef : IContentItem
{
    /// <summary>Node id, unique across every tree; <c>&lt;abilityId&gt;.&lt;suffix&gt;</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The class whose tree this node belongs to (a key of <see cref="ContentRegistry.Classes"/>).</summary>
    public required string ClassId { get; init; }

    /// <summary>The rotational ability the node grows (a key of <see cref="ContentRegistry.Abilities"/>).</summary>
    public required string AbilityId { get; init; }

    /// <summary>Node kind: <c>rank</c>, <c>enhanced</c> or <c>choice</c> (ABILITY-TREES s1.2).</summary>
    public required string Kind { get; init; }

    /// <summary>The key level at which the node unlocks (the chain's gates are 0, 0, 4, 7).</summary>
    public required int MinKey { get; init; }

    /// <summary>Node ids that must be taken first (the previous node of the chain; empty for the first node).</summary>
    public required ValueList<string> Requires { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>One-line description.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// One class's tree file (game/content/data/trees/&lt;classId&gt;.json): a root object whose <c>nodes</c> array holds the
/// class's nodes, the shape <see cref="TreesRule"/> validates.
/// </summary>
public sealed record TreeFileDef
{
    /// <summary>The class's nodes, in chain order.</summary>
    public required ValueList<TreeNodeDef> Nodes { get; init; }
}
