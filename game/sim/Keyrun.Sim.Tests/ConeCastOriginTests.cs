using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// A cone cast must resolve against the cone its telegraph drew at cast start, not the caster's live facing. Hollow-vault
/// k10 seed 7: the Warden's 40-tick Cleave telegraph was stored at cast start, but the damage read the boss's live Pos and
/// Yaw at cast_complete, so a player outside the drawn cone was hit after the boss turned. The case is constructed: a
/// target behind a cone-casting enemy is outside the drawn cone; the enemy turns to face it mid-cast; the cast completes.
/// </summary>
public class ConeCastOriginTests
{
    private const int CasterId = 1;
    private const int TargetId = 2;
    private const int DrawnYaw = 0; // facing +x when the cast starts: the drawn cone points away from the target
    private const int TurnedYaw = 512; // facing -x after the turn: the target now stands dead ahead
    private const int CastTicks = 40; // the Warden's Cleave cast length in the trace
    private const long ConeRadius = 3000;
    private const int ConeAngle1024 = 256;

    private static readonly AbilityDef Cleave = Def(
        "test.coneOriginCleave",
        new AbilityShape(ShapeKind.Cone, ConeRadius, ConeAngle1024),
        new EffectOp[] { new DamageOp(5, "physical") },
        castTicks: CastTicks);

    /// <summary>Starts the cone cast, turns the caster on the first tick under way, and runs the cast to its end.</summary>
    private static Outcome CastThroughTurn()
    {
        var abilities = AbilityMap(Cleave);
        var caster = Make(CasterId, ActorKind.Enemy, "caster", maxHealth: 1000, maxResource: 100, yaw: DrawnYaw);
        caster.Resource = 100;
        var target = Make(TargetId, ActorKind.Player, "target", x: -1000, z: 0, maxHealth: 1000, yaw: DrawnYaw);
        var state = At(0, caster, target);
        var rules = Rules.Make(Config(abilities));
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(CasterId, Cleave.Id, TargetId)), events.Add);
        Assert.Equal(Cleave.Id, caster.CastId);
        var startPos = caster.Pos;

        caster.Yaw = TurnedYaw; // the caster turns to face the target during the cast; its position does not change
        for (var i = 0; i < CastTicks + 10 && caster.CastId is not null; i++)
        {
            Tick(state, rules, Cmds(), events.Add);
        }
        return new Outcome(state, events, startPos, target.Pos);
    }

    [Fact]
    public void ConeCastDoesNotHitATargetOnlyInsideTheConeAfterTheCasterTurns()
    {
        var outcome = CastThroughTurn();
        var target = outcome.State.Actors[TargetId];

        Assert.True(outcome.Events.Any(e => e.Type == "cast_complete"), "the cone cast never completed; the case proves nothing");
        Assert.False(
            Telegraphs.PointInCone(outcome.StartPos, DrawnYaw, ConeAngle1024, ConeRadius, outcome.TargetPos),
            "precondition: the target must be outside the cone as drawn at cast start");
        Assert.True(
            Telegraphs.PointInCone(outcome.StartPos, TurnedYaw, ConeAngle1024, ConeRadius, outcome.TargetPos),
            "precondition: the target must be inside the cone once the caster has turned");
        Assert.True(
            target.Health == target.MaxHealth,
            $"target outside the cone drawn at cast start took damage: health {target.Health} of {target.MaxHealth} " +
            "(cast_complete resolved the cone on the caster's live facing, not the telegraph's)");
    }

    [Fact]
    public void TheCasterStaysPutThroughAConeCastItTurnsDuringTheCast()
    {
        var outcome = CastThroughTurn();
        var caster = outcome.State.Actors[CasterId];

        Assert.True(outcome.Events.Any(e => e.Type == "cast_complete"), "the cone cast never completed; the case proves nothing");
        Assert.Null(caster.CastId);
        Assert.True(
            caster.Pos == outcome.StartPos,
            $"caster position at cast_complete {caster.Pos} differs from its position at cast start {outcome.StartPos}");
    }

    private sealed record Outcome(SimState State, List<SimEvent> Events, Vec3 StartPos, Vec3 TargetPos);
}
