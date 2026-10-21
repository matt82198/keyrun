using System;

namespace Keyrun.Sim.Encounter;

/// <summary>The Lock stack readout a boss unit frame draws (LOCK-STAKES, ruling R-LOCK-1).</summary>
/// <param name="Stacks">Tempo stacks the boss carries from failed Locks, 1..<paramref name="MaxStacks"/>.</param>
/// <param name="MaxStacks">Stacks at which the boss is enraged (<see cref="BossConfig.FailsToEnrage"/>).</param>
/// <param name="TempoBp">The boss's effective loop tempo at these stacks, basis points (10000 = base pace).</param>
/// <param name="Text">The frame label, <c>LOCK xN/M</c>.</param>
public sealed record BossLockStacks(int Stacks, int MaxStacks, int TempoBp, string Text);

/// <summary>
/// Read-only selector for the HUD (LOCK-STAKES). Nothing in the sim steps through here: it only names the Lock stacks
/// the armed boss's frame shows. Display only. Separate from <see cref="BossBreakBarReadout"/>: the Break bar is the
/// Twins' bar, never a Lock meter.
/// </summary>
public static class BossLockReadout
{
    /// <summary>The Lock stacks <paramref name="actorId"/>'s frame shows, or null (not the armed boss, or no failed Lock).</summary>
    /// <param name="encounter">The encounter state.</param>
    /// <param name="actorId">The framed enemy.</param>
    /// <returns>The readout, or null.</returns>
    public static BossLockStacks? For(EncounterState encounter, int actorId)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        if (encounter.Boss is not { } boss || boss.Config.BossActorId != actorId || boss.LockFails <= 0)
        {
            return null;
        }
        var max = boss.Config.FailsToEnrage;
        var stacks = Math.Min(boss.LockFails, max);
        var tempoBp = boss.Enraged ? boss.Config.EnrageTempoBp : BossRules.LockTempoBp(boss.Config, boss.LockFails);
        return new BossLockStacks(stacks, max, tempoBp, FormattableString.Invariant($"LOCK x{stacks}/{max}"));
    }
}
