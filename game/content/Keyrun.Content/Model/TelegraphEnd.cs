namespace Keyrun.Content;

/// <summary>How a floor shape ends on one sim event (<see cref="TelegraphEnd.Of"/>).</summary>
public enum TelegraphEndKind
{
    /// <summary>The event does not end the shape.</summary>
    None,

    /// <summary>The cast landed (<c>telegraph_hit</c>): flash bright, then fade and free.</summary>
    Flash,

    /// <summary>The cast will never land (<c>telegraph_cancel</c>): free the shape now, with no flash.</summary>
    Vanish,
}

/// <summary>
/// TELEGRAPH-CANCEL: which sim event ends a floor telegraph's shape in src/View/TelegraphView.cs, and how. A flash
/// reads as a hit, so only a landed cast flashes. Pure and Godot-free so the decision is unit-tested. Display only;
/// never a rule.
/// </summary>
public static class TelegraphEnd
{
    /// <summary>The ending one event type gives the shape its event names.</summary>
    /// <param name="eventType">The sim event's type.</param>
    /// <returns>How the shape ends, or <see cref="TelegraphEndKind.None"/>.</returns>
    public static TelegraphEndKind Of(string eventType) => eventType switch
    {
        "telegraph_hit" => TelegraphEndKind.Flash,
        _ => TelegraphEndKind.None,
    };
}
