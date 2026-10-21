using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Finding f-854228f4: the pull_start form <c>A@pullN+0</c> of <see cref="SeatAbandonPlan"/> on the real run (seed 1,
/// key 2, the host's party and bot gear as SimDriver.StartRun builds it), stepped in SimDriver.StepOnce's order: the
/// abandon check at the pre-step tick, then the step, then the anchor from that step's events. Its run is also the
/// oracle RunFAbandonPacingTests holds its double to (f-5813f5d2).
/// </summary>
public sealed class SeatAbandonZeroOffsetTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int MaxTicks = 3500;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public SeatAbandonZeroOffsetTests(ITestOutputHelper output) => _output = output;

    /// <summary>What one real run with an abandon plan did.</summary>
    /// <param name="PullStart">pull 0's pull_start event tick, or null when it never started.</param>
    /// <param name="EmittedAtPreTick">The pre-step tick of the step whose events carried that pull_start.</param>
    /// <param name="Abandon">The pre-step tick the seat emptied at, or null when it never did.</param>
    /// <param name="Stopped">The run tick the loop stopped at.</param>
    internal sealed record RealRun(int? PullStart, int? EmittedAtPreTick, int? Abandon, int Stopped);

    /// <summary>Runs the real sim (seed 1, key 2, the host's party, ScriptedWarden seat 1, bots elsewhere) with
    /// <paramref name="abandon"/> in SimDriver.StepOnce's order until the seat empties or the run ends.</summary>
    /// <param name="abandon">The <c>--abandon-seat</c> plan text.</param>
    /// <returns>What the run did.</returns>
    internal static RealRun RunReal(string abandon)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed);
        var book = BackfillState.Empty;
        var plan = SeatAbandonPlan.Parse(abandon);
        int? anchor = null;
        int? abandonTick = null;
        int? pullStart = null;
        int? emittedAt = null;

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null && abandonTick is null)
        {
            var tick = run.State.Tick;
            if (tick == plan.ResolveTick(anchor))
            {
                book = Backfill.Abandon(book, run, plan.ActorId);
                abandonTick = tick;
            }
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, book.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            var stepped = Backfill.Step(book, run, commands, new List<QueueRequest>());
            book = stepped.Backfill;
            if (pullStart is null && stepped.Step.Events.FirstOrDefault(e => e.Type == "pull_start" && e.Get("pullIndex") is JsInt { Value: 0 }) is { } ps)
            {
                pullStart = ps.Tick;
                emittedAt = tick;
            }
            anchor ??= plan.AnchorAt(tick, stepped.Step.Events, stepped.Step.Run.Dungeon, stepped.Step.Run.State.Actors);
            run = stepped.Step.Run;
        }
        return new RealRun(pullStart, emittedAt, abandonTick, run.State.Tick);
    }

    [Fact(DisplayName = "A@pull0+0 abandons the seat on pull 0's pull_start tick")]
    public void Zero_offset_abandons_on_the_pull_start_tick()
    {
        var real = RunReal("2@pull0+0");
        _output.WriteLine($"pull_start event tick={real.PullStart?.ToString() ?? "none"} emitted by the step whose pre-step tick={real.EmittedAtPreTick?.ToString() ?? "none"}");
        _output.WriteLine($"pull0 pull_start={real.PullStart?.ToString() ?? "none"} abandon={real.Abandon?.ToString() ?? "never"} stopped at tick {real.Stopped}");
        Assert.True(real.PullStart is not null, "pull 0 never started");
        Assert.Equal(real.PullStart, real.Abandon);
    }
}
