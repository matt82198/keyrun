using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane KF1-FACING (demo verdict KF1, "combat animation reads badly"; Matt 2026-10-20: "they face a random direction
/// and swing the air"). An instant swing starts and completes inside one step, so <see cref="ActorState.CastId"/> is
/// never set on a drawn state and <see cref="CastFacing.RenderYaw(SimState, ActorState, IReadOnlyDictionary{string, AbilityDef}?)"/>
/// drew the swing at the movement facing. <see cref="SwingFacing"/> keeps the struck target as a render-only hold: the
/// swinger is drawn facing it from the resolve tick until it moves, its target dies, or it resolves a cone, line or self
/// ability. Every case drives the real rules (<see cref="Rules.Make"/>).
/// </summary>
public class SwingFacingTests
{
    private const int CasterId = 1;
    private const int TargetId = 2;
    private const int OtherId = 3;
    private const int Backwards = 768; // facing -z; the victim stands at +z (yaw 256)
    private const int TowardTarget = 256;

    private static readonly AbilityDef Punch = Def("test.punch", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(5, "physical") });
    private static readonly AbilityDef Sweep = Def("test.sweep", new AbilityShape(ShapeKind.Cone, 3000, 256), new EffectOp[] { new DamageOp(5, "physical") });
    private static readonly AbilityDef Shout = Def("test.shout", new AbilityShape(ShapeKind.Self), new EffectOp[] { new HealOp(1) });
    private static readonly AbilityDef Bolt = Def("test.bolt", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(5, "fire") }, castTicks: 20);
    private static readonly AbilityDef Cleave = Def("test.cleave", new AbilityShape(ShapeKind.Cone, 3000, 256), new EffectOp[] { new DamageOp(5, "physical") }, castTicks: 20);

    [Fact]
    public void AnInstantSwingIsDrawnFacingItsVictim()
    {
        var (state, abilities, hold) = SwingAt(Punch.Id);

        var caster = state.Actors[CasterId];
        Assert.Null(caster.CastId); // the instant swing resolved inside the step
        Assert.Equal(Backwards, caster.Yaw); // the sim facing is untouched
        Assert.Equal(Backwards, CastFacing.RenderYaw(state, caster, abilities)); // the old rule draws it swinging the air
        Assert.NotNull(hold);
        Assert.Equal(TowardTarget, SwingFacing.RenderYaw(state, caster, abilities, hold));
    }

    [Fact]
    public void TheHoldLastsWhileTheSwingerStandsStill()
    {
        var (state, abilities, hold, rules) = SwingAtWithRules(Punch.Id);
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(), events.Add);

        hold = SwingFacing.After(state, state.Actors[CasterId], events, abilities, hold);
        Assert.Equal(TowardTarget, SwingFacing.RenderYaw(state, state.Actors[CasterId], abilities, hold));
    }

    [Fact]
    public void MovingReleasesTheHold()
    {
        var (state, abilities, hold) = SwingAt(Punch.Id);
        var caster = state.Actors[CasterId];
        caster.Pos = new Vec3(caster.Pos.X - 400, caster.Pos.Y, caster.Pos.Z);

        hold = SwingFacing.After(state, caster, new List<SimEvent>(), abilities, hold);
        Assert.Null(hold);
        Assert.Equal(Backwards, SwingFacing.RenderYaw(state, caster, abilities, hold));
    }

    [Fact]
    public void ADeadVictimReleasesTheHold()
    {
        var (state, abilities, hold) = SwingAt(Punch.Id);
        state.Actors[TargetId].Alive = false;

        Assert.Equal(Backwards, SwingFacing.RenderYaw(state, state.Actors[CasterId], abilities, hold));
        Assert.Null(SwingFacing.After(state, state.Actors[CasterId], new List<SimEvent>(), abilities, hold));
    }

    [Fact]
    public void AConeSwingKeepsTheSimFacingAndClearsAnEarlierHold()
    {
        var (state, abilities, hold, rules) = SwingAtWithRules(Punch.Id);
        Assert.NotNull(hold);
        state.Actors[CasterId].GcdReadyTick = 0;
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(CasterId, Sweep.Id, TargetId)), events.Add);
        Assert.Contains(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == Sweep.Id);

        hold = SwingFacing.After(state, state.Actors[CasterId], events, abilities, hold);
        Assert.Null(hold); // the cone resolved along Yaw: drawing any other facing misreports where it landed
        Assert.Equal(Backwards, SwingFacing.RenderYaw(state, state.Actors[CasterId], abilities, hold));
    }

    [Fact]
    public void ASelfAbilityMakesNoHold()
    {
        var (state, abilities, hold) = SwingAt(Shout.Id);

        Assert.Null(hold);
        Assert.Equal(Backwards, SwingFacing.RenderYaw(state, state.Actors[CasterId], abilities, hold));
    }

    [Fact]
    public void AnotherActorsSwingMakesNoHold()
    {
        var abilities = AbilityMap(Punch, Sweep, Shout, Bolt, Cleave);
        var state = Arena(abilities, out var rules);
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(OtherId, Punch.Id, TargetId)), events.Add);
        Assert.Contains(events, e => e.Type == "cast_complete" && Int(e, "actorId") == OtherId);

        Assert.Null(SwingFacing.After(state, state.Actors[CasterId], events, abilities, null));
    }

    [Fact]
    public void AnAimedCastStillFacesItsCastTargetAndAConeCastItsSimFacing()
    {
        var (state, abilities, hold, rules) = SwingAtWithRules(Punch.Id);
        var other = state.Actors[OtherId];
        other.Pos = new Vec3(-1000, 0, 0); // yaw 512 from the caster
        state.Actors[CasterId].GcdReadyTick = 0;
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(CasterId, Bolt.Id, OtherId)), events.Add);
        hold = SwingFacing.After(state, state.Actors[CasterId], events, abilities, hold);

        Assert.Equal(Bolt.Id, state.Actors[CasterId].CastId);
        Assert.Equal(512, SwingFacing.RenderYaw(state, state.Actors[CasterId], abilities, hold)); // the bolt's target

        var (coneState, coneAbilities, coneHold, coneRules) = SwingAtWithRules(Punch.Id);
        coneState.Actors[CasterId].GcdReadyTick = 0;
        Tick(coneState, coneRules, Cmds(Cast(CasterId, Cleave.Id, TargetId)), _ => { });
        Assert.Equal(Cleave.Id, coneState.Actors[CasterId].CastId);
        Assert.Equal(Backwards, SwingFacing.RenderYaw(coneState, coneState.Actors[CasterId], coneAbilities, coneHold));
    }

    private static (SimState State, IReadOnlyDictionary<string, AbilityDef> Abilities, FacingHold? Hold) SwingAt(string abilityId)
    {
        var (state, abilities, hold, _) = SwingAtWithRules(abilityId);
        return (state, abilities, hold);
    }

    private static (SimState State, IReadOnlyDictionary<string, AbilityDef> Abilities, FacingHold? Hold, StepRules Rules) SwingAtWithRules(string abilityId)
    {
        var abilities = AbilityMap(Punch, Sweep, Shout, Bolt, Cleave);
        var state = Arena(abilities, out var rules);
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(CasterId, abilityId, TargetId)), events.Add);
        Assert.Contains(events, e => e.Type == "cast_complete" && Int(e, "actorId") == CasterId && Str(e, "abilityId") == abilityId);
        return (state, abilities, SwingFacing.After(state, state.Actors[CasterId], events, abilities, null), rules);
    }

    private static SimState Arena(IReadOnlyDictionary<string, AbilityDef> abilities, out StepRules rules)
    {
        rules = Rules.Make(Config(abilities));
        var caster = Make(CasterId, ActorKind.Player, "caster", maxHealth: 1000, maxResource: 100, yaw: Backwards);
        caster.Resource = 100;
        var target = Make(TargetId, ActorKind.Enemy, "target", z: 1000, maxHealth: 1000, yaw: Backwards);
        var other = Make(OtherId, ActorKind.Player, "other", x: 1000, maxHealth: 1000, maxResource: 100, yaw: Backwards);
        other.Resource = 100;
        return At(0, caster, target, other);
    }
}
