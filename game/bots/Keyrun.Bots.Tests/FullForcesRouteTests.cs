using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
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
/// FULL-FORCES-ROUTE (BIBLE s9.2 and s9.5; unit DOOR-100): a default host-driven run (<see cref="HostRunSetup.Build"/>
/// at key 2, seed 1, the bots in the other seats, the autorun Warden as the human) with REAL health, no weakening, reaches
/// the Enemy Forces bar's full requirement before any party member enters the Warden arena, and it does so by the bots
/// pulling at least one OPTIONAL layout pack on the way. The teaching pulls and the required pack do not fill the bar on
/// their own; the optional packs (cantor-vigil, warrior-remnant) are what lift it to 100%.
/// </summary>
public sealed class FullForcesRouteTests
{
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";
    private const int Key = 2;
    private const long Seed = 1;
    private const int MaxTicks = 24000;
    private const string ArenaBoxId = "warden-arena";

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    /// <summary>
    /// One whole default run up to the arena entry: the packs engaged in order (with the tick and the banked points as each
    /// engaged), the points banked when the first living party member stood in the arena, and whether it did.
    /// </summary>
    private sealed record RouteRun(
        IReadOnlyList<string> PackOrder,
        IReadOnlyList<string> Trace,
        bool ArenaReached,
        int ArenaTick,
        int PointsAtArena,
        int Required);

    private static readonly Lazy<RouteRun> Route = new(Drive);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the pack order and the bar at arena entry are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public FullForcesRouteTests(ITestOutputHelper output) => _output = output;

    private static RouteRun Drive()
    {
        var registry = Registry.Value;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var points = registry.Forces.Values.ToDictionary(v => v.EnemyId, v => v.Points, StringComparer.Ordinal);
        var arena = run.Content.Layout!.Boxes.First(b => string.Equals(b.Id, ArenaBoxId, StringComparison.Ordinal));

        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();

        var backfill = BackfillState.Empty;
        var order = new List<string>();
        var trace = new List<string>();
        int? arenaTick = null;
        var pointsAtArena = 0;
        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null && arenaTick is null)
        {
            var commands = new List<Command>();
            // SimDriver.StepOnce order: the human's commands first, then every bot seat's.
            warden.Sample(run.State.Tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;

            foreach (var e in step.Step.Events.Where(e => e.Type == "pack_engage"))
            {
                var packId = ((JsString)e.Get("packId")!).Value;
                order.Add(packId);
                trace.Add($"{packId}@t{e.Tick}");
            }

            if (run.State.Actors.Values.Any(a => a.Kind == ActorKind.Player && a.Alive && Movement.Contains(arena.Box, a.Pos)))
            {
                arenaTick = run.State.Tick;
                pointsAtArena = Forces.Points(run.State, points);
            }
        }

        return new RouteRun(order, trace, arenaTick is not null, arenaTick ?? -1, pointsAtArena, registry.Forces.Required);
    }

    [Fact(DisplayName = "FULL-FORCES-ROUTE: a default host run reaches the full forces bar before the arena by pulling an optional pack")]
    public void A_default_host_run_reaches_the_full_forces_bar_before_the_arena()
    {
        var r = Route.Value;
        var optional = Registry.Value.Layout.Packs.Where(p => p.Optional).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var summary = $"pack order [{string.Join(", ", r.Trace)}]; points at arena entry {r.PointsAtArena}/{r.Required} "
            + $"(arena reached {r.ArenaReached} at tick {r.ArenaTick})";

        _output.WriteLine(summary);
        Assert.True(r.ArenaReached, summary + "; no party member reached warden-arena within the tick budget");
        Assert.True(r.PackOrder.Any(optional.Contains), summary + "; no optional pack was pulled on the way");
        Assert.True(r.PointsAtArena >= r.Required, summary + "; the bar is short of the requirement at arena entry");
    }
}
