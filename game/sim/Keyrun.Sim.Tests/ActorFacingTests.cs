using System.Collections.Generic;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Finding f-e0650754 (Matt 2026-10-22: "they often just look backwards in enemy packs and friendly bots ... they should
/// be visibly aggrod onto a player"). <see cref="ActorFacing"/> is the view's yaw rule: an actor with a live target faces
/// it from its position to the target's position while casting, attacking or merely aggroed; with no target it keeps the
/// sim facing (movement direction while moving, last facing while still). Cases drive the real rules.
/// </summary>
public class ActorFacingTests
{
    private const int CasterId = 1;
    private const int TargetId = 2;
    private const int Backwards = 768; // facing -z; the target stands at +z (yaw 256)
    private const int TowardTarget = 256;

    private static readonly AbilityDef Bolt = Def("test.bolt", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(5, "fire") }, castTicks: 20);
    private static readonly AbilityDef Cleave = Def("test.cleave", new AbilityShape(ShapeKind.Cone, 3000, 256), new EffectOp[] { new DamageOp(5, "physical") }, castTicks: 20);

    [Fact]
    public void YawTowardFacesATargetBehindTheActor()
    {
        Assert.Equal(TowardTarget, ActorFacing.YawToward(new Vec3(0, 0, 0), new Vec3(0, 0, 1000), Backwards));
        Assert.Equal(768, ActorFacing.YawToward(new Vec3(0, 0, 0), new Vec3(0, 0, -1000), TowardTarget));
        Assert.Equal(0, ActorFacing.YawToward(new Vec3(0, 0, 0), new Vec3(1000, 0, 0), Backwards));
        Assert.Equal(512, ActorFacing.YawToward(new Vec3(0, 0, 0), new Vec3(-1000, 0, 0), Backwards));
    }

    [Fact]
    public void YawTowardKeepsTheLastYawOnTheSameSpot()
    {
        Assert.Equal(Backwards, ActorFacing.YawToward(new Vec3(5, 0, 5), new Vec3(5, 9, 5), Backwards));
    }

    [Fact]
    public void AnAggroedActorBetweenAttacksFacesItsTarget()
    {
        var (state, abilities) = Arena(Bolt);
        var caster = state.Actors[CasterId];
        caster.TargetId = TargetId;

        Assert.Null(caster.CastId); // not casting, not swinging: aggroed only
        Assert.Equal(Backwards, caster.Yaw); // the sim facing is stale
        Assert.Equal(TowardTarget, ActorFacing.RenderYaw(state, caster, abilities, null, false));
        Assert.Equal(Backwards, CastFacing.RenderYaw(state, caster, abilities)); // the old rule left it facing away
    }

    [Fact]
    public void AnAggroedActorStillFacesItsTargetAfterMoving()
    {
        var (state, abilities) = Arena(Bolt);
        var caster = state.Actors[CasterId];
        caster.TargetId = TargetId;
        caster.Pos = new Vec3(400, 0, 0); // chased sideways; the old swing hold released on any move

        var expected = Fixed.YawBetween(caster.Pos, state.Actors[TargetId].Pos);
        Assert.Equal(expected, ActorFacing.RenderYaw(state, caster, abilities, null, false));
        Assert.NotEqual(Backwards, expected);
    }

    [Fact]
    public void NoTargetStillKeepsTheSimFacing()
    {
        var (state, abilities) = Arena(Bolt);
        var caster = state.Actors[CasterId];

        Assert.Null(caster.TargetId);
        Assert.Equal(Backwards, ActorFacing.RenderYaw(state, caster, abilities, null, false)); // stationary: last facing
        caster.Pos = new Vec3(300, 0, 0);
        caster.Yaw = 0; // the sim turned it to its movement direction
        Assert.Equal(0, ActorFacing.RenderYaw(state, caster, abilities, null, false)); // moving: movement direction
    }

    [Fact]
    public void ADeadTargetReleasesTheFacing()
    {
        var (state, abilities) = Arena(Bolt);
        var caster = state.Actors[CasterId];
        caster.TargetId = TargetId;
        state.Actors[TargetId].Alive = false;

        Assert.Equal(Backwards, ActorFacing.RenderYaw(state, caster, abilities, null, false));
    }

    [Fact]
    public void TheYawStaysLockedOnTheTargetOnEveryTickOfACast()
    {
        var (state, abilities) = Arena(Bolt);
        var rules = Rules.Make(Config(abilities));
        var caster = state.Actors[CasterId];
        caster.TargetId = TargetId;
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(CasterId, Bolt.Id, TargetId)), events.Add);
        Assert.NotNull(caster.CastId);

        var sawCast = 0;
        for (var i = 0; i < 40 && caster.CastId is not null; i++)
        {
            // the target drifts around the caster; the caster's sim Yaw stays stale the whole time
            var target = state.Actors[TargetId];
            target.Pos = new Vec3(i % 2 == 0 ? 600 : -600, 0, 1000);
            caster.Yaw = Backwards;
            Assert.Equal(
                Fixed.YawBetween(caster.Pos, target.Pos),
                ActorFacing.RenderYaw(state, caster, abilities, null, false));
            sawCast++;
            Tick(state, rules, Cmds(), events.Add);
        }
        Assert.True(sawCast >= 15, $"the cast ran {sawCast} ticks");
    }

    [Fact]
    public void AConeCastKeepsTheSimFacingBecauseItResolvesAlongYaw()
    {
        var abilities = AbilityMap(Cleave);
        var (state, _) = Arena(Cleave);
        var caster = state.Actors[CasterId];
        caster.TargetId = TargetId;
        var rules = Rules.Make(Config(abilities));
        Tick(state, rules, Cmds(Cast(CasterId, Cleave.Id, TargetId)), _ => { });
        Assert.NotNull(caster.CastId);

        Assert.Equal(caster.Yaw, ActorFacing.RenderYaw(state, caster, abilities, null, false));
    }

    [Fact]
    public void ThePlayersSteeredSeatKeepsItsOwnFacingWhileItsTargetIsOffToTheSide()
    {
        var (state, abilities) = Arena(Bolt);
        var caster = state.Actors[CasterId];
        caster.TargetId = TargetId;

        Assert.Equal(Backwards, ActorFacing.RenderYaw(state, caster, abilities, null, true));
    }

    private static (SimState State, IReadOnlyDictionary<string, AbilityDef> Abilities) Arena(AbilityDef def)
    {
        var abilities = AbilityMap(def);
        var caster = Make(CasterId, ActorKind.Enemy, "caster", maxHealth: 1000, maxResource: 100, yaw: Backwards);
        caster.Resource = 100;
        var target = Make(TargetId, ActorKind.Player, "target", z: 1000, maxHealth: 1000, yaw: Backwards);
        return (At(0, caster, target), abilities);
    }
}
