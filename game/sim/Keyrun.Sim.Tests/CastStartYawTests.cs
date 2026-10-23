using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane CAST-FACING, sim side (R-FACING-1 (a), Matt 2026-10-22: "casters only face the aggrod target ... they should be
/// visibly aggrod onto a player"). A targeted enemy cast writes the caster's sim <see cref="ActorState.Yaw"/> to
/// <see cref="Fixed.YawBetween"/> from the caster to its target when the cast starts, and that yaw holds for the whole
/// cast until <c>cast_complete</c>, even while the target moves. Cases drive the real <see cref="Rules.Make"/> step.
/// Named <c>CastStartYawTests</c> because <c>CastFacingTests</c> already pins the render-only D10 rule.
/// </summary>
public class CastStartYawTests
{
    private const int CasterId = 1;
    private const int TargetId = 2;
    private const int Backwards = 768; // facing -z; the target stands at +z (yaw 256)

    private static readonly AbilityDef Bolt = Def(
        "test.yawBolt", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(5, "fire") }, castTicks: 20);

    [Fact]
    public void AnEnemyTargetedCastTurnsTheCasterToItsTargetAsItStarts()
    {
        var (state, abilities) = Arena();
        var caster = state.Actors[CasterId];
        var target = state.Actors[TargetId];
        var rules = Rules.Make(Config(abilities));
        Assert.Equal(Backwards, caster.Yaw); // facing away before the cast

        Tick(state, rules, Cmds(Cast(CasterId, Bolt.Id, TargetId)), _ => { });

        Assert.Equal(Bolt.Id, caster.CastId);
        Assert.Equal(Fixed.YawBetween(caster.Pos, target.Pos), caster.Yaw);
        Assert.NotEqual(Backwards, caster.Yaw);
    }

    [Fact]
    public void TheCasterKeepsThatYawOnEveryTickUntilCastComplete()
    {
        var (state, abilities) = Arena();
        var caster = state.Actors[CasterId];
        var target = state.Actors[TargetId];
        var rules = Rules.Make(Config(abilities));
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(CasterId, Bolt.Id, TargetId)), events.Add);
        var startYaw = caster.Yaw;
        Assert.Equal(Fixed.YawBetween(caster.Pos, target.Pos), startYaw);

        var ticksChecked = 0;
        for (var i = 0; i < 40 && !events.Any(e => e.Type == "cast_complete"); i++)
        {
            // the target drifts from one side of the caster to the other; the caster's facing must not follow it
            target.Pos = new Vec3(i % 2 == 0 ? 600 : -600, 0, 1000);
            Tick(state, rules, Cmds(), events.Add);
            Assert.Equal(startYaw, caster.Yaw);
            ticksChecked++;
        }

        Assert.True(events.Any(e => e.Type == "cast_complete"), $"the cast never completed in {ticksChecked} ticks");
        Assert.True(ticksChecked >= 15, $"only {ticksChecked} ticks were checked before cast_complete");
    }

    private static (SimState State, IReadOnlyDictionary<string, AbilityDef> Abilities) Arena()
    {
        var abilities = AbilityMap(Bolt);
        var caster = Make(CasterId, ActorKind.Enemy, "caster", maxHealth: 1000, maxResource: 100, yaw: Backwards);
        caster.Resource = 100;
        var target = Make(TargetId, ActorKind.Player, "target", z: 1000, maxHealth: 1000, yaw: Backwards);
        return (At(0, caster, target), abilities);
    }
}
