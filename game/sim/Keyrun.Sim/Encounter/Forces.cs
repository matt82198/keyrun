using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// The Enemy Forces bar as the HUD shows it (BIBLE s9.2; Mythic+ enemy forces, ledger f-228202e1). Integers only:
/// <see cref="PercentBp"/> is basis points of <see cref="Required"/>, floored, and passes 10000 on an overpull.
/// </summary>
/// <param name="Points">Points banked so far.</param>
/// <param name="Required">Points that fill the bar (100%); 0 or less reads as an empty bar.</param>
public sealed record ForcesReading(int Points, int Required)
{
    private const int FullBp = 10000;

    /// <summary>Banked share of the bar in basis points (10000 = 100%), floored; 0 when there is no requirement.</summary>
    public int PercentBp => Required <= 0 ? 0 : (int)Math.Min(int.MaxValue, (long)Math.Max(0, Points) * FullBp / Required);

    /// <summary>True once the banked points reach the requirement (the Mythic+ "Enemy Forces" check).</summary>
    public bool Full => Required > 0 && Points >= Required;

    /// <summary>WoW's tracker text with two decimals: "Enemy Forces 16.00%", and "... done" once <see cref="Full"/>.</summary>
    public string Label
    {
        get
        {
            var bp = PercentBp;
            var text = FormattableString.Invariant($"Enemy Forces {bp / 100}.{bp % 100:00}%");
            return Full ? text + " done" : text;
        }
    }

    /// <summary>The HUD proof line: "HUD forces pts=160/1000 bp=1600 full=0".</summary>
    /// <returns>The line.</returns>
    public string LogLine() => FormattableString.Invariant(
        $"HUD forces pts={Points}/{Required} bp={PercentBp} full={(Full ? 1 : 0)}");
}

/// <summary>
/// FINISH-NEEDS-FORCES (ledger f-8c50a787, WoW Mythic+): the dungeon's Enemy Forces requirement. A dungeon that carries
/// one (<see cref="DungeonDef.ForcesGoal"/>) completes only when the last pull is cleared AND <see cref="Met"/> holds, in
/// either order (<see cref="Runs.RunWiring.CheckRunEnd"/>). A requirement of zero or less is rejected (fail closed).
/// </summary>
public sealed record ForcesGoal
{
    /// <summary>Builds the goal.</summary>
    /// <param name="pointsByEnemy">Enemy id to forces value (forces.json); a missing id is worth 0.</param>
    /// <param name="required">Points that fill the bar (100%); must be positive.</param>
    public ForcesGoal(IReadOnlyDictionary<string, int> pointsByEnemy, int required)
    {
        ArgumentNullException.ThrowIfNull(pointsByEnemy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(required);
        PointsByEnemy = pointsByEnemy;
        Required = required;
    }

    /// <summary>Enemy id to forces value.</summary>
    public IReadOnlyDictionary<string, int> PointsByEnemy { get; }

    /// <summary>Points that fill the bar.</summary>
    public int Required { get; }

    /// <summary>The bar's reading for <paramref name="state"/>.</summary>
    /// <param name="state">The run state.</param>
    /// <returns>The reading.</returns>
    public ForcesReading Read(SimState state) => Forces.Read(state, PointsByEnemy, Required);

    /// <summary>True once the run's dead enemies bank the requirement.</summary>
    /// <param name="state">The run state.</param>
    /// <returns>True when the bar is full.</returns>
    public bool Met(SimState state) => Read(state).Full;
}

/// <summary>
/// The Enemy Forces tally (BIBLE s9.2, HUD-FORCES): the sum of the forces value of every enemy actor that has died in
/// the run. It is read from the run's own state (a dead actor stays in <see cref="SimState.Actors"/> with
/// <c>Alive = false</c> and its enemy id as <c>ClassId</c>), so it adds no sim state and never moves a trace. Players
/// never bank, an enemy with no entry (the boss, the Twins, Echo adds) banks 0, and since a dead enemy is never
/// revived a death or a wipe never lowers it. The counter kept in EncounterState with a <c>forces</c> event is
/// FORCES-ROUTE-PLAN increment 1, which can replace this read without changing what the bar shows.
/// </summary>
public static class Forces
{
    /// <summary>Points banked by the run's dead enemies.</summary>
    /// <param name="state">The run state.</param>
    /// <param name="pointsByEnemy">Enemy id to forces value (forces.json); a missing id is worth 0.</param>
    /// <returns>The banked points.</returns>
    public static int Points(SimState state, IReadOnlyDictionary<string, int> pointsByEnemy)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(pointsByEnemy);
        long sum = 0;
        foreach (var actor in state.Actors.Values)
        {
            if (actor.Kind == ActorKind.Enemy && !actor.Alive && pointsByEnemy.TryGetValue(actor.ClassId, out var points))
            {
                sum += Math.Max(0, points);
            }
        }
        return (int)Math.Min(int.MaxValue, sum);
    }

    /// <summary>The bar's reading for the run.</summary>
    /// <param name="state">The run state.</param>
    /// <param name="pointsByEnemy">Enemy id to forces value.</param>
    /// <param name="required">Points that fill the bar.</param>
    /// <returns>The reading.</returns>
    public static ForcesReading Read(SimState state, IReadOnlyDictionary<string, int> pointsByEnemy, int required) =>
        new(Points(state, pointsByEnemy), required);
}
