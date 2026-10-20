using System;

namespace Keyrun.Content;

/// <summary>
/// The player-facing word for a class resource id (<see cref="ClassLoop.Resource"/>), as BIBLE.md names each loop:
/// Warden Momentum, Cinder Heat, Chanter Verse (lane RESOURCE-LABEL). Godot-free so xUnit pins it; src/Hud/AbilityBar.cs
/// labels the resource bar with it.
/// </summary>
public static class ResourceLabel
{
    /// <summary>The label for a class resource id.</summary>
    /// <param name="resourceId">The class json's <c>loop.resource</c>, e.g. "heat".</param>
    /// <returns>The word the HUD shows, e.g. "Heat".</returns>
    /// <exception cref="ArgumentException">The id names no shipped resource; there is no default label, so a new
    /// class resource cannot silently read as another class's word.</exception>
    public static string For(string resourceId) => resourceId switch
    {
        "momentum" => "Momentum",
        "heat" => "Heat",
        "verse" => "Verse",
        _ => throw new ArgumentException($"no HUD label for class resource \"{resourceId}\"", nameof(resourceId)),
    };
}
