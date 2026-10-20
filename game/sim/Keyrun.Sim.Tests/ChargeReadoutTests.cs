using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane CHARGE-COOLDOWN: the ability bar's cooldown number for a multi-charge ability is the wait until the NEXT charge,
/// matching when the sim lets the ability be cast again, not the wait until the last charge returns.
/// The fixture mirrors warden.charge (content/data/abilities/warden.json: cooldownTicks 200, charges 2).
/// </summary>
public sealed class ChargeReadoutTests
{
    private static AbilityDef Charge(int cooldownTicks = 200, long charges = 2) =>
        Ability(new AbilityShape(ShapeKind.Line), new EffectOp[] { new InterruptOp() }, id: "warden.charge", gcdTicks: 0, cooldownTicks: cooldownTicks, charges: charges);

    private static ActorState P() => Actor(1, ActorKind.Player, maxResource: 100, role: ActorRole.Tank);

    private static int TenthsOfSecond(int ticks) => ticks * 10 / Tick.TickHz;

    [Fact]
    public void Two_casts_show_the_wait_until_the_next_charge_not_the_last()
    {
        var actor = P();
        var def = Charge();
        Cooldowns.ConsumeCharge(actor, def, 0);
        Cooldowns.ConsumeCharge(actor, def, 10);

        var readout = ChargeReadout.For(actor, def, 10);

        Assert.Equal(0, readout.Charges);
        Assert.True(readout.OnCooldown);
        Assert.Equal(190, readout.TicksToNextCharge);
        Assert.Equal(95, TenthsOfSecond(readout.TicksToNextCharge)); // 9.5 s, not (400 - 10) / 20 = 19.5 s
        // The sim agrees: castable exactly when the readout reaches zero, not before.
        Assert.False(Cooldowns.IsAbilityReady(actor, def, 10 + readout.TicksToNextCharge - 1));
        Assert.True(Cooldowns.IsAbilityReady(actor, def, 10 + readout.TicksToNextCharge));
    }

    [Fact]
    public void Two_casts_then_the_first_charge_returns_and_the_second_counts_down_a_full_cooldown()
    {
        var actor = P();
        var def = Charge();
        Cooldowns.ConsumeCharge(actor, def, 0);
        Cooldowns.ConsumeCharge(actor, def, 10);

        var at200 = ChargeReadout.For(actor, def, 200);
        Assert.Equal(1, at200.Charges);
        Assert.False(at200.OnCooldown);
        Assert.Equal(200, at200.TicksToNextCharge);

        var at399 = ChargeReadout.For(actor, def, 399);
        Assert.Equal(1, at399.Charges);
        Assert.Equal(1, at399.TicksToNextCharge);

        var at400 = ChargeReadout.For(actor, def, 400);
        Assert.Equal(2, at400.Charges);
        Assert.Equal(0, at400.TicksToNextCharge);
    }

    [Fact]
    public void One_cast_leaves_one_charge_ready_and_the_slot_usable()
    {
        var actor = P();
        var def = Charge();
        Cooldowns.ConsumeCharge(actor, def, 0);

        var readout = ChargeReadout.For(actor, def, 50);

        Assert.Equal(1, readout.Charges);
        Assert.Equal(2, readout.MaxCharges);
        Assert.False(readout.OnCooldown);
        Assert.Equal(150, readout.TicksToNextCharge);
        Assert.True(Cooldowns.IsAbilityReady(actor, def, 50));
    }

    [Fact]
    public void A_single_charge_ability_counts_down_its_whole_cooldown_unchanged()
    {
        var actor = P();
        var def = Charge(cooldownTicks: 120, charges: 1);
        Cooldowns.ConsumeCharge(actor, def, 30);

        var readout = ChargeReadout.For(actor, def, 40);

        Assert.Equal(0, readout.Charges);
        Assert.True(readout.OnCooldown);
        Assert.Equal(110, readout.TicksToNextCharge); // readyTick 150 - now 40, as before
        Assert.False(ChargeReadout.For(actor, def, 150).OnCooldown);
    }

    [Fact]
    public void Full_charges_and_no_cooldown_abilities_read_ready_with_no_countdown()
    {
        var actor = P();
        Assert.Equal(new ChargeReadout(2, 2, 0, 200), ChargeReadout.For(actor, Charge(), 0));
        var free = Charge(cooldownTicks: 0, charges: 1);
        Cooldowns.ConsumeCharge(actor, free, 0);
        Assert.Equal(new ChargeReadout(1, 1, 0, 0), ChargeReadout.For(actor, free, 0));
    }
}
