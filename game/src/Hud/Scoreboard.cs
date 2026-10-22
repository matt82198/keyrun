using System;
using System.Collections.Generic;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// Enemies killed out of the enemies in the dungeon that count toward Enemy Forces (Godot-free, HUD-SCOREBOARD-LIVE,
/// Matt 2026-10-21 "the kill count"). Read like <see cref="Forces.Points"/> from the run's own state: an enemy's
/// <c>death</c> event leaves it in <see cref="SimState.Actors"/> with <c>Alive = false</c> and it is never revived, so
/// the dead count equals the death events seen. Counted enemies are those with a forces entry (forces.json); the boss,
/// the Sundered Twins and Echo adds are worth 0 forces and are not trash kills (the pull header counts the boss).
/// </summary>
/// <param name="Killed">Counted enemies dead.</param>
/// <param name="Total">Counted enemies in the dungeon: those the state holds plus pull spawns it does not hold yet.</param>
public sealed record KillCount(int Killed, int Total)
{
    /// <summary>The readout beside the forces bar: "Kills 3/28".</summary>
    public string Label => FormattableString.Invariant($"Kills {Killed}/{Total}");

    /// <summary>Counts the run's kills.</summary>
    /// <param name="run">The run.</param>
    /// <param name="pointsByEnemy">Enemy id to forces value (forces.json); an id with an entry is a counted enemy.</param>
    /// <returns>The count.</returns>
    public static KillCount Of(Run run, IReadOnlyDictionary<string, int> pointsByEnemy)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(pointsByEnemy);
        var killed = 0;
        var total = 0;
        foreach (var actor in run.State.Actors.Values)
        {
            if (actor.Kind != ActorKind.Enemy || !pointsByEnemy.ContainsKey(actor.ClassId))
            {
                continue;
            }
            total++;
            if (!actor.Alive)
            {
                killed++;
            }
        }
        foreach (var pull in run.Dungeon.Pulls)
        {
            foreach (var spawn in pull.Spawns)
            {
                if (!run.State.Actors.ContainsKey(spawn.ActorId) && pointsByEnemy.ContainsKey(spawn.ClassId))
                {
                    total++;
                }
            }
        }
        return new KillCount(killed, total);
    }
}

/// <summary>
/// The Mythic+ scoreboard as the HUD shows it on one tick (Godot-free, HUD-SCOREBOARD-LIVE): the run clock
/// (<see cref="SlackReading"/>), Enemy Forces (<see cref="Forces.Read"/>), the kill count (<see cref="KillCount"/>), the
/// deaths counter and the pull header (<see cref="ObjectiveModel"/>), each read by the same rule the drawn node uses.
/// The host writes <see cref="LogLine"/> to the run log every <see cref="EveryTicks"/> ticks, so a capture log proves
/// what the screen showed. Time is game ticks at <see cref="Keyrun.Sim.Core.Tick.TickHz"/> (20 Hz), never render frames.
/// </summary>
/// <param name="Tick">The sim tick.</param>
/// <param name="TimerSeconds">The run clock in whole seconds, as the timer label shows it (run time plus death time).</param>
/// <param name="ForcesBp">Enemy Forces in basis points (10000 = 100%).</param>
/// <param name="Kills">The kill count.</param>
/// <param name="Deaths">Player deaths this run (the Deaths counter).</param>
/// <param name="PullId">The pull the header names ("done" once cleared).</param>
/// <param name="Remain">Enemies the header says remain.</param>
public sealed record ScoreboardReading(int Tick, long TimerSeconds, int ForcesBp, KillCount Kills, int Deaths, string PullId, int Remain)
{
    /// <summary>The proof line cadence: 60 ticks (3 s of game time at 20 Hz).</summary>
    public const int EveryTicks = 60;

    /// <summary>True on the ticks the host writes the proof line.</summary>
    /// <param name="tick">The sim tick.</param>
    /// <returns>True every <see cref="EveryTicks"/> ticks, tick 0 included.</returns>
    public static bool Due(int tick) => tick >= 0 && tick % EveryTicks == 0;

    /// <summary>"HUD scoreboard tick=780 timer=39 forces=0.00 kills=0/28 deaths=0 pull=choir remain=3".</summary>
    /// <returns>The line.</returns>
    public string LogLine() => FormattableString.Invariant(
        $"HUD scoreboard tick={Tick} timer={TimerSeconds} forces={ForcesBp / 100}.{ForcesBp % 100:00} kills={Kills.Killed}/{Kills.Total} deaths={Deaths} pull={PullId} remain={Remain}");

    /// <summary>Reads the scoreboard.</summary>
    /// <param name="run">The run.</param>
    /// <param name="registry">Content for the header's pull numbering, or null.</param>
    /// <param name="pointsByEnemy">Enemy id to forces value (forces.json).</param>
    /// <param name="required">Points that fill the forces bar.</param>
    /// <returns>The reading.</returns>
    public static ScoreboardReading Of(Run run, ContentRegistry? registry, IReadOnlyDictionary<string, int> pointsByEnemy, int required)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(pointsByEnemy);
        var clock = SlackReading.Of(run);
        var objective = ObjectiveModel.Build(run, registry);
        return new ScoreboardReading(
            run.State.Tick,
            Math.Max(0, clock.ClockTicks) / Keyrun.Sim.Core.Tick.TickHz,
            Forces.Read(run.State, pointsByEnemy, required).PercentBp,
            KillCount.Of(run, pointsByEnemy),
            clock.Deaths,
            objective.PullId,
            objective.Remaining);
    }

    /// <summary>The forces table and requirement from content (forces.json), or an empty table when content has none.</summary>
    /// <param name="registry">Content, or null.</param>
    /// <returns>Enemy id to points, and the required points.</returns>
    public static (IReadOnlyDictionary<string, int> Table, int Required) ForcesOf(ContentRegistry? registry)
    {
        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        if (registry?.Forces is not { } forces)
        {
            return (table, 0);
        }
        foreach (var v in forces.Values)
        {
            table[v.EnemyId] = v.Points;
        }
        return (table, forces.Required);
    }
}
