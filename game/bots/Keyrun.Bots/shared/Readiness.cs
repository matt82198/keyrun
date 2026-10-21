using System;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// The cooldown readiness check of bots/src/roles/shared.ts <c>abilityReady</c>, re-exported by roles.ts and used
/// by pings-policy.ts and shared/ledger.ts. It lives in shared/ here so pings-policy (lane 3A) and the role state
/// machines (lane 3B) read one definition; roles/shared.ts's other helpers stay with the roles.
/// </summary>
public static class Readiness
{
    /// <summary><c>self.cooldowns[abilityId] ?? 0</c>: the tick the ability's cooldown is ready.</summary>
    /// <param name="self">Actor.</param>
    /// <param name="abilityId">Ability.</param>
    /// <returns>Ready tick (0 when no cooldown is recorded).</returns>
    public static int CooldownReadyTick(ActorState self, string abilityId)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.Cooldowns.TryGetValue(abilityId, out var ready) ? ready : 0;
    }

    /// <summary>roles/shared.ts abilityReady: not mid-cast, off cooldown and off the GCD at <paramref name="tick"/>.</summary>
    /// <param name="self">Actor.</param>
    /// <param name="abilityId">Ability.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>True when castable now (resource cost is not checked).</returns>
    public static bool AbilityReady(ActorState self, string abilityId, int tick) => AbilityReady(self, abilityId, tick, offGcd: false);

    /// <summary>
    /// roles/shared.ts abilityReady with the kit: an ability in <see cref="EffectiveKit.OffGcdAbilityIds"/> (its own
    /// gcdTicks is 0) is off the GCD and only waits for its own cooldown, as the sim's Cooldowns.IsAbilityReady.
    /// </summary>
    /// <param name="self">Actor.</param>
    /// <param name="abilityId">Ability.</param>
    /// <param name="tick">Current tick.</param>
    /// <param name="kit">The actor's resolved kit.</param>
    /// <returns>True when castable now (resource cost is not checked).</returns>
    public static bool AbilityReady(ActorState self, string abilityId, int tick, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        return AbilityReady(self, abilityId, tick, IsOffGcd(kit, abilityId));
    }

    /// <summary>True when <paramref name="abilityId"/>'s own gcdTicks is 0 per <paramref name="kit"/>.</summary>
    /// <param name="kit">The resolved kit.</param>
    /// <param name="abilityId">Ability.</param>
    /// <returns>True when the ability is off the GCD.</returns>
    public static bool IsOffGcd(EffectiveKit kit, string abilityId)
    {
        ArgumentNullException.ThrowIfNull(kit);
        foreach (var id in kit.OffGcdAbilityIds)
        {
            if (string.Equals(id, abilityId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="self"/> is mid-cast at <paramref name="tick"/>: the sim refuses every press then
    /// (Casting.StartError "casting"). A cast ending on <paramref name="tick"/> completes before that tick's commands
    /// run, so it does not block.
    /// </summary>
    /// <param name="self">Actor.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>True while a cast is under way.</returns>
    public static bool IsCasting(ActorState self, int tick)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self.CastId is not null && (self.CastEndsTick ?? int.MaxValue) > tick;
    }

    /// <summary>
    /// True when <paramref name="self"/>'s cast under way still locks it out of a press at <paramref name="tick"/>: it
    /// is casting and more than <see cref="SpellQueue.WindowTicks"/> ticks remain. Inside the last window ticks a press
    /// is queued by the sim and fires on the tick the cast ends (Combat/SpellQueue.cs), so a bot presses there.
    /// Mirrors ledger.ts isCastLocked.
    /// </summary>
    /// <param name="self">Actor.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>True while the cast blocks a press.</returns>
    public static bool IsCastLocked(ActorState self, int tick) =>
        IsCasting(self, tick) && (long)(self.CastEndsTick ?? int.MaxValue) - tick > SpellQueue.WindowTicks;

    /// <summary>
    /// The tick a press by <paramref name="self"/> at <paramref name="tick"/> lands, as far as its own cast goes: now,
    /// or the end of the cast under way when the press is queued behind it.
    /// </summary>
    /// <param name="self">Actor.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>The landing tick.</returns>
    public static int LandingTick(ActorState self, int tick) =>
        IsCasting(self, tick) ? self.CastEndsTick ?? int.MaxValue : tick;

    private static bool AbilityReady(ActorState self, string abilityId, int tick, bool offGcd)
    {
        ArgumentNullException.ThrowIfNull(self);
        return !IsCastLocked(self, tick) &&CooldownReadyTick(self, abilityId) <= tick && (offGcd || self.GcdReadyTick <= tick);
    }
}
