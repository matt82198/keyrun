using System.Collections.Generic;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Finding f-80b5cf16 (BIBLE sections 3 and 4, "the Choir's must-kick heal"): an ability whose own gcdTicks is 0 is off
/// the GCD. It neither waits for nor triggers the GCD, in the sim (<see cref="Cooldowns.IsAbilityReady"/>, driven here
/// through <see cref="Rules.Make"/>) and in bot readiness (<see cref="Readiness.AbilityReady(ActorState, string, int, EffectiveKit)"/>, the kit's
/// OffGcdAbilityIds), so a kick lands on the tick it is pressed even while a rotation press holds the GCD.
/// </summary>
public sealed class ZeroGcdOffGcdTests
{
    private const int KickerId = 1;
    private const int CantorId = 2;
    private const int GcdTicks = 15;

    private static readonly AbilityDef Quench = Def("test.quench", 0, 0, new EffectOp[] { new InterruptOp() });
    private static readonly AbilityDef Bolt = Def("test.bolt", 0, GcdTicks, new EffectOp[] { new DamageOp(5, "fire") });
    private static readonly AbilityDef Mending = Def("test.mending", 80, 0, new EffectOp[] { new HealOp(50) }, interruptible: true);

    [Fact]
    public void A_zero_gcd_interrupt_pressed_during_an_active_gcd_lands_on_that_tick()
    {
        var (state, rules) = Arena();
        var kicker = state.Actors[KickerId];
        Tick(state, rules, new Command[] { new CastCommand(0, KickerId, Bolt.Id, CantorId), new CastCommand(0, CantorId, Mending.Id, KickerId) }, _ => { });
        var gcdReady = kicker.GcdReadyTick;
        Assert.True(gcdReady > state.Tick, $"the rotation press holds the GCD (ready {gcdReady}, now {state.Tick})");
        Assert.Equal(Mending.Id, state.Actors[CantorId].CastId);

        var events = new List<SimEvent>();
        var kickTick = state.Tick;
        Tick(state, rules, new Command[] { new CastCommand(kickTick, KickerId, Quench.Id, CantorId) }, events.Add);

        Assert.Contains(events, e => e.Type == "interrupt" && e.Tick == kickTick);
        Assert.Null(state.Actors[CantorId].CastId);
        Assert.Equal(gcdReady, kicker.GcdReadyTick); // the kick does not trigger the GCD either
    }

    [Fact]
    public void An_on_gcd_ability_still_waits_for_the_gcd()
    {
        var (state, rules) = Arena();
        Tick(state, rules, new Command[] { new CastCommand(0, KickerId, Bolt.Id, CantorId) }, _ => { });
        var events = new List<SimEvent>();
        Tick(state, rules, new Command[] { new CastCommand(1, KickerId, Bolt.Id, CantorId) }, events.Add);
        Assert.DoesNotContain(events, e => e.Type == "cast_complete");
    }

    [Fact]
    public void Bot_readiness_treats_the_real_zero_gcd_interrupt_as_off_the_gcd()
    {
        var kit = RealContent.Kits.GetClassKit("cinder");
        Assert.NotNull(kit);
        Assert.Contains("cinder.quench", kit!.OffGcdAbilityIds);
        Assert.DoesNotContain(kit.Rotation[0], kit.OffGcdAbilityIds);
        var effective = EffectiveKit.FromClassKit(kit);

        var self = BotTestKit.MakeActor(id: 3, classId: "cinder");
        self.GcdReadyTick = 110;
        Assert.True(Readiness.AbilityReady(self, "cinder.quench", 100, effective));
        Assert.False(Readiness.AbilityReady(self, kit.Rotation[0], 100, effective));
        Assert.True(Readiness.AbilityReady(self, kit.Rotation[0], 110, effective));
        self.Cooldowns["cinder.quench"] = 105; // off the GCD is not off its own cooldown
        Assert.False(Readiness.AbilityReady(self, "cinder.quench", 100, effective));
    }

    [Fact]
    public void The_kick_ledger_assigns_a_kicker_whose_gcd_is_held()
    {
        var cinder = BotTestKit.MakeActor(id: 3, classId: "cinder");
        cinder.GcdReadyTick = 110;
        var cantor = BotTestKit.MakeActor(id: 9, kind: ActorKind.Enemy, classId: "choir.cantor", role: ActorRole.None, castId: "choir.healCast");
        cantor.CastStartTick = 95;
        cantor.CastEndsTick = 175;
        var view = BotTestKit.View(BotTestKit.MakeState(100, cinder, cantor)) with { Kits = RealContent.Kits };
        var kit = RoleShared.KitOf(view, "cinder");

        var kick = RoleShared.LedgerKickCommand(view, cinder, kit);

        Assert.NotNull(kick);
        Assert.Equal("cinder.quench", kick!.AbilityId);
        Assert.Equal(cantor.Id, kick.TargetId);
    }

    private static (SimState State, StepRules Rules) Arena()
    {
        var abilities = new Dictionary<string, AbilityDef> { [Quench.Id] = Quench, [Bolt.Id] = Bolt, [Mending.Id] = Mending };
        var rules = Rules.Make(new RulesConfig
        {
            Abilities = abilities,
            Effects = new Dictionary<string, EffectDef>(),
            LockoutTicks = 60,
            BaseSpeedMmPerTick = 300,
            ActorRadiusMm = 50,
            Bounds = new Bounds(-10000, 10000, -10000, 10000),
        });
        var kicker = Actors.Create(KickerId, ActorKind.Player, "cinder", ActorRole.Damage, new Vec3(0, 0, 0), 1000, 100, 0);
        var cantor = Actors.Create(CantorId, ActorKind.Enemy, "choir.cantor", ActorRole.None, new Vec3(0, 0, 1000), 1000, 0, 0);
        var state = SimState.Create(1);
        state.Phase = "combat";
        state.Actors[KickerId] = kicker;
        state.Actors[CantorId] = cantor;
        return (state, rules);
    }

    private static void Tick(SimState state, StepRules rules, IReadOnlyList<Command> cmds, System.Action<SimEvent> emit)
    {
        rules(state, cmds, emit);
        state.Tick += 1;
    }

    private static AbilityDef Def(string id, int castTicks, int gcdTicks, IReadOnlyList<EffectOp> effects, bool interruptible = false) => new()
    {
        Id = id,
        CastTicks = castTicks,
        GcdTicks = gcdTicks,
        CooldownTicks = 0,
        Charges = 1,
        Cost = new AbilityCost("none", 0),
        Range = 5000,
        Shape = new AbilityShape(ShapeKind.Target),
        Interruptible = interruptible,
        Effects = effects,
        Tags = null,
    };
}
