namespace Keyrun.Content;

/// <summary>
/// One pack of a seeded dungeon's roster (D46: generate by seed, then fill; game/content/data/roster/&lt;id&gt;.json, one
/// pack per file, ruling R-LOADER-1). Unlike a <see cref="LayoutPack"/> it has no room and no centre: the fill step
/// places it. Its forces are the sum of its members' values (ruling R-PACKS-7), so it carries no points of its own.
/// </summary>
public sealed record RosterPack : IContentItem
{
    /// <summary>Pack id, unique across every roster.</summary>
    public required string Id { get; init; }

    /// <summary>The dungeon whose pool deals it.</summary>
    public required string Dungeon { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>What the pack teaches (BIBLE section 9: every pull teaches one thing).</summary>
    public required string Teaches { get; init; }

    /// <summary>Aggro ring radius, mm (positive), as a layout pack carries.</summary>
    public required int AggroMm { get; init; }

    /// <summary>Member groups; each group's <c>spawn</c> is its offset from the pack centre the fill picks, mm.</summary>
    public required ValueList<PullEnemy> Enemies { get; init; }

    /// <summary>
    /// Packs of the same dungeon built to be chain-pulled with this one (adjacent rooms, complementary casts; "huge pulls
    /// are fun"), or null. The pairing is mutual: each partner lists this pack back (<see cref="ContentValidator"/>).
    /// </summary>
    public ValueList<string>? ChainWith { get; init; }

    /// <summary>Why the chain works (the casts that overlap, what the party trades), for the fill and route lane; or null.</summary>
    public string? ChainNote { get; init; }
}
