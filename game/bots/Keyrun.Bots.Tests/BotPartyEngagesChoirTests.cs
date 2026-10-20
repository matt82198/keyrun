using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// BOTS-ENGAGE-CHOIR (demo verdict KF2, finding f-210c3939: "bots idle in the first room"; design/BIBLE.md section 9:
/// "The Choir - two melee and a caster lieutenant whose 4-second heal cast must be kicked; teaches the kick ledger").
/// The default party (the hollow-vault k2 oracle setup's Warden 1, Chanter 2 and Cinder 3) placed at the Entrance of
/// the real layout at full enemy health, with EVERY seat a real bot (<see cref="RoleSet.Default"/> through
/// <see cref="Bot.RunBots"/>, as BotCommandSource drives a bot seat) and nothing scripted: no seat walks the route for
/// them. The party must reach the Choir, start the pull, put damage on a Choir enemy soon after, and kick at least one
/// Choral Mending.
/// </summary>
public sealed class BotPartyEngagesChoirTests
{
    /// <summary>The Choir pull must start within this many ticks of the run start (30 s at 20 Hz).</summary>
    public const int PullStartWithinTicks = 600;

    /// <summary>A party member must damage a Choir enemy within this many ticks of the Choir's pull_start (5 s).</summary>
    public const int EngageWithinTicks = 100;

    private const long Seed = 1;
    private const int MaxTicks = 6000;
    private const string HealCast = "choir.healCast";
    private static readonly int[] PartyIds = { 1, 2, 3 };

    private sealed record Tally(int PullStartTick, int FirstHitTick, int Kicks, int HealsStarted, Vec3 LeaderEnd, List<string> Log);

    private static readonly Lazy<Tally> Driven = new(DriveParty);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the trace is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public BotPartyEngagesChoirTests(ITestOutputHelper output) => _output = output;

    private static Tally DriveParty()
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

        var kits = KitRegistry.From(registry);
        var bots = PartyIds.ToDictionary(id => id, _ => Bot.MakeBot(RoleSet.Default));
        IReadOnlyDictionary<int, BotMemory> mem = PartyIds.ToDictionary(id => id, _ => BotMemory.Default(80));
        var prng = Skill.SeedPrng(Seed);

        var choirIds = new HashSet<long>(dungeon.Pulls[0].Spawns.Select(s => (long)s.ActorId));
        var pullStart = -1;
        var firstHit = -1;
        var kicks = 0;
        var started = 0;
        var clear = -1;
        var casting = new HashSet<int>();
        var log = new List<string>();

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null && clear < 0)
        {
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;

            var stepped = RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                switch (e.Type)
                {
                    case "room_enter":
                        log.Add($"{e.Tick} room_enter {((JsInt)e.Get("actorId")!).Value} {((JsString)e.Get("room")!).Value}");
                        break;
                    case "pull_start" when ((JsString)e.Get("pullId")!).Value == "choir":
                        pullStart = e.Tick;
                        log.Add($"{e.Tick} pull_start choir");
                        break;
                    case "pull_clear" when ((JsString)e.Get("pullId")!).Value == "choir":
                        clear = e.Tick;
                        log.Add($"{e.Tick} pull_clear choir");
                        break;
                    case "damage" when firstHit < 0 && PartyIds.Contains((int)((JsInt)e.Get("sourceId")!).Value)
                        && choirIds.Contains(((JsInt)e.Get("targetId")!).Value):
                        firstHit = e.Tick;
                        log.Add($"{e.Tick} first party damage on a Choir enemy by {((JsInt)e.Get("sourceId")!).Value}");
                        break;
                    case "interrupt" when ((JsString)e.Get("abilityId")!).Value == HealCast:
                        var src = ((JsInt)e.Get("sourceId")!).Value;
                        if (PartyIds.Contains((int)src))
                        {
                            kicks++;
                        }
                        log.Add($"{e.Tick} interrupt {HealCast} by {src}");
                        break;
                }
            }
            foreach (var a in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy))
            {
                var now = a.Alive && a.CastId == HealCast;
                if (now && casting.Add(a.Id))
                {
                    started++;
                    log.Add($"{run.State.Tick} heal started by {a.Id}");
                }
                else if (!now)
                {
                    casting.Remove(a.Id);
                }
            }
        }
        return new Tally(pullStart, firstHit, kicks, started, run.State.Actors[1].Pos, log);
    }

    [Fact(DisplayName = "Bot party: with no human leading, the bots walk into the Choir, start it, engage it and kick its heal")]
    public void Bot_party_engages_the_choir_and_kicks_its_heal()
    {
        var t = Driven.Value;
        foreach (var line in t.Log)
        {
            _output.WriteLine(line);
        }
        _output.WriteLine($"CHOIR pullStart={t.PullStartTick} firstHit={t.FirstHitTick} healsStarted={t.HealsStarted} kicks={t.Kicks} leaderEnd=({t.LeaderEnd.X},{t.LeaderEnd.Z})");
        Assert.True(t.PullStartTick >= 0 && t.PullStartTick <= PullStartWithinTicks,
            $"the Choir pull started at tick {t.PullStartTick} (want 0..{PullStartWithinTicks}); the leader ended at ({t.LeaderEnd.X},{t.LeaderEnd.Z})");
        Assert.True(t.FirstHitTick >= 0 && t.FirstHitTick - t.PullStartTick <= EngageWithinTicks,
            $"first party damage on a Choir enemy at tick {t.FirstHitTick}, pull_start {t.PullStartTick} (want within {EngageWithinTicks})");
        // Non-vacuity: the Cantor must have started a heal, or a kick count would mean nothing.
        Assert.True(t.HealsStarted >= 1, $"the Cantor started {t.HealsStarted} Choral Mendings; the kick check would be vacuous");
        Assert.True(t.Kicks >= 1, $"the party kicked {t.Kicks} of {t.HealsStarted} Choral Mendings");
    }
}
