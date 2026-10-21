using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using C = Keyrun.Content;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Tests;

/// <summary>
/// BANKED-CHARGE (ruling R-PILE-7c, finding f-f44791bc; BIBLE section 6 "real decisions", section 7 "Facets change
/// function"): a multi-charge ability is castable while one charge is banked, in the sim
/// (<see cref="S.Cooldowns.ChargesAvailable"/>: <c>cooldowns[id]</c> is the tick the recharging charge finishes, not the
/// tick the button is usable) and so in bot readiness (<see cref="Readiness.AbilityReady(ActorState, string, int, EffectiveKit)"/>,
/// the kit's ChargedAbilities). Before this, the bots read <c>cooldowns[id] &lt;= tick</c>
/// alone, so Chorus "Encore" ("Gains a second charge") and the Facet "Chorus grants an extra charge" never gave a bot a
/// second press: the parity check measured Encore identical to Round Call in every Hollow Vault run.
/// </summary>
public sealed class BankedChargeReadinessTests
{
    private const string Chorus = "chanter.chorus";

    /// <summary>
    /// A two-charge Encore Chorus (cooldown 100): Encore as the talent screen patches it, plus the second charge the ring
    /// Facet chanter.ring.versedReserves ("Chorus grants an extra charge", charges 2) gives. R-ENCORE-1 took the second
    /// charge off Encore itself (it now trades it for a floor Window), so the Facet is how a Chanter banks a Chorus.
    /// </summary>
    private static S.AbilityDef EncoreChorus()
    {
        var reg = C.ContentLoader.Load();
        var option = reg.Classes["chanter"].Talents.First(t => t.AbilityId == Chorus).Options.First(o => o.Id == "encore");
        var talented = ForkParityTests.ToSim(C.TalentPicks.ApplyPatch(reg.Abilities[Chorus], option.Patch));
        Assert.Equal(1, talented.Charges);
        var def = talented with { Charges = 2 };
        Assert.Equal(100, def.CooldownTicks);
        return def;
    }

    private static EffectiveKit EncoreKit() =>
        RealContent.Kits.ForRun(new Dictionary<string, S.AbilityDef> { [Chorus] = EncoreChorus() }).ResolveKit("chanter", ClassKitView.Empty);

    [Fact]
    public void A_bot_sees_the_banked_second_charge_of_a_talented_two_charge_ability()
    {
        var kit = EncoreKit();
        var self = BotTestKit.MakeActor(id: 2, classId: "chanter", role: ActorRole.Healer);

        // One press from full at tick 692: the recharging charge finishes at 792, the second is banked.
        self.Cooldowns[Chorus] = 792;
        Assert.True(Readiness.AbilityReady(self, Chorus, 700, kit), "one charge is banked: the sim would cast it");

        // Two presses at 692: the second finishes at 892, so no charge until 792.
        self.Cooldowns[Chorus] = 892;
        Assert.False(Readiness.AbilityReady(self, Chorus, 700, kit), "both charges spent");
        Assert.True(Readiness.AbilityReady(self, Chorus, 792, kit), "the first recharge lands at 792");
    }

    [Fact]
    public void A_one_charge_ability_keeps_waiting_for_its_cooldown()
    {
        var kit = EffectiveKit.FromClassKit(RealContent.Kits.GetClassKit("chanter")!);
        var self = BotTestKit.MakeActor(id: 2, classId: "chanter", role: ActorRole.Healer);
        self.Cooldowns[Chorus] = 812;
        Assert.False(Readiness.AbilityReady(self, Chorus, 700, kit));
        Assert.True(Readiness.AbilityReady(self, Chorus, 812, kit));
    }

    [Fact]
    public void A_content_two_charge_ability_is_ready_with_one_charge_banked()
    {
        var classKit = RealContent.Kits.GetClassKit("chanter")!;
        var kit = EffectiveKit.FromClassKit(classKit);
        var self = BotTestKit.MakeActor(id: 2, classId: "chanter", role: ActorRole.Healer);
        // chanter.glide: 2 charges, cooldown 200. One press at tick 100 from full: recharge at 300.
        self.Cooldowns["chanter.glide"] = 300;
        Assert.True(Readiness.AbilityReady(self, "chanter.glide", 200, kit));
        self.Cooldowns["chanter.glide"] = 500;
        Assert.False(Readiness.AbilityReady(self, "chanter.glide", 200, kit));
    }

    [Fact]
    public void The_healer_spends_the_banked_Encore_charge_on_a_hurt_ally_in_its_Chorus_radius()
    {
        var run = new Dictionary<string, S.AbilityDef> { [Chorus] = EncoreChorus() };
        var healer = BotTestKit.MakeActor(id: 2, classId: "chanter", role: ActorRole.Healer);
        healer.Cooldowns[Chorus] = 792;
        var tank = BotTestKit.MakeActor(id: 1, classId: "warden", role: ActorRole.Tank, x: 1000);
        // R-PILE-7d: a banked charge is held for a tank at 53%; 30% is the emergency case
        tank.Health = 30;
        var view = BotTestKit.View(BotTestKit.MakeState(700, tank, healer)) with { Kits = RealContent.Kits.ForRun(run) };

        var commands = HealPick.HealCommands(view, healer, RoleShared.KitOf(view, "chanter"), tank);

        var cast = Assert.IsType<CastCommand>(Assert.Single(commands));
        Assert.Equal(Chorus, cast.AbilityId);
    }
}
