using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// WoW M+ rule (dungeon loop defers to WoW Mythic+): a mob casting a frontal or cone stops moving and keeps the facing it
/// had at cast start until the cast resolves. The caster's own Pos and Yaw must hold their cast-start values on every tick
/// of the cast window, even while its AI would walk and turn it toward a target that sidesteps. The resolve side (the cone
/// landing on its stored telegraph) is covered by <see cref="ConeCastOriginTests"/>; this file covers only the caster.
/// </summary>
public class ConeCasterFreezeTests
{
    private const int CasterId = 1;
    private const int TargetId = 2;
    private const int CastTicks = 40; // the Warden's Cleave cast length in the trace
    private const long ConeRadius = 3000;
    private const int ConeAngle1024 = 256;
    private const long TargetSidestepMmPerTick = 60; // the target walks along +z while the caster is casting

    private static readonly AbilityDef Cleave = Def(
        "test.coneCasterFreezeCleave",
        new AbilityShape(ShapeKind.Cone, ConeRadius, ConeAngle1024),
        new EffectOp[] { new DamageOp(5, "physical") },
        castTicks: CastTicks);

    /// <summary>
    /// Starts the cone cast at a target in front of the caster, then runs the cast while the caster's AI emits a chase
    /// move (walk toward the target, face it) every tick and the target sidesteps. Returns each tick of the cast window
    /// where the caster's Pos or Yaw differs from its cast-start value.
    /// </summary>
    private static (List<string> Violations, int WindowTicks, List<SimEvent> Events, Vec3 StartPos, int StartYaw) RunCastWithChasingAi()
    {
        var abilities = AbilityMap(Cleave);
        var caster = Make(CasterId, ActorKind.Enemy, "caster", maxHealth: 1000, maxResource: 100, yaw: 0);
        caster.Resource = 100;
        var target = Make(TargetId, ActorKind.Player, "target", x: 1000, z: 0, maxHealth: 1000, yaw: 0);
        var state = At(0, caster, target);
        var rules = Rules.Make(Config(abilities));
        var events = new List<SimEvent>();

        Tick(state, rules, Cmds(Cast(CasterId, Cleave.Id, TargetId)), events.Add);
        Assert.Equal(Cleave.Id, caster.CastId);
        var startPos = caster.Pos;
        var startYaw = caster.Yaw;

        var violations = new List<string>();
        var windowTicks = 0;
        for (var i = 0; i < CastTicks + 10 && caster.CastId is not null; i++)
        {
            target.Pos = new Vec3(target.Pos.X, 0, TargetSidestepMmPerTick * (i + 1));

            var walk = new Vec3(target.Pos.X - caster.Pos.X, 0, target.Pos.Z - caster.Pos.Z);
            var ai = walk.X == 0 && walk.Z == 0
                ? new MoveCommand(state.Tick, CasterId, new Dir2(0, 0), startYaw)
                : new MoveCommand(
                    state.Tick,
                    CasterId,
                    new Dir2(Fixed.NormaliseTo(walk, 1000).X, Fixed.NormaliseTo(walk, 1000).Z),
                    Fixed.YawBetween(caster.Pos, target.Pos));

            Tick(state, rules, Cmds(ai), events.Add);

            if (caster.CastId is not null)
            {
                windowTicks++;
                if (caster.Pos != startPos || caster.Yaw != startYaw)
                {
                    violations.Add(
                        $"tick {state.Tick}: caster Pos {caster.Pos} Yaw {caster.Yaw} " +
                        $"(cast start Pos {startPos} Yaw {startYaw})");
                }
            }
        }
        return (violations, windowTicks, events, startPos, startYaw);
    }

    [Fact]
    public void ConeCasterHoldsItsPosAndYawOnEveryTickOfTheCastWindow()
    {
        var (violations, windowTicks, events, startPos, startYaw) = RunCastWithChasingAi();

        Assert.True(events.Any(e => e.Type == "cast_complete"), "the cone cast never completed; the case proves nothing");
        Assert.True(
            windowTicks >= CastTicks - 1,
            $"the cast window was only {windowTicks} ticks, shorter than the {CastTicks}-tick cast; the case proves nothing");
        Assert.True(
            violations.Count == 0,
            $"Expected: caster Pos {startPos} and Yaw {startYaw} unchanged on all {windowTicks} cast-window ticks. " +
            $"Actual: {violations.Count} tick(s) changed the caster; first: {violations.FirstOrDefault()}");
    }
}
