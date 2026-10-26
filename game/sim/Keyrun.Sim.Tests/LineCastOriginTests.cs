using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// A line cast must resolve against the line its telegraph drew at cast start, not the caster's live facing. The case is
/// constructed: a target behind a line-casting enemy is outside the drawn line; the enemy turns to face it mid-cast; the
/// cast completes. Targeting.InLine reads the caster's live Yaw, so this test fails until the line origin is stored.
/// </summary>
public class LineCastOriginTests
{
    private const int CasterId = 1;
    private const int TargetId = 2;
    private const int DrawnYaw = 0; // facing +x when the cast starts: the drawn line points away from the target
    private const int TurnedYaw = 512; // facing -x after the turn: the target now stands dead ahead
    private const int CastTicks = 40; // the cast length used by the cone origin case
    private const long LineLength = 3000;
    private const long LineHalfWidth = 600;
    private const long LineWidth = LineHalfWidth * 2;

    private static readonly AbilityDef Charge = Def(
        "test.lineOriginCharge",
        new AbilityShape(ShapeKind.Line, Radius: LineHalfWidth, Length: LineLength),
        new EffectOp[] { new DamageOp(5, "physical") },
        castTicks: CastTicks);

    /// <summary>Starts the line cast, turns the caster on the first tick under way, and runs the cast to its end.</summary>
    private static Outcome CastThroughTurn()
    {
        var abilities = AbilityMap(Charge);
        var caster = Make(CasterId, ActorKind.Enemy, "caster", maxHealth: 1000, maxResource: 100, yaw: DrawnYaw);
        caster.Resource = 100;
        var target = Make(TargetId, ActorKind.Player, "target", x: -1000, z: 0, maxHealth: 1000, yaw: DrawnYaw);
        var state = At(0, caster, target);
        var rules = Rules.Make(Config(abilities));
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(CasterId, Charge.Id, TargetId)), events.Add);
        Assert.Equal(Charge.Id, caster.CastId);
        var startPos = caster.Pos;

        caster.Yaw = TurnedYaw; // the caster turns to face the target during the cast; its position does not change
        for (var i = 0; i < CastTicks + 10 && caster.CastId is not null; i++)
        {
            Tick(state, rules, Cmds(), events.Add);
        }
        return new Outcome(state, events, startPos, target.Pos);
    }

    [Fact]
    public void LineCastDoesNotHitATargetOnlyInsideTheLineAfterTheCasterTurns()
    {
        var outcome = CastThroughTurn();
        var target = outcome.State.Actors[TargetId];

        Assert.True(outcome.Events.Any(e => e.Type == "cast_complete"), "the line cast never completed; the case proves nothing");
        Assert.False(
            Telegraphs.PointInLine(outcome.StartPos, DrawnYaw, LineLength, LineWidth, outcome.TargetPos),
            "precondition: the target must be outside the line as drawn at cast start");
        Assert.True(
            Telegraphs.PointInLine(outcome.StartPos, TurnedYaw, LineLength, LineWidth, outcome.TargetPos),
            "precondition: the target must be inside the line once the caster has turned");
        Assert.True(
            target.Health == target.MaxHealth,
            $"target outside the line drawn at cast start took damage: health {target.Health} of {target.MaxHealth} " +
            "(cast_complete resolved the line on the caster's live facing, not the telegraph's)");
    }

    private sealed record Outcome(SimState State, List<SimEvent> Events, Vec3 StartPos, Vec3 TargetPos);
}
