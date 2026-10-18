using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// Port of sim/combat/cooldowns.ts: GCD and charge cooldowns with a chained recharge timestamp.
/// <c>cooldowns[abilityId]</c> holds the tick the currently recharging charge finishes; spending another charge
/// while below full pushes it forward by cooldownTicks.
/// </summary>
public static class Cooldowns
{
    /// <summary>sim/combat/cooldowns.ts:12-14 isGcdReady: <c>now &gt;= gcdReadyTick</c>.</summary>
    /// <param name="actor">The actor.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>True when the GCD is ready.</returns>
    public static bool IsGcdReady(ActorState actor, int now) => now >= actor.GcdReadyTick;

    /// <summary>sim/combat/cooldowns.ts:16-19 startGcd: a non-positive length is a no-op (:17).</summary>
    /// <param name="actor">The actor.</param>
    /// <param name="now">Current tick.</param>
    /// <param name="gcdTicks">GCD length.</param>
    public static void StartGcd(ActorState actor, int now, int gcdTicks)
    {
        if (gcdTicks <= 0)
        {
            return;
        }
        actor.GcdReadyTick = now + gcdTicks;
    }

    /// <summary>
    /// sim/combat/cooldowns.ts:21-28 chargesAvailable: full when cooldownTicks is 0 or the timestamp has passed;
    /// otherwise <c>max(0, charges - ceil(remaining / cooldownTicks))</c> (Math.ceil, :26: CeilDiv).
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="def">The ability.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>Charges ready now.</returns>
    public static long ChargesAvailable(ActorState actor, AbilityDef def, int now)
    {
        if (def.CooldownTicks == 0)
        {
            return def.Charges;
        }
        var nextReady = actor.Cooldowns.TryGetValue(def.Id, out var r) ? r : 0;
        if (now >= nextReady)
        {
            return def.Charges;
        }
        var ticksRemaining = nextReady - now;
        var chargesOnCooldown = IntMath.CeilDiv(ticksRemaining, def.CooldownTicks);
        return Math.Max(0, def.Charges - chargesOnCooldown);
    }

    /// <summary>
    /// sim/combat/cooldowns.ts:30-35 consumeCharge: <c>cooldowns[id] = max-of(now, timestamp) + cooldownTicks</c>;
    /// no entry is written when cooldownTicks is 0 (:31).
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="def">The ability.</param>
    /// <param name="now">Current tick.</param>
    public static void ConsumeCharge(ActorState actor, AbilityDef def, int now)
    {
        if (def.CooldownTicks == 0)
        {
            return;
        }
        var nextReady = actor.Cooldowns.TryGetValue(def.Id, out var r) ? r : 0;
        var baseTick = now >= nextReady ? now : nextReady;
        actor.Cooldowns[def.Id] = baseTick + def.CooldownTicks;
    }

    /// <summary>sim/combat/cooldowns.ts:37-39 isAbilityReady: GCD ready and at least one charge.</summary>
    /// <param name="actor">The actor.</param>
    /// <param name="def">The ability.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>True when castable now (cooldown-wise).</returns>
    public static bool IsAbilityReady(ActorState actor, AbilityDef def, int now) =>
        IsGcdReady(actor, now) && ChargesAvailable(actor, def, now) >= 1;
}
