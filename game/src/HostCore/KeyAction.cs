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

/// <summary>A keybind action (client/src/input/keybinds.ts KeybindAction).</summary>
public enum KeyAction
{
    /// <summary>Rotational slot 1.</summary>
    Ability1,

    /// <summary>Rotational slot 2.</summary>
    Ability2,

    /// <summary>Rotational slot 3.</summary>
    Ability3,

    /// <summary>Rotational slot 4.</summary>
    Ability4,

    /// <summary>Rotational slot 5.</summary>
    Ability5,

    /// <summary>Interrupt.</summary>
    AbilityQ,

    /// <summary>Defensive.</summary>
    AbilityE,

    /// <summary>Class utility.</summary>
    AbilityR,

    /// <summary>Movement ability.</summary>
    AbilityF,

    /// <summary>Cycle the hostile target.</summary>
    TargetCycle,

    /// <summary>Ping a target or spot.</summary>
    Ping,
}
