using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using C = Keyrun.Content;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Tests;

/// <summary>
/// CHARGE-SPEND (ruling R-PILE-7d, finding f-f44791bc; BIBLE section 0 "real decisions on five buttons", section 6
/// forks): the WoW Mythic+ rule for a charged heal. Never let the charges cap: with every charge full the healer spends
/// one (the recharge would otherwise be wasted). Otherwise the spare charge is banked and spent only on a real trigger:
/// the heal target under HealPick.ChargeEmergencyPct (35 percent), or no other heal that reaches the target is ready
/// this GCD. So a banked Encore charge is never pressed back to back on a tank that was just healed. A one-charge
/// ability's choice is unchanged. Before this, the healer pressed Encore's banked Chorus about 30 ticks after the first
/// (hv-k2-s7 at 301 and 331), trading damage and Combo presses for overheal, and ForkParity measured Encore dominated by
/// Round Call.
/// </summary>
public sealed class BankedChargeSpendTests
{
    private const string Chorus = "chanter.chorus";
    private const string Cadence = "chanter.cadence";
    private const string Hymn = "chanter.hymn";

    /// <summary>Encore's Chorus exactly as the talent screen patches it and the sim runs it (2 charges, cooldown 100).</summary>
    private static S.AbilityDef EncoreChorus()
    {
        var reg = C.ContentLoader.Load();
        var option = reg.Classes["chanter"].Talents.First(t => t.AbilityId == Chorus).Options.First(o => o.Id == "encore");
        var def = ForkParityTests.ToSim(C.TalentPicks.ApplyPatch(reg.Abilities[Chorus], option.Patch));
        Assert.Equal(2, def.Charges);
        Assert.Equal(100, def.CooldownTicks);
        return def;
    }

    /// <summary>
    /// The healer at tick 700 with the tank 1000 mm away (inside Chorus's 5000 mm circle, outside Hymn's pool margin
    /// so Hymn is held on cooldown to isolate the Chorus choice), on the Encore run when <paramref name="encore"/>.
    /// </summary>
    private static IReadOnlyList<Command> Decide(bool encore, int? chorusReadyTick, long tankHealth, int? cadenceReadyTick = null)
    {
        var healer = BotTestKit.MakeActor(id: 2, classId: "chanter", role: ActorRole.Healer);
        healer.Cooldowns[Hymn] = 999;
        if (chorusReadyTick is { } c)
        {
            healer.Cooldowns[Chorus] = c;
        }
        if (cadenceReadyTick is { } cd)
        {
            healer.Cooldowns[Cadence] = cd;
        }
        var tank = BotTestKit.MakeActor(id: 1, classId: "warden", role: ActorRole.Tank, x: 1000);
        tank.Health = tankHealth;
        var view = BotTestKit.View(BotTestKit.MakeState(700, tank, healer)) with
        {
            Kits = encore ? RealContent.Kits.ForRun(new Dictionary<string, S.AbilityDef> { [Chorus] = EncoreChorus() }) : RealContent.Kits,
        };
        return HealPick.HealCommands(view, healer, RoleShared.KitOf(view, "chanter"), tank);
    }

    private static string CastOf(IReadOnlyList<Command> commands) => Assert.IsType<CastCommand>(Assert.Single(commands)).AbilityId;

    [Fact]
    public void A_banked_Encore_charge_is_held_for_a_tank_at_65_percent_just_healed()
    {
        // One press from full at 692: one charge banked, the other recharging to 792. The tank sits at 65 percent.
        Assert.Equal(Cadence, CastOf(Decide(encore: true, chorusReadyTick: 792, tankHealth: 65)));
    }

    [Fact]
    public void A_banked_Encore_charge_is_spent_on_a_tank_under_the_emergency_threshold()
    {
        Assert.Equal(Chorus, CastOf(Decide(encore: true, chorusReadyTick: 792, tankHealth: 30)));
    }

    [Fact]
    public void Encore_at_full_charges_spends_one_so_the_recharge_is_not_wasted()
    {
        Assert.Equal(Chorus, CastOf(Decide(encore: true, chorusReadyTick: null, tankHealth: 65)));
        Assert.Equal(Chorus, CastOf(Decide(encore: true, chorusReadyTick: 700, tankHealth: 65)));
    }

    [Fact]
    public void A_banked_Encore_charge_is_spent_when_no_other_heal_reaches_the_target_this_GCD()
    {
        Assert.Equal(Chorus, CastOf(Decide(encore: true, chorusReadyTick: 792, tankHealth: 65, cadenceReadyTick: 999)));
    }

    [Fact]
    public void A_one_charge_Chorus_decision_is_unchanged()
    {
        // Ready: pressed at 65 percent, as before. On cooldown: the targeted heal, as before.
        Assert.Equal(Chorus, CastOf(Decide(encore: false, chorusReadyTick: null, tankHealth: 65)));
        Assert.Equal(Chorus, CastOf(Decide(encore: false, chorusReadyTick: 700, tankHealth: 65)));
        Assert.Equal(Cadence, CastOf(Decide(encore: false, chorusReadyTick: 812, tankHealth: 65)));
        Assert.Equal(Cadence, CastOf(Decide(encore: false, chorusReadyTick: 812, tankHealth: 30)));
    }
}
