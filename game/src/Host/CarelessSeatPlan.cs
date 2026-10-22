using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Bots;

namespace Keyrun.Host;

/// <summary>A careless bot seat (<c>--careless-seat S@T:GROUP</c>, CARELESS-SEAT): bot actor <paramref name="ActorId"/>
/// strays toward the idle group <paramref name="GroupId"/> from run tick <paramref name="Tick"/> (<see cref="Careless"/>).</summary>
/// <param name="ActorId">The bot seat's player actor (2 and 3 in the three-seat party).</param>
/// <param name="Tick">The run tick (pre-step) from which the seat strays, 0 or more.</param>
/// <param name="GroupId">The group's id: a pull id of the run's dungeon (choir, pair, seed, boss).</param>
public sealed record CarelessSeatPlan(int ActorId, int Tick, string GroupId)
{
    /// <summary>Parses <c>S@T:GROUP</c> (S a positive actor id, T a tick of 0 or more, GROUP a non-empty id).</summary>
    /// <param name="raw">The flag value.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">Not of the form S@T:GROUP.</exception>
    public static CarelessSeatPlan Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var at = raw.Split('@');
        var rest = at.Length == 2 ? at[1].Split(':') : Array.Empty<string>();
        if (at.Length != 2 || rest.Length != 2 || rest[1].Length == 0 ||
            !int.TryParse(at[0], NumberStyles.None, CultureInfo.InvariantCulture, out var actor) || actor <= 0 ||
            !int.TryParse(rest[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tick))
        {
            throw new ArgumentException($"--careless-seat needs actorId@tick:groupId, got '{raw}'");
        }
        return new CarelessSeatPlan(actor, tick, rest[1]);
    }

    /// <summary>The bots' plan: this seat strays toward the actors of <see cref="GroupId"/>'s pull in <paramref name="dungeon"/>.</summary>
    /// <param name="dungeon">The run's dungeon.</param>
    /// <returns>The plan by seat.</returns>
    /// <exception cref="ArgumentException">The dungeon has no pull with that id (fail closed: a typo never runs a normal party).</exception>
    public IReadOnlyDictionary<int, CarelessPlan> For(Keyrun.Sim.Encounter.DungeonDef dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        var pull = dungeon.Pulls.FirstOrDefault(p => string.Equals(p.Id, GroupId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"--careless-seat: dungeon {dungeon.Id} has no group '{GroupId}'");
        return new Dictionary<int, CarelessPlan> { [ActorId] = new CarelessPlan(Tick, pull.Spawns.Select(s => s.ActorId).ToList()) };
    }
}
