using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// CARELESS-SEAT (Matt 2026-10-20, verbatim: "no a bad player will accidentally aggro more groups, just test it
/// naturally"). A careless bot seat (<see cref="Careless.Wrap"/> over a real <see cref="RoleSet.Default"/> bot) strays off
/// the party toward a named prespawned group from a given tick, walks into that group's aggro ring, engages it through
/// the sim's own aggro rule (<see cref="Aggro.Triggered"/>: a living party member in the ring), and the group, with no
/// threat on anyone, goes for the nearest living player (<see cref="ScriptTarget"/>), which is the seat that pulled it.
/// The hollow-vault k2 oracle setup is placed on the real layout with the host's three seats at the Entrance; seat 1 (the
/// leader) stands still, so the only way the Choir engages is a seat walking into its ring. The control drives the same
/// run with the plain bot in seat 3: it follows its formation slot and nothing engages.
/// </summary>
public sealed class CarelessSeatTests
{
    private const long Seed = 1;
    private const int CarelessId = 3;

    /// <summary>The tick the careless seat starts to stray (the Choir is idle and the party stands at the Entrance).</summary>
    private const int FromTick = 20;

    /// <summary>The Choir must engage within this many ticks of the run start.</summary>
    private const int EngageWithinTicks = 600;

    /// <summary>A Choir enemy must damage the careless seat within this many ticks of the engage (10 s at 20 Hz).</summary>
    private const int HitWithinTicks = 200;

    private static readonly int[] BotIds = { 2, 3 };

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the drive's ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public CarelessSeatTests(ITestOutputHelper output) => _output = output;

    private sealed record Drive(
        int? EngageTick,
        IReadOnlyDictionary<int, Vec3> AtEngage,
        Vec3 CarelessAtFrom,
        IReadOnlyList<SimEvent> Events,
        SimDungeon Dungeon);

    private static long Int(SimEvent e, string key) => ((JsInt)e.Get(key)!).Value;

    private static Drive DriveRun(bool careless)
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        Assert.Equal(new[] { "warden", "chanter", "cinder" }, party.OrderBy(p => p.Id).Select(p => p.ClassId).ToArray());
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);
        Assert.False(Pulls.IsEngaged(run.Encounter, dungeon, 0), "the Choir engaged at tick 0");

        var kits = KitRegistry.From(registry);
        var choirIds = dungeon.Pulls[0].Spawns.Select(s => s.ActorId).ToList();
        var bots = BotIds.ToDictionary(id => id, _ => Bot.MakeBot(RoleSet.Default));
        if (careless)
        {
            bots[CarelessId] = Careless.Wrap(bots[CarelessId], new CarelessPlan(FromTick, choirIds));
        }
        IReadOnlyDictionary<int, BotMemory> mem = BotIds.ToDictionary(id => id, _ => BotMemory.Default(80));
        var prng = Skill.SeedPrng(Seed);

        int? engage = null;
        IReadOnlyDictionary<int, Vec3> atEngage = new Dictionary<int, Vec3>();
        var carelessAtFrom = run.State.Actors[CarelessId].Pos;
        var events = new List<SimEvent>();
        while (run.State.Tick < EngageWithinTicks && (engage is null || run.State.Tick < engage.Value + HitWithinTicks))
        {
            if (run.State.Tick == FromTick)
            {
                carelessAtFrom = run.State.Actors[CarelessId].Pos;
            }
            // Seat 1, the leader, stands still: no command for it. Seats 2 and 3 are bots, as BotCommandSource runs them.
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var stepped = RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            events.AddRange(stepped.Events);
            if (engage is null && stepped.Events.Any(e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == dungeon.Pulls[0].Id))
            {
                engage = run.State.Tick;
                atEngage = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player).ToDictionary(a => a.Id, a => a.Pos);
            }
        }
        return new Drive(engage, atEngage, carelessAtFrom, events, dungeon);
    }

    private static IEnumerable<SimEvent> ChoirHitsOn(Drive d, int targetId)
    {
        var choirIds = d.Dungeon.Pulls[0].Spawns.Select(s => (long)s.ActorId).ToHashSet();
        return d.Events.Where(e => e.Type == "damage" && choirIds.Contains(Int(e, "sourceId")) && Int(e, "targetId") == targetId && Int(e, "healthDamage") > 0);
    }

    [Fact(DisplayName = "Careless seat: strays toward the idle Choir from its tick, walks into the Choir's aggro ring alone, pulls it, and the Choir damages that seat")]
    public void A_careless_seat_pulls_the_group_it_wanders_into_and_takes_its_damage()
    {
        var d = DriveRun(careless: true);
        var choir = d.Dungeon.Pulls[0];
        var center = Pulls.RingCenter(choir);
        var hits = ChoirHitsOn(d, CarelessId).ToList();
        _output.WriteLine($"careless seat {CarelessId} at tick {FromTick}: ({d.CarelessAtFrom.X},{d.CarelessAtFrom.Z}); Choir engage tick {d.EngageTick?.ToString() ?? "none"}");
        foreach (var (id, p) in d.AtEngage.OrderBy(kv => kv.Key))
        {
            _output.WriteLine($"  at engage: seat {id} ({p.X},{p.Z}) inRing={Aggro.InRing(p, center, choir.AggroMm)}");
        }
        _output.WriteLine($"Choir hits on seat {CarelessId}: {hits.Count}, first at tick {hits.FirstOrDefault()?.Tick.ToString() ?? "none"}");

        // Before its tick the seat plays normally: it is still with the party at the Entrance, short of the Choir's room.
        // 10000 mm is the choir-room's west wall (content/data/layout.json); literal on purpose.
        Assert.True(d.CarelessAtFrom.X < 10000, $"the careless seat stood at ({d.CarelessAtFrom.X},{d.CarelessAtFrom.Z}) at tick {FromTick}, already in the Choir's room");

        // It walks into the ring, and the sim's aggro rule engages the Choir on it alone (the others are nowhere near).
        Assert.True(d.EngageTick is not null, $"the Choir never engaged within {EngageWithinTicks} ticks: the careless seat did not walk into its ring");
        Assert.True(d.EngageTick >= FromTick, $"the Choir engaged at tick {d.EngageTick}, before the seat strayed (tick {FromTick})");
        Assert.True(Aggro.InRing(d.AtEngage[CarelessId], center, choir.AggroMm), "the careless seat was not in the Choir's ring on its engage tick");
        Assert.All(new[] { 1, 2 }, id => Assert.False(Aggro.InRing(d.AtEngage[id], center, choir.AggroMm), $"seat {id} stood in the Choir's ring on its engage tick"));

        // The engaged Choir goes for the seat that pulled it (nearest living player, no threat on anyone): it takes damage.
        Assert.True(hits.Count > 0, $"no Choir enemy damaged the careless seat within {HitWithinTicks} ticks of the engage at tick {d.EngageTick}");
        Assert.True(hits[0].Tick <= d.EngageTick + HitWithinTicks, $"the first Choir hit on the careless seat came at tick {hits[0].Tick}, more than {HitWithinTicks} ticks after the engage at {d.EngageTick}");
    }

    [Fact(DisplayName = "Careless seat control: the same seat without the plan follows its formation slot, and nothing engages or hits it")]
    public void Without_the_plan_the_seat_follows_and_nothing_engages()
    {
        var d = DriveRun(careless: false);
        Assert.Null(d.EngageTick);
        Assert.DoesNotContain(d.Events, e => e.Type == "pull_start");
        Assert.Empty(ChoirHitsOn(d, CarelessId));
    }
}
