using System.Collections.Generic;

namespace Keyrun.Sim.Combat;

/// <summary>What a cast resolves against (sim/combat/casting.ts:33-37 CastContext), plus the step's Clause hooks.</summary>
/// <param name="Abilities">Abilities by id.</param>
/// <param name="Effects">Effect definitions by id.</param>
/// <param name="LockoutTicks">School lockout length after an interrupt.</param>
public sealed record CastContext(
    IReadOnlyDictionary<string, AbilityDef> Abilities,
    IReadOnlyDictionary<string, EffectDef> Effects,
    int LockoutTicks)
{
    /// <summary>The step's Clause hooks (the TS reads them from clause-hooks.ts module state). Default: no Clause.</summary>
    public ClauseHooks Hooks { get; init; } = new(null);

    /// <summary>
    /// Class id to its Heat/Overheat rule (BIBLE section 5 Cinder), or null for none. Null on every oracle run: the
    /// Overheat is new behaviour with no TS oracle and runs only when a host sets it (<see cref="RulesConfig.Overheat"/>).
    /// </summary>
    public IReadOnlyDictionary<string, OverheatRule>? Overheat { get; init; }

    /// <summary>The Pair's spread and stack mark rules (<see cref="PairMarks"/>) for the mark_applied receipt, or null for none (every oracle run).</summary>
    public MarkRules? Marks { get; init; }
}
