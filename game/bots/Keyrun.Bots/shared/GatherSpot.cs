using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// A point the party gathers at to fight: the spot the tank carries the packs back to. <paramref name="GroupIds"/> names the
/// <see cref="Keyrun.Sim.Encounter.AggroGroup"/>s pulled to this spot. Data only; the rules that read it live elsewhere.
/// </summary>
/// <param name="Id">The gather's id.</param>
/// <param name="Spot">The fight spot.</param>
/// <param name="GroupIds">The ids of the aggro groups gathered here.</param>
public sealed record GatherSpot(string Id, Vec3 Spot, IReadOnlyList<string> GroupIds);
