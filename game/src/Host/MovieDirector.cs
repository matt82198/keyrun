using System;
using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Host;

/// <summary>One proof frame due on the step just observed: its file name, the trace event it shows, and that
/// event's tick.</summary>
/// <param name="Name">The PNG file name (01-choir-chase.png, 02-lieutenant-cast-bar.png, 03-after-kick.png).</param>
/// <param name="Event">The trace event the frame was taken for, as a RunLog-style line.</param>
/// <param name="EventTick">The tick of that event.</param>
public sealed record MovieShot(string Name, string Event, int EventTick);

/// <summary>
/// Picks the <c>--write-movie</c> proof frames (Godot-free). RED STUB: still the fixed ticks 8 / 220 / 246.
/// </summary>
public sealed class MovieDirector
{
    /// <summary>The Choir chase frame.</summary>
    public const string ChaseFrame = "01-choir-chase.png";

    /// <summary>The must-kick cast bar frame.</summary>
    public const string CastBarFrame = "02-lieutenant-cast-bar.png";

    /// <summary>The frame after the kick.</summary>
    public const string AfterKickFrame = "03-after-kick.png";

    /// <summary>Ticks into a must-kick cast before its bar is captured.</summary>
    public const int CastBarLeadTicks = 4;

    private static readonly (int Tick, string Name)[] Fixed =
    {
        (8, ChaseFrame),
        (220, CastBarFrame),
        (246, AfterKickFrame),
    };

    /// <summary>Builds a director for one run.</summary>
    /// <param name="wardenId">The human Warden's actor id.</param>
    /// <param name="abilities">The run's abilities (which casts are must-kick).</param>
    public MovieDirector(int wardenId, IReadOnlyDictionary<string, AbilityDef> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        _ = wardenId;
    }

    /// <summary>The frames due on this step (captured once the step's frame is drawn).</summary>
    /// <param name="before">The state before the step.</param>
    /// <param name="after">The state after the step.</param>
    /// <param name="events">The step's events.</param>
    /// <returns>The shots, possibly empty.</returns>
    public IReadOnlyList<MovieShot> Observe(SimState before, SimState after, IReadOnlyList<SimEvent> events)
    {
        ArgumentNullException.ThrowIfNull(after);
        var shots = new List<MovieShot>();
        foreach (var (tick, name) in Fixed)
        {
            if (tick == after.Tick)
            {
                shots.Add(new MovieShot(name, "fixed", tick));
            }
        }
        return shots;
    }
}
