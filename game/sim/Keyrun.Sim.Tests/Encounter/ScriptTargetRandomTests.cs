using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// Caster facing for a random-target script cast (f-e0650754, Matt: "they should be visibly aggrod onto a player"). A
/// script step with <c>target: random</c> picks a seat, and a cast starts AT that seat for its whole cast, so the sim
/// yaw the caster starts the cast with must point at the seat it resolved on. <see cref="Casting.StartCast"/> never
/// writes <see cref="ActorState.Yaw"/> today, so the caster keeps the yaw of its last move and looks backwards into its
/// pack. Seeds 1-5 vary the PRNG draw; the Target and Circle shapes (R-FACING-1) face the seat; cone and line keep the movement yaw (see the cone Fact).
/// </summary>
public sealed class ScriptTargetRandomTests
{
    private const int CasterId = 1;
    private const int Tank = 10;
    private const int SeatB = 11;
    private const int SeatC = 12;
    private const int MovementYaw = 768; // the caster just walked toward -z, away from every seat
    private const int ToleranceYaw = 14; // 5 degrees: 1024 yaw units per turn, 5 * 1024 / 360 = 14.2, rounded down
    private const string CastId = "hostile.bolt";
    private const long CastRange = 3000;

    public static IEnumerable<object[]> Shapes() =>
        new[] { new object[] { ShapeKind.Target }, new object[] { ShapeKind.Circle } };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void A_random_target_cast_starts_with_the_caster_facing_the_seat_it_resolved_on(ShapeKind shape)
    {
        var resolvedSeats = new HashSet<int>();
        var misfaced = new List<string>();
        for (uint seed = 1; seed <= 5; seed++)
        {
            var state = Arena(seed);
            var caster = state.Actors[CasterId];
            var def = AbilityFor(shape);
            var scripts = EncounterTestKit.Scripts(CasterId, ScriptStep.At(0, CastId) with { Target = ScriptTargetOverride.Random });
            var scriptAbilities = new Dictionary<string, ScriptAbility> { [CastId] = new(CastId, CastRange, ShapeName(shape), false) };

            var cast = Scripts.StepEnemyScripts(state, scripts, abilities: scriptAbilities).Commands.OfType<CastCommand>().Single();
            Assert.NotNull(cast.TargetId);
            var seat = state.Actors[cast.TargetId!.Value];
            resolvedSeats.Add(seat.Id);

            var error = Casting.StartCast(state, caster, def, cast.TargetId, null, state.Tick);
            Assert.Null(error);

            var expected = Fixed.YawBetween(caster.Pos, seat.Pos);
            if (YawDistance(caster.Yaw, expected) > ToleranceYaw)
            {
                misfaced.Add($"seed {seed}, {shape} cast on seat {seat.Id}: caster.Yaw {caster.Yaw}, bearing {expected}");
            }
        }

        Assert.True(
            resolvedSeats.Count >= 2,
            $"random target resolved on only seats {string.Join(", ", resolvedSeats)} across seeds 1-5; expected at least two distinct seats");
        Assert.True(
            misfaced.Count == 0,
            $"caster.Yaw should point at the resolved seat within {ToleranceYaw} units at cast start; it does not: "
            + string.Join(" | ", misfaced));
    }

    // R-FACING-1: a cone cast resolves along Yaw, so a random-target cone keeps the movement facing it started with.
    [Fact]
    public void A_random_target_cone_cast_keeps_the_movement_yaw_it_aims_along()
    {
        for (uint seed = 1; seed <= 5; seed++)
        {
            var state = Arena(seed);
            var caster = state.Actors[CasterId];
            var def = AbilityFor(ShapeKind.Cone);
            var scripts = EncounterTestKit.Scripts(CasterId, ScriptStep.At(0, CastId) with { Target = ScriptTargetOverride.Random });
            var scriptAbilities = new Dictionary<string, ScriptAbility> { [CastId] = new(CastId, CastRange, ShapeName(ShapeKind.Cone), false) };

            var cast = Scripts.StepEnemyScripts(state, scripts, abilities: scriptAbilities).Commands.OfType<CastCommand>().Single();
            Assert.NotNull(cast.TargetId);

            var error = Casting.StartCast(state, caster, def, cast.TargetId, null, state.Tick);
            Assert.Null(error);

            Assert.True(
                caster.Yaw == MovementYaw,
                $"seed {seed}: a cone cast must keep the movement yaw {MovementYaw} it aims along, got caster.Yaw {caster.Yaw}");
        }
    }

    // The enemy caster at the origin walking toward -z; a tank holding top threat and two other players in range. The
    // seats sit on +z, -x and +x so none of them is the movement yaw (768 = -z).
    private static SimState Arena(uint seed)
    {
        var caster = Make(CasterId, ActorKind.Enemy, "boss", ActorRole.None, 0, 0, 1000, 0, MovementYaw);
        caster.Threat[Tank] = 500;
        caster.Threat[SeatB] = 100;
        caster.Threat[SeatC] = 100;
        var tank = Make(Tank, ActorKind.Player, "warden", ActorRole.Tank, 0, 1500, 1000);
        var seatB = Make(SeatB, ActorKind.Player, "cinder", ActorRole.None, -1500, 0, 1000);
        var seatC = Make(SeatC, ActorKind.Player, "cinder", ActorRole.None, 1500, 0, 1000);
        // Each seed mixes every PRNG word, so seeds 1-5 are five well-separated streams (small seeds leave the high words
        // near zero, and the first draw of a near-zero state is the same index for every seed).
        var prng = new PrngState(seed * 0x9E3779B9u, (seed * 0x85EBCA6Bu) ^ 222u, 333u ^ (seed * 0xC2B2AE35u), 444u + (seed * 0x27D4EB2Fu));
        return EncounterTestKit.State(0, prng, caster, tank, seatB, seatC);
    }

    private static AbilityDef AbilityFor(ShapeKind shape) => shape switch
    {
        ShapeKind.Cone => Def(CastId, new AbilityShape(ShapeKind.Cone, 3000, 256, null), new EffectOp[] { new HealOp(1) }, castTicks: 20, range: CastRange),
        ShapeKind.Line => Def(CastId, new AbilityShape(ShapeKind.Line, null, null, 6000), new EffectOp[] { new HealOp(1) }, castTicks: 20, range: CastRange),
        ShapeKind.Circle => Def(CastId, new AbilityShape(ShapeKind.Circle, 3000), new EffectOp[] { new HealOp(1) }, castTicks: 20, range: CastRange),
        _ => Def(CastId, new AbilityShape(ShapeKind.Target), new EffectOp[] { new HealOp(1) }, castTicks: 20, range: CastRange),
    };

    private static string ShapeName(ShapeKind shape) => shape switch
    {
        ShapeKind.Cone => "cone",
        ShapeKind.Line => "line",
        ShapeKind.Circle => "circle",
        _ => "target",
    };

    // Circular distance between two yaws in 1/1024 turn.
    private static int YawDistance(int a, int b)
    {
        var d = System.Math.Abs(a - b) % 1024;
        return System.Math.Min(d, 1024 - d);
    }
}
