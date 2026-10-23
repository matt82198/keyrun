using System.Text.Json;

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

    /// <summary>
    /// The node's edit object as compact JSON text (ABILITY-TREES s1.2, PIVOT s1.5): a <c>rank</c> node's
    /// <c>effectDelta</c>, or an <c>enhanced</c> node's <c>conditional</c>. Text rather than a <see cref="JsonElement"/>,
    /// so two loads of the same content compare equal (a record's equality on a JsonElement is by reference). Validated by
    /// <see cref="TreesRule.Validate"/> at load; <c>null</c> for a <c>choice</c> node, which carries no edit.
    /// </summary>
    public string? Edit { get; init; }

    /// <summary>The source a <c>choice</c> node draws its options from (<c>"talents"</c>); <c>null</c> for other kinds.</summary>
    public string? OptionsFrom { get; init; }
}

/// <summary>
/// One tree node as the file spells it (the typed read's shape). It differs from <see cref="TreeNodeDef"/> only in that
/// <c>edit</c> is read as a <see cref="JsonElement"/> and then stored as text; <see cref="ToDef"/> maps it.
/// </summary>
public sealed record TreeNodeFile
{
    /// <summary>Node id.</summary>
    public required string Id { get; init; }

    /// <summary>Class id.</summary>
    public required string ClassId { get; init; }

    /// <summary>Ability id.</summary>
    public required string AbilityId { get; init; }

    /// <summary>Node kind.</summary>
    public required string Kind { get; init; }

    /// <summary>Key gate.</summary>
    public required int MinKey { get; init; }

    /// <summary>Required node ids.</summary>
    public required ValueList<string> Requires { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>One-line description.</summary>
    public required string Text { get; init; }

    /// <summary>The edit object as the file holds it, or absent.</summary>
    public JsonElement? Edit { get; init; }

    /// <summary>The options source, or absent.</summary>
    public string? OptionsFrom { get; init; }

    /// <summary>Maps the file shape to the registry node; the edit is kept as compact JSON text.</summary>
    /// <returns>The registry node.</returns>
    public TreeNodeDef ToDef() => new()
    {
        Id = Id,
        ClassId = ClassId,
        AbilityId = AbilityId,
        Kind = Kind,
        MinKey = MinKey,
        Requires = Requires,
        Name = Name,
        Text = Text,
        Edit = Edit?.GetRawText(),
        OptionsFrom = OptionsFrom,
    };
}

/// <summary>
/// One class's tree file (game/content/data/trees/&lt;classId&gt;.json): a root object whose <c>nodes</c> array holds the
/// class's nodes, the shape <see cref="TreesRule"/> validates.
/// </summary>
public sealed record TreeFileDef
{
    /// <summary>The class's nodes, in chain order.</summary>
    public required ValueList<TreeNodeFile> Nodes { get; init; }
}
