using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// A PLAYER cone cast resolves on the facing the player has at cast_complete (WoW M+: the player steers the cone while it
/// is cast). Only enemy casts keep the aim locked at cast start, because an enemy's telegraph is the warning the player
/// reads. The case is constructed: the caster starts facing +x, a target ahead of that start facing is inside the drawn cone,
/// the caster turns 180 degrees during the cast, and a target ahead of the new facing completes the cast. The player case
/// must hit the completion-facing target and miss the start-facing one. The enemy control turns the same way and still
/// hits along its start aim.
/// </summary>
public class PlayerConeCompletionFacingTests
{
    private const int CasterId = 1;
    private const int StartTargetId = 2; // ahead of the start facing (+x), behind the completion facing
    private const int CompletionTargetId = 3; // ahead of the completion facing (-x), behind the start facing
    private const int StartYaw = 0; // facing +x when the cast starts
    private const int CompletionYaw = 512; // facing -x after the 180 degree turn
    private const int CastTicks = 40; // the Warden's Cleave cast length in the trace
    private const long ConeRadius = 3000;
    private const int ConeAngle1024 = 256;
    private const long TargetDistance = 1000;

    private static readonly AbilityDef Cone = Def(
        "test.playerConeCompletionCone",
        new AbilityShape(ShapeKind.Cone, ConeRadius, ConeAngle1024),
        new EffectOp[] { new DamageOp(5, "physical") },
        castTicks: CastTicks);

    /// <summary>The kind a caster's cone can hit: a player's cone hits enemies, an enemy's cone hits players.</summary>
    private static ActorKind OpposingKind(ActorKind casterKind) =>
        casterKind == ActorKind.Player ? ActorKind.Enemy : ActorKind.Player;

    /// <summary>Starts the cone cast, turns the caster 180 degrees on the first tick under way, and runs the cast to its end.</summary>
    private static Outcome CastThroughTurn(ActorKind casterKind, int? castTargetId)
    {
        var abilities = AbilityMap(Cone);
        var caster = Make(CasterId, casterKind, "caster", maxHealth: 1000, maxResource: 100, yaw: StartYaw);
        caster.Resource = 100;
        var targetKind = OpposingKind(casterKind);
        var startTarget = Make(StartTargetId, targetKind, "start", x: TargetDistance, z: 0, maxHealth: 1000, yaw: StartYaw);
        var completionTarget = Make(CompletionTargetId, targetKind, "completion", x: -TargetDistance, z: 0, maxHealth: 1000, yaw: StartYaw);
        var state = At(0, caster, startTarget, completionTarget);
        var rules = Rules.Make(Config(abilities));
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(CasterId, Cone.Id, castTargetId)), events.Add);
        Assert.Equal(Cone.Id, caster.CastId);
        var startPos = caster.Pos;

        caster.Yaw = CompletionYaw; // the caster turns 180 degrees during the cast; its position does not change
        for (var i = 0; i < CastTicks + 10 && caster.CastId is not null; i++)
        {
            Tick(state, rules, Cmds(), events.Add);
        }
        return new Outcome(state, events, startPos);
    }

    [Fact]
    public void PlayerConeCastResolvesOnTheFacingItHasAtCompletion()
    {
        var outcome = CastThroughTurn(ActorKind.Player, castTargetId: null);
        var startTarget = outcome.State.Actors[StartTargetId];
        var completionTarget = outcome.State.Actors[CompletionTargetId];

        Assert.True(outcome.Events.Any(e => e.Type == "cast_complete"), "the player cone cast never completed; the case proves nothing");
        Assert.True(
            Telegraphs.PointInCone(outcome.StartPos, StartYaw, ConeAngle1024, ConeRadius, startTarget.Pos),
            "precondition: the start-facing target must be inside the cone drawn at cast start");
        Assert.False(
            Telegraphs.PointInCone(outcome.StartPos, StartYaw, ConeAngle1024, ConeRadius, completionTarget.Pos),
            "precondition: the completion-facing target must be outside the cone drawn at cast start");
        Assert.True(
            Telegraphs.PointInCone(outcome.StartPos, CompletionYaw, ConeAngle1024, ConeRadius, completionTarget.Pos),
            "precondition: the completion-facing target must be inside the cone at the caster's completion facing");
        Assert.False(
            Telegraphs.PointInCone(outcome.StartPos, CompletionYaw, ConeAngle1024, ConeRadius, startTarget.Pos),
            "precondition: the start-facing target must be outside the cone at the caster's completion facing");

        Assert.True(
            startTarget.Health == startTarget.MaxHealth,
            $"player cone hit the start-facing target {StartTargetId} (health {startTarget.Health} of {startTarget.MaxHealth}): " +
            "the player cone resolved on the facing stored at cast start, not on the facing held at cast_complete");
        Assert.True(
            completionTarget.Health < completionTarget.MaxHealth,
            $"player cone missed the completion-facing target {CompletionTargetId} (health {completionTarget.Health} of " +
            $"{completionTarget.MaxHealth}): the cone must resolve on the player's facing at cast_complete");
    }

    [Fact]
    public void EnemyConeCastStillResolvesOnTheAimItStoredAtCastStart()
    {
        var outcome = CastThroughTurn(ActorKind.Enemy, castTargetId: StartTargetId);
        var startTarget = outcome.State.Actors[StartTargetId];
        var completionTarget = outcome.State.Actors[CompletionTargetId];

        Assert.True(outcome.Events.Any(e => e.Type == "cast_complete"), "the enemy cone cast never completed; the control proves nothing");
        Assert.True(
            startTarget.Health < startTarget.MaxHealth,
            $"enemy cone missed the start-aim target {StartTargetId} (health {startTarget.Health} of {startTarget.MaxHealth}): " +
            "an enemy cone must keep the aim locked at cast start");
        Assert.True(
            completionTarget.Health == completionTarget.MaxHealth,
            $"enemy cone hit the completion-facing target {CompletionTargetId} (health {completionTarget.Health} of " +
            $"{completionTarget.MaxHealth}): an enemy cone must not follow the caster's turn");
    }

    private sealed record Outcome(SimState State, List<SimEvent> Events, Vec3 StartPos);
}
