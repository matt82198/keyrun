using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// What the ability bar shows for one ability's cooldown (lane CHARGE-COOLDOWN): the charges ready now and the ticks
/// until the NEXT charge returns. Built from the same chained recharge timestamp the sim reads
/// (<see cref="Cooldowns"/>: <c>cooldowns[id]</c> is the tick the LAST spent charge finishes), so the number the
/// player sees is the wait until the ability is castable again, not the wait until every charge is back.
/// </summary>
/// <param name="Charges">Charges ready now (<see cref="Cooldowns.ChargesAvailable"/>).</param>
/// <param name="MaxCharges">The ability's charge count.</param>
/// <param name="TicksToNextCharge">Ticks until one more charge is ready; 0 when every charge is ready.</param>
/// <param name="CooldownTicks">One charge's recharge length (0 for an ability with no cooldown).</param>
public readonly record struct ChargeReadout(long Charges, long MaxCharges, int TicksToNextCharge, int CooldownTicks)
{
    /// <summary>True when no charge is ready, so the slot reads as on cooldown.</summary>
    public bool OnCooldown => Charges < 1 && TicksToNextCharge > 0;

    /// <summary>
    /// The readout for <paramref name="def"/> on <paramref name="actor"/> at tick <paramref name="now"/>. With the
    /// timestamp <c>T</c> and <c>r = T - now &gt; 0</c>, the sim gains a charge each time <c>ceil(r / cd)</c> drops,
    /// i.e. when <c>r</c> falls to the next lower multiple of <c>cd</c>, so the next charge is <c>((r - 1) mod cd) + 1</c>
    /// ticks away (cooldowns.ts:21-28 chargesAvailable).
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="def">The ability.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>The readout.</returns>
    public static ChargeReadout For(ActorState actor, AbilityDef def, int now)
    {
        var charges = Cooldowns.ChargesAvailable(actor, def, now);
        if (def.CooldownTicks <= 0 || !actor.Cooldowns.TryGetValue(def.Id, out var readyTick) || readyTick <= now)
        {
            return new ChargeReadout(charges, def.Charges, 0, def.CooldownTicks);
        }
        var remaining = readyTick - now;
        var next = ((remaining - 1) % def.CooldownTicks) + 1;
        return new ChargeReadout(charges, def.Charges, next, def.CooldownTicks);
    }
}
