using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// A party seat its player left must not finish a cast on the leave tick (WoW: a player who leaves is gone, and their
/// cast does not land). The defect: <c>Rules</c> completes in-flight casts before the command loop, and the open seat's
/// stopcast is appended after by <see cref="Backfill"/>, so a seat abandoned on the tick its cast ends still resolves
/// that cast (playtest RunFJoinerActsTests: "chanter.hymn@1310"). The fixture is one seat casting a 20-tick cast at a
/// dummy enemy; the abandon is placed relative to the tick the cast ends (<c>CastEndsTick</c>), read from the run.
/// </summary>
public sealed class BackfillLeaveTickCastTests
{
    private const long Seed = 30011;
    private const int RunKey = 10;
    private const int Tank = 1;
    private const int Seat = 2;
    private const int Enemy = 100;
    private const string CastAbility = "burst";
    private const int CastTicks = 20;
    private const int EndTick = 60;

    private static Run BuildRun()
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            [CastAbility] = RunTestKit.Ability(CastAbility, new EffectOp[] { new DamageOp(1000, "physical") }) with { CastTicks = CastTicks },
        };
        var content = RunTestKit.Content(abilities, lockoutTicks: 0) with { KeyLevel = RunKey };
        var pulls = new List<PullDef> { new("p0", new[] { RunTestKit.Spawn(Enemy, "dummy", 3000, 0, 100000) }) };
        return RunRoot.CreateRun(
            Seed,
            new[]
            {
                RunTestKit.Member(Tank, "warden", ActorRole.Tank, 0, 0, 500, 100),
                RunTestKit.Member(Seat, "cinder", ActorRole.Damage, -700, 0, 500, 100),
            },
            new DungeonDef("cast-leave-vault", pulls),
            content);
    }

    private sealed record Drive(int? CastEnd, int? AbandonTick, List<SimEvent> Events);

    /// <summary>
    /// Starts the seat's cast on tick 0, then (when <paramref name="abandonOffset"/> is set) abandons the seat at
    /// <c>CastEndsTick + offset</c>. The queue is empty, so no refill can end the cast: only the leave can.
    /// </summary>
    private static Drive DriveCast(int? abandonOffset)
    {
        var run = BuildRun();
        var book = BackfillState.Empty;
        var queue = new List<QueueRequest>();
        var events = new List<SimEvent>();
        int? castEnd = null;
        int? abandonTick = null;
        var issued = false;
        while (run.State.Tick < EndTick)
        {
            var tick = run.State.Tick;
            var seat = run.State.Actors[Seat];
            if (castEnd is null && seat.CastId is not null)
            {
                castEnd = seat.CastEndsTick;
            }
            if (abandonOffset is { } offset && abandonTick is null && castEnd is { } end && tick == end + offset)
            {
                book = Backfill.Abandon(book, run, Seat);
                abandonTick = tick;
            }
            var commands = new List<Command>();
            if (!issued)
            {
                commands.Add(new CastCommand(tick, Seat, CastAbility, Enemy));
                issued = true;
            }
            var step = Backfill.Step(book, run, commands, queue);
            book = step.Backfill;
            events.AddRange(step.Step.Events);
            run = step.Step.Run;
        }
        return new Drive(castEnd, abandonTick, events);
    }

    private static long ActorOf(SimEvent e) => e.Get("actorId") is JsInt id ? id.Value : -1;

    private static List<SimEvent> SeatCastCompletesFrom(Drive d, int tick) =>
        d.Events.Where(e => e.Type == "cast_complete" && ActorOf(e) == Seat && e.Tick >= tick).ToList();

    [Fact(DisplayName = "Backfill leave tick: a seat abandoned on the tick its cast ends does not resolve that cast")]
    public void A_seat_abandoned_on_the_tick_its_cast_ends_does_not_resolve_that_cast()
    {
        var d = DriveCast(abandonOffset: 0);

        Assert.NotNull(d.CastEnd);
        Assert.NotNull(d.AbandonTick);
        Assert.Equal(d.CastEnd, d.AbandonTick);
        var resolved = SeatCastCompletesFrom(d, d.AbandonTick!.Value);
        Assert.True(
            resolved.Count == 0,
            $"the seat {Seat} was abandoned at tick {d.AbandonTick} (its cast ends at {d.CastEnd}) and still resolved "
            + string.Join(", ", resolved.Select(e => $"{(e.Get("abilityId") as JsString)?.Value}@{e.Tick}")));
    }

    [Theory(DisplayName = "Backfill leave tick: a seat abandoned one tick before or after its cast ends resolves no cast from the abandon on")]
    [InlineData(-1)]
    [InlineData(1)]
    public void A_seat_abandoned_one_tick_either_side_of_its_cast_end_resolves_no_cast_from_the_abandon_on(int offset)
    {
        var d = DriveCast(abandonOffset: offset);

        Assert.NotNull(d.CastEnd);
        Assert.NotNull(d.AbandonTick);
        var resolved = SeatCastCompletesFrom(d, d.AbandonTick!.Value);
        Assert.True(
            resolved.Count == 0,
            $"the seat {Seat} was abandoned at tick {d.AbandonTick} (its cast ends at {d.CastEnd}) and resolved "
            + string.Join(", ", resolved.Select(e => $"{(e.Get("abilityId") as JsString)?.Value}@{e.Tick}")));
    }
}
