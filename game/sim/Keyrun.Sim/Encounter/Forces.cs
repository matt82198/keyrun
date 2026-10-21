using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>
/// The Hollow Door's data (BIBLE s9.5, ruling R3): the Seed pull whose clear opens it, the boss pull it guards, the
/// forces table, the bar's size and the share of the bar the door needs (basis points, 10000 = 100%).
/// </summary>
/// <param name="SeedPullId">The pull whose every enemy must be dead before the door can open.</param>
/// <param name="BossPullId">The pull the door guards (the Warden).</param>
/// <param name="PointsByEnemy">Enemy id to forces value (forces.json).</param>
/// <param name="Required">Points that fill the bar (forces.json required).</param>
/// <param name="RequiredBp">The share of the bar the door needs, basis points (forces.json bossDoorRequiredBp).</param>
public sealed record BossDoorGate(string SeedPullId, string BossPullId, IReadOnlyDictionary<string, int> PointsByEnemy, int Required, int RequiredBp);

/// <summary>
/// The Hollow Door (BIBLE s9.5, ruling R3): the boss pull may engage only once the Seed pull is cleared AND the Forces
/// bar reads at or above the gate's requirement. Pure: reads the run's state, never mutates it.
/// </summary>
public static class BossDoor
{
    /// <summary>True when the door is open on <paramref name="state"/>: the Seed is cleared and the bar is at the requirement.</summary>
    /// <param name="state">The run state.</param>
    /// <param name="dungeon">The dungeon (its pulls name the Seed).</param>
    /// <param name="gate">The door's data.</param>
    public static bool IsOpen(SimState state, DungeonDef dungeon, BossDoorGate gate)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(gate);
        return SeedCleared(state, dungeon, gate.SeedPullId)
            && Forces.Read(state, gate.PointsByEnemy, gate.Required).PercentBp >= gate.RequiredBp;
    }

    /// <summary>The group ids the engage step may engage this tick: every id except the boss while the door is shut.</summary>
    /// <param name="gate">The door's data, or null for no door (every oracle run and every flat-arena run).</param>
    /// <param name="dungeon">The dungeon.</param>
    /// <param name="state">The post-combat state.</param>
    /// <param name="groupIds">The groups the aggro rule triggered.</param>
    public static IReadOnlyList<string> Admit(BossDoorGate? gate, DungeonDef dungeon, SimState state, IReadOnlyList<string> groupIds)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        if (gate is null || !groupIds.Contains(gate.BossPullId, StringComparer.Ordinal) || IsOpen(state, dungeon, gate))
        {
            return groupIds;
        }
        return groupIds.Where(id => !string.Equals(id, gate.BossPullId, StringComparison.Ordinal)).ToList();
    }

    // Every enemy of the Seed pull is dead or absent. A missing or empty Seed pull never counts as cleared (fail closed).
    private static bool SeedCleared(SimState state, DungeonDef dungeon, string seedPullId)
    {
        var seed = dungeon.Pulls.FirstOrDefault(p => string.Equals(p.Id, seedPullId, StringComparison.Ordinal));
        return seed is not null && seed.Spawns.Count > 0
            && seed.Spawns.All(s => !state.Actors.TryGetValue(s.ActorId, out var a) || !a.Alive);
    }
}
