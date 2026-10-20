using System.Collections.Generic;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
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
    private const int ChannelTicks = 30;
    private static readonly AbilityDef Channel = Def("test.channel", ChannelTicks, GcdTicks, new EffectOp[] { new DamageOp(5, "fire") });
    private static readonly AbilityDef Mending =Def("test.mending", 80, 0, new EffectOp[] { new HealOp(50) }, interruptible: true);

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

    [Fact]
    public void A_zero_gcd_instant_pressed_during_its_own_cast_is_refused_and_the_cast_completes()
    {
        var (state, rules) = Arena();
        var kicker = state.Actors[KickerId];
        var events = new List<SimEvent>();
        Tick(state, rules, new Command[] { new CastCommand(0, KickerId, Channel.Id, CantorId), new CastCommand(0, CantorId, Mending.Id, KickerId) }, events.Add);
        Assert.Equal(Channel.Id, kicker.CastId);

        Tick(state, rules, new Command[] { new CastCommand(1, KickerId, Quench.Id, CantorId) }, events.Add);

        Assert.DoesNotContain(events, e => e.Type == "interrupt");
        Assert.DoesNotContain(events, e => e.Type == "cast_stopped");
        Assert.Equal(Mending.Id, state.Actors[CantorId].CastId);
        Assert.Equal(Channel.Id, kicker.CastId); // the press did not replace the cast under way
        while (state.Tick <= ChannelTicks)
        {
            Tick(state, rules, System.Array.Empty<Command>(), events.Add);
        }
        Assert.Contains(events, e => e.Type == "cast_complete" && e.Tick == ChannelTicks && ((JsString)e.Get("abilityId")!).Value == Channel.Id);
        Assert.DoesNotContain(events, e => e.Type == "cast_stopped");
    }

    [Fact]
    public void An_on_gcd_ability_pressed_after_the_gcd_but_during_its_own_cast_is_refused()
    {
        var (state, rules) = Arena();
        var kicker = state.Actors[KickerId];
        var events = new List<SimEvent>();
        Tick(state, rules, new Command[] { new CastCommand(0, KickerId, Channel.Id, CantorId) }, events.Add);
        while (state.Tick < GcdTicks + 1)
        {
            Tick(state, rules, System.Array.Empty<Command>(), events.Add);
        }
        Assert.True(kicker.GcdReadyTick <= state.Tick, "the GCD is over; only the cast under way blocks");

        Tick(state, rules, new Command[] { new CastCommand(state.Tick, KickerId, Bolt.Id, CantorId) }, events.Add);

        Assert.Equal(Channel.Id, kicker.CastId);
        Assert.DoesNotContain(events, e => e.Type == "cast_complete" && ((JsString)e.Get("abilityId")!).Value == Bolt.Id);
    }

    [Fact]
    public void Bot_readiness_says_not_ready_while_the_bot_is_casting()
    {
        var kit = RealContent.Kits.GetClassKit("cinder")!;
        var effective = EffectiveKit.FromClassKit(kit);
        var self = BotTestKit.MakeActor(id: 3, classId: "cinder", castId: kit.Rotation[0]);
        self.GcdReadyTick = 0;
        Assert.False(Readiness.AbilityReady(self, "cinder.quench", 100, effective));
        Assert.False(Readiness.AbilityReady(self, kit.Rotation[1], 100, effective));
        Assert.False(Readiness.AbilityReady(self, kit.Rotation[1], 100));
        self.CastId = null;
        Assert.True(Readiness.AbilityReady(self, "cinder.quench", 100, effective));
    }

    [Fact]
    public void The_kick_ledger_never_assigns_a_kicker_who_is_casting()
    {
        var cinder = BotTestKit.MakeActor(id: 3, classId: "cinder", castId: RealContent.Kits.GetClassKit("cinder")!.Rotation[0]);
        var cantor = BotTestKit.MakeActor(id: 9, kind: ActorKind.Enemy, classId: "choir.cantor", role: ActorRole.None, castId: "choir.healCast");
        cantor.CastStartTick = 95;
        cantor.CastEndsTick = 175;
        var view = BotTestKit.View(BotTestKit.MakeState(100, cinder, cantor)) with { Kits = RealContent.Kits };

        Assert.Null(RoleShared.LedgerKickCommand(view, cinder, RoleShared.KitOf(view, "cinder")));
    }

    [Fact]
    public void A_facet_that_zeroes_an_abilitys_gcd_makes_bot_readiness_treat_it_off_the_gcd()
    {
        var baseKit = RealContent.Kits.GetClassKit("cinder")!;
        var rot = baseKit.Rotation[0];
        Assert.DoesNotContain(rot, baseKit.OffGcdAbilityIds);
        var run = new Dictionary<string, AbilityDef>
        {
            [rot] = new GearPatch { GcdTicks = 0 }.ApplyTo(Def(rot, 0, GcdTicks, new EffectOp[] { new DamageOp(5, "fire") })),
            ["cinder.quench"] = Def("cinder.quench", 0, GcdTicks, new EffectOp[] { new InterruptOp() }),
        };

        var kit = RealContent.Kits.ForRun(run).ResolveKit("cinder", ClassKitView.Empty);

        Assert.Contains(rot, kit.OffGcdAbilityIds);
        Assert.DoesNotContain("cinder.quench", kit.OffGcdAbilityIds); // the run's def is authoritative both ways
        var self = BotTestKit.MakeActor(id: 3, classId: "cinder");
        self.GcdReadyTick = 110;
        Assert.True(Readiness.AbilityReady(self, rot, 100, kit));
        Assert.False(Readiness.AbilityReady(self, "cinder.quench", 100, kit));
    }

    private static (SimState State, StepRules Rules) Arena()
    {
        var abilities = new Dictionary<string, AbilityDef> { [Quench.Id] = Quench, [Bolt.Id] = Bolt, [Mending.Id] = Mending, [Channel.Id] = Channel };
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
