using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Lane CAST-FACING, bots side (R-FACING-2 (b), finding f-e0650754). A bot seat's facing is owned by its command source:
/// when a bot presses a targeted ability, the seat must already face its target on the tick that cast starts, so
/// <c>Yaw == Fixed.YawBetween(seat, target)</c>. The probe drives the default host run (hollow-vault, key 2, seed 1,
/// bots on, the human idle at the Entrance, as <see cref="ChoirEngageTests"/> does). Before every tick the first bot seat
/// is turned to face AWAY from its nearest living enemy, so a bot that never turns to its target keeps the away yaw.
/// Each targeted cast start by that seat (detected from <c>CastStartTick</c> advancing) is recorded with the seat yaw and
/// the expected yaw read after the step. Expected to be red until the bots layer writes the seat yaw on cast start.
/// </summary>
public sealed class BotCastStartFacingTests
{
    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";
    private const int MaxTicks = 6000;
    private const int YawTurn = 512;
    private const int YawMask = 1023;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());
    private static readonly Lazy<Probe> Shot = new(Drive);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the probe trace is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public BotCastStartFacingTests(ITestOutputHelper output) => _output = output;

    /// <summary>One targeted cast start by the probed seat.</summary>
    /// <param name="Tick">The run tick read after the step that started the cast.</param>
    /// <param name="AbilityId">The ability that started.</param>
    /// <param name="ClassId">The seat's class.</param>
    /// <param name="SeatYaw">The seat yaw at that tick.</param>
    /// <param name="ExpectedYaw">Fixed.YawBetween(seat position, target position) as they stood when the bot decided the cast (before the step).</param>
    private sealed record Start(int Tick, string AbilityId, string ClassId, int SeatYaw, int ExpectedYaw);

    /// <summary>The probe result: the seat and its targeted cast starts.</summary>
    /// <param name="SeatId">The probed bot seat's actor id.</param>
    /// <param name="Starts">Targeted cast starts by that seat, in tick order.</param>
    private sealed record Probe(int SeatId, IReadOnlyList<Start> Starts);

    /// <summary>Drives the host run with the first bot seat turned away from its nearest enemy each tick.</summary>
    /// <returns>The probe result.</returns>
    private static Probe Drive()
    {
        var registry = Registry.Value;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var seatId = setup.BotActorIds.OrderBy(id => id).First();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();

        var backfill = BackfillState.Empty;
        var starts = new List<Start>();
        var lastCastStart = -1;
        var issuedTargeted = 0;

        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null)
        {
            if (run.State.Actors.TryGetValue(seatId, out var seat))
            {
                var enemy = NearestLivingEnemy(run.State, seat);
                if (enemy is not null)
                {
                    seat.Yaw = (FacingYaw(seat.Pos, enemy.Pos) + YawTurn) & YawMask;
                }
            }

            var commands = new List<Command>();
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            var issued = commands.OfType<CastCommand>().Where(c => c.ActorId == seatId && c.TargetId is not null).ToList();
            issuedTargeted += issued.Count;
            // The bot decides its face command and cast from the pre-step state, so the expected yaw is the bearing
            // between the seat and its target as they stood at decision time. Reading them after the step would
            // measure a moved target against a yaw that was correct when it was set.
            var decidedSeat = run.State.Actors.TryGetValue(seatId, out var seatAtDecision) ? seatAtDecision.Pos : (Vec3?)null;
            var decidedTarget = issued.Count > 0 && run.State.Actors.TryGetValue(issued[0].TargetId!.Value, out var targetAtDecision)
                ? targetAtDecision.Pos
                : (Vec3?)null;

            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;

            // A targeted cast the seat issued this tick is a cast start when the seat's CastStartTick advanced.
            if (issued.Count > 0 &&
                decidedSeat is Vec3 seatPos && decidedTarget is Vec3 targetPos &&
                run.State.Actors.TryGetValue(seatId, out var after) &&
                after.CastStartTick is int castStart && castStart != lastCastStart &&
                (targetPos.X != seatPos.X || targetPos.Z != seatPos.Z))
            {
                lastCastStart = castStart;
                starts.Add(new Start(run.State.Tick, issued[0].AbilityId, after.ClassId, after.Yaw, Fixed.YawBetween(seatPos, targetPos)));
            }
        }

        Console.WriteLine($"PROBE issuedTargetedCasts={issuedTargeted} tick={run.State.Tick}");
        return new Probe(seatId, starts);
    }

    /// <summary>The nearest living enemy to the seat, or null when none is alive.</summary>
    /// <param name="state">The sim state.</param>
    /// <param name="seat">The bot seat.</param>
    /// <returns>The nearest enemy actor, or null.</returns>
    private static ActorState? NearestLivingEnemy(SimState state, ActorState seat) =>
        state.Actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.Alive)
            .OrderBy(a => SquaredDistance(seat.Pos, a.Pos))
            .FirstOrDefault();

    /// <summary>Squared horizontal distance between two points, in long to avoid overflow.</summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    /// <returns>The squared distance on the X and Z plane.</returns>
    private static long SquaredDistance(Vec3 a, Vec3 b)
    {
        long dx = (long)a.X - b.X;
        long dz = (long)a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    /// <summary>The sim facing from one point toward another, wrapped into 0..1023.</summary>
    /// <param name="from">The viewer.</param>
    /// <param name="to">The point faced.</param>
    /// <returns>The yaw in 0..1023.</returns>
    private static int FacingYaw(Vec3 from, Vec3 to) => Fixed.YawBetween(from, to) & YawMask;

    /// <summary>The probed seat's first targeted cast start must face its target: the bots layer owns that yaw.</summary>
    [Fact(DisplayName = "Bot facing: a bot seat that presses a targeted ability already faces its target on the tick the cast starts")]
    public void A_bot_seat_faces_its_target_on_the_tick_its_targeted_cast_starts()
    {
        var probe = Shot.Value;
        _output.WriteLine($"PROBE seat={probe.SeatId} targetedCastStarts={probe.Starts.Count}");
        foreach (var s in probe.Starts)
        {
            _output.WriteLine($"tick={s.Tick} ability={s.AbilityId} class={s.ClassId} seatYaw={s.SeatYaw} expectedYaw={s.ExpectedYaw}");
        }

        Assert.True(probe.Starts.Count >= 1,
            $"seat {probe.SeatId} started no targeted cast in {MaxTicks} ticks: the probe is vacuous");

        var bad = probe.Starts.FirstOrDefault(s => s.SeatYaw != s.ExpectedYaw);
        Assert.True(bad is null,
            bad is null
                ? string.Empty
                : $"seat {probe.SeatId} turned to face nothing on cast start: tick {bad.Tick}, ability {bad.AbilityId}, " +
                  $"seat yaw {bad.SeatYaw}, expected yaw {bad.ExpectedYaw} (Fixed.YawBetween(seat, target))");
    }
}
