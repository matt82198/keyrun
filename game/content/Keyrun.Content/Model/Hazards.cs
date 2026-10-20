namespace Keyrun.Content;

/// <summary>
/// A room hazard (BIBLE section 9.4, hazards.json, C#-only): an untargetable environment caster bound to one branch
/// room. The cast schedule and telegraphs arrive with the HAZARDS increment.
/// </summary>
public sealed record HazardDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>The branch room it runs in.</summary>
    public required string Room { get; init; }
}
