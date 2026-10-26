using System.Collections.Generic;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Finding f-e0650754, attack half (Matt 2026-10-22: "casters only face the aggrod target 100% of the time during their
/// animation ... they should be visibly aggrod onto a player"). An aggroed melee enemy swings at a target that crosses
/// from one side of it to the other every few ticks. On every swing tick, and on every tick between swings, the drawn
/// facing (<see cref="SwingFacing.RenderYaw"/> for the sim hold, <see cref="ActorFacing.RenderYaw"/> for the view rule)
/// must point at the target's current position. The enemy never moves. The test does not
/// assert on its sim <see cref="ActorState.Yaw"/>. Every case drives the real rules (<see cref="Rules.Make"/>).
/// </summary>
public class SwingFacingTracksTargetTests
{
    private const int EnemyId = 1;
    private const int TargetId = 2;
    private const int Backwards = 768; // facing -z; the target starts at +z
    private const int TicksPerSide = 3; // the target switches sides every three ticks
    private const int Ticks = 36;       // twelve swing ticks (one every three ticks), six per side

    private static readonly AbilityDef Punch = Def("test.punch", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(5, "physical") });

    [Fact]
    public void AnAggroedMeleeSwingFacesTheTargetOnEverySwingTickAsItAlternatesSides()
    {
        var abilities = AbilityMap(Punch);
        var rules = Rules.Make(Config(abilities));
        var state = Arena();
        var enemy = state.Actors[EnemyId];
        var target = state.Actors[TargetId];
        enemy.TargetId = TargetId;

        FacingHold? hold = null;
        var swingTicks = 0;
        for (var i = 0; i < Ticks; i++)
        {
            // the target crosses to the other side of the enemy every few ticks, and the enemy never moves
            target.Pos = new Vec3(i / TicksPerSide % 2 == 0 ? 600 : -600, 0, 1000);

            var cmds = new List<Command>();
            var isSwingTick = i % TicksPerSide == 0;
            if (isSwingTick)
            {
                enemy.GcdReadyTick = 0; // a melee instant swing: ready every swing tick
                cmds.Add(Cast(EnemyId, Punch.Id, TargetId));
            }

            var events = new List<SimEvent>();
            Tick(state, rules, cmds, events.Add);
            hold = SwingFacing.After(state, enemy, events, abilities, hold);

            var swung = events.Exists(e => e.Type == "cast_complete" && Int(e, "actorId") == EnemyId);
            if (isSwingTick)
            {
                Assert.True(swung, $"the enemy's swing on tick {i} did not resolve");
                swingTicks++;
            }

            // the drawn facing must track the target's current position on every tick
            var expected = Fixed.YawBetween(enemy.Pos, target.Pos);
            Assert.True(
                expected == SwingFacing.RenderYaw(state, enemy, abilities, hold),
                $"tick {i}: sim-hold yaw {SwingFacing.RenderYaw(state, enemy, abilities, hold)} != target yaw {expected}");
            Assert.True(
                expected == ActorFacing.RenderYaw(state, enemy, abilities, hold, false),
                $"tick {i}: view yaw {ActorFacing.RenderYaw(state, enemy, abilities, hold, false)} != target yaw {expected}");
        }

        Assert.Equal(Ticks / TicksPerSide, swingTicks);
    }

    /// <summary>
    /// Separates the drawn facing from the stored <see cref="ActorState.Yaw"/>. The enemy swings once at a target on the
    /// +x side, then the target walks around to the -x side over the following ticks with no swing. The hold stays
    /// valid (the enemy never moves), and no cast starts, so the sim never re-turns the stored yaw. Every tick after the
    /// swing must still draw the enemy facing the target's current position.
    /// </summary>
    [Fact]
    public void ASwingerFacesAMovingTargetBetweenSwingsWithoutRecastingYaw()
    {
        var abilities = AbilityMap(Punch);
        var rules = Rules.Make(Config(abilities));
        var state = Arena();
        var enemy = state.Actors[EnemyId];
        var target = state.Actors[TargetId];
        enemy.TargetId = TargetId;

        // tick 0: the one swing, at a target on the +x side
        target.Pos = new Vec3(600, 0, 1000);
        enemy.GcdReadyTick = 0;
        var swingEvents = new List<SimEvent>();
        Tick(state, rules, new List<Command> { Cast(EnemyId, Punch.Id, TargetId) }, swingEvents.Add);
        FacingHold? hold = SwingFacing.After(state, enemy, swingEvents, abilities, null);
        Assert.True(
            swingEvents.Exists(e => e.Type == "cast_complete" && Int(e, "actorId") == EnemyId),
            "the enemy's swing on tick 0 did not resolve");
        Assert.NotNull(hold);

        // ticks 1..8: no swing; the target walks from the +x side to the -x side one step per tick
        for (var i = 1; i <= 8; i++)
        {
            target.Pos = new Vec3(600 - 150 * i, 0, 1000);

            var events = new List<SimEvent>();
            Tick(state, rules, new List<Command>(), events.Add);
            hold = SwingFacing.After(state, enemy, events, abilities, hold);

            var expected = Fixed.YawBetween(enemy.Pos, target.Pos);
            Assert.True(
                expected == SwingFacing.RenderYaw(state, enemy, abilities, hold),
                $"tick {i}: sim-hold yaw {SwingFacing.RenderYaw(state, enemy, abilities, hold)} != target yaw {expected}");
            Assert.True(
                expected == ActorFacing.RenderYaw(state, enemy, abilities, hold, false),
                $"tick {i}: view yaw {ActorFacing.RenderYaw(state, enemy, abilities, hold, false)} != target yaw {expected}");
        }
    }

    private static SimState Arena()
    {
        var enemy = Make(EnemyId, ActorKind.Enemy, "enemy", maxHealth: 100000, yaw: Backwards);
        var target = Make(TargetId, ActorKind.Player, "target", z: 1000, maxHealth: 100000, yaw: Backwards);
        return At(0, enemy, target);
    }
}
