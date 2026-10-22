using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>One party roster slot (content-party.ts RosterSlot plus its SPAWN_X and HUMAN_MAX_HEALTH entries).</summary>
/// <param name="ClassId">The class that fills the slot.</param>
/// <param name="Role">The slot's role (bots dispatch on it).</param>
/// <param name="SpawnX">Spawn x, mm (z is the party spawn line).</param>
/// <param name="MaxHealth">Max health.</param>
public sealed record RosterSlot(string ClassId, ActorRole Role, long SpawnX, long MaxHealth);

/// <summary>One ability-bar slot: the action, its key label and the ability it casts.</summary>
/// <param name="Action">The keybind action.</param>
/// <param name="Key">The key label shown on the bar.</param>
/// <param name="AbilityId">The ability id.</param>
public sealed record AbilitySlot(KeyAction Action, string Key, string AbilityId);
