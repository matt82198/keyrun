using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Bots;

/// <summary>
/// A careless seat's plan (Matt 2026-10-20: "a bad player will accidentally aggro more groups, just test it naturally"):
/// from <paramref name="FromTick"/> the seat strays off the party toward the enemy group <paramref name="GroupActorIds"/>.
/// </summary>
/// <param name="FromTick">The first tick the seat strays.</param>
/// <param name="GroupActorIds">The actor ids of the group it strays toward (a pull's spawns).</param>
public sealed record CarelessPlan(int FromTick, IReadOnlyList<int> GroupActorIds);

/// <summary>A careless seat: a bot that plays badly by wandering toward an enemy group (see <see cref="CarelessPlan"/>).</summary>
public static class Careless
{
    /// <summary>The careless bot over <paramref name="inner"/>. Not implemented yet: returns <paramref name="inner"/>.</summary>
    /// <param name="inner">The seat's bot.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The bot.</returns>
    public static IBot Wrap(IBot inner, CarelessPlan plan)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(plan);
        return inner;
    }
}
