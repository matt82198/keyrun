namespace Keyrun.Content;

/// <summary>
/// An optional trash pack (BIBLE section 9.1, packs.json, C#-only): it stands in one branch room beside the walking
/// line and engages as a whole when a party member enters its aggro ring. It never gates.
/// </summary>
public sealed record PackDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>The branch room it stands in (a room of a <see cref="BranchDef"/>).</summary>
    public required string Room { get; init; }

    /// <summary>Aggro ring centre, mm.</summary>
    public required SpawnPoint Center { get; init; }

    /// <summary>Aggro ring radius, mm (BIBLE: 4000).</summary>
    public required int RingMm { get; init; }

    /// <summary>Member groups; every member enemy needs a forces value.</summary>
    public required ValueList<PullEnemy> Enemies { get; init; }
}
