using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Bots.Tests.BotTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// LINE-DODGE (finding f-e17e190c, ruling R-DODGE-1): a floor telegraph exists to be left. A bot seat standing inside an
/// active caster-to-target line telegraph (slagRam.moltenCharge: length 9000, width 800, castTicks 50, not interruptible)
/// must step out of it before the cast resolves, as a Mythic+ player would. These tests run the seat the way the sim
/// does: every move the decision carries is applied in order, the seat keeps its damage role, and the walkable rule
/// (the sim clamps each actor into its open boxes, Run.StepLayout) is applied after each tick.
/// </summary>
public sealed class TelegraphLineDodgeTests
{
    /// <summary>Walk speed per move command, mm (dir is a 1000-scale unit vector; 180 mm is the bots' walk).</summary>
    private const long WalkMmPerTick = 180;

    /// <summary>Caster id (slagRam), and the seat id (the bot holds the caster as its target).</summary>
    private const int CasterId = 2;

    private const int SeatId = 1;

    /// <summary>
    /// A 600 mm corridor inside the 800 mm line: every lateral step out of the telegraph runs into a wall, so the seat can
    /// leave only along the line (behind the caster or past its far end). Kept off the line's edge so no point sits on a
    /// float boundary.
    /// </summary>
    private static readonly Bounds FilledCorridor = new(-300, 300, -1000, 10000);

    /// <summary>Caster-to-target line: faces +z (yaw 256 = +z), starts at the caster, 9000 mm long, 800 mm wide.</summary>
    private static BotTelegraph MoltenChargeLine(int resolvesTick) =>
        new("molten-charge", TelegraphShape.Line, new PointMm(0, 0), resolvesTick, true)
        {
            Yaw = 256,
            Length = 9000,
            Width = 800,
            SourceId = CasterId,
        };

    /// <summary>
    /// Runs the seat from <paramref name="start"/> through the cast's ticks on <paramref name="layout"/> (null = open
    /// field) and returns its position at the resolve tick.
    /// </summary>
    private static PointMm SeatAtResolve(PointMm start, BotTelegraph telegraph, NavLayout? layout, Bounds? walkable)
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(telegraph.ResolvesTick - 50);
        var prng = Skill.SeedPrng(1);
        var seat = MakeActor(id: SeatId, classId: "warrior", role: ActorRole.Damage, x: start.X, z: start.Z, targetId: CasterId);
        var caster = MakeActor(id: CasterId, kind: ActorKind.Enemy, classId: "slagRam", role: ActorRole.None, x: 0, z: 0, targetId: SeatId);
        var pos = start;
        for (var tick = telegraph.ResolvesTick - 50; tick < telegraph.ResolvesTick; tick++)
        {
            SetPos(seat, pos);
            var view = View(MakeState(tick, seat, caster), null, telegraph) with { Layout = layout };
            var result = bot.Decide(view, memory, SeatId, prng);
            memory = result.Memory;
            prng = result.Prng;
            foreach (var move in result.Commands.OfType<MoveCommand>())
            {
                pos = ApplyMove(pos, move, WalkMmPerTick);
            }
            if (walkable is { } box)
            {
                pos = new PointMm(Math.Clamp(pos.X, box.MinX, box.MaxX), Math.Clamp(pos.Z, box.MinZ, box.MaxZ));
            }
        }
        return pos;
    }

    [Fact(DisplayName = "telegraph avoidance: line dodge > a seat inside a line telegraph leaves it before the cast resolves, with its role still deciding")]
    public void SeatLeavesLineTelegraphBeforeItResolves()
    {
        var telegraph = MoltenChargeLine(resolvesTick: 150);
        var start = new PointMm(0, 3000);
        Assert.True(Positioning.IsInAnyDanger(start, new[] { telegraph }), "precondition: the seat starts inside the line");

        var final = SeatAtResolve(start, telegraph, layout: null, walkable: null);

        Assert.False(
            Positioning.IsInAnyDanger(final, new[] { telegraph }),
            $"the seat is still inside the line at resolve tick {telegraph.ResolvesTick}, final position ({final.X}, {final.Z})");
    }

    [Fact(DisplayName = "telegraph avoidance: line dodge > in a corridor the line fills, the seat leaves along the corridor, not into a wall")]
    public void SeatLeavesFilledCorridorAlongItBeforeItResolves()
    {
        var telegraph = MoltenChargeLine(resolvesTick: 150);
        var start = new PointMm(0, 3000);
        Assert.True(Positioning.IsInAnyDanger(start, new[] { telegraph }), "precondition: the seat starts inside the line");
        var layout = new NavLayout(
            new[] { new NavBox("corridor", FilledCorridor, true, null) },
            new[] { new NavTrigger("p1", FilledCorridor) },
            new[] { new Vec3(0, 0, 0) });

        var final = SeatAtResolve(start, telegraph, layout, FilledCorridor);

        Assert.False(
            Positioning.IsInAnyDanger(final, new[] { telegraph }),
            $"the seat is still inside the line at resolve tick {telegraph.ResolvesTick}, final position ({final.X}, {final.Z})");
    }
}
