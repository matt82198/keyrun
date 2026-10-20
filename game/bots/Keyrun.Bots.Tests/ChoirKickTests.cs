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
/// BOT-CHOIR-KICK (KF2; design/BIBLE.md section 9: "The Choir - two melee and a caster lieutenant whose 4-second heal
/// cast must be kicked; teaches the kick ledger"). The Godot run's shape, headless: the hollow-vault k2 oracle setup
/// placed into the real layout, the host's three seats (Warden 1 human, Chanter 2 and Cinder 3 bots, as
/// HostRunSetup.BuildParty seats them) and the real bot roles, exactly as BotCommandSource drives them. The human
/// walks the route into the Choir and then stands idle, issuing nothing. Every Choral Mending the Cantor starts is
/// counted, and every one the bots kick.
/// </summary>
public sealed class ChoirKickTests
{
    private const long Seed = 1;
    private const int HumanId = 1;
    private const int MaxTicks = 6000;
    private const string HealCast = "choir.healCast";
    private static readonly int[] BotIds = { 2, 3 };

    private sealed record Tally(int Started, int KickedByBots, int Completed, int ClearTick, List<string> Log);

    private static readonly Lazy<Tally> Driven = new(DriveChoir);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the counts are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public ChoirKickTests(ITestOutputHelper output) => _output = output;

    private static Tally DriveChoir()
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
        var bots = BotIds.ToDictionary(id => id, _ => Bot.MakeBot(RoleSet.Default));
        IReadOnlyDictionary<int, BotMemory> mem = BotIds.ToDictionary(id => id, _ => BotMemory.Default(80));
        var prng = Skill.SeedPrng(Seed);

        var started = 0;
        var kicked = 0;
        var completed = 0;
        var clearTick = -1;
        var inChoir = false;
        var log = new List<string>();
        var casting = new HashSet<int>();

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null && clearTick < 0)
        {
            var tick = run.State.Tick;
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var commands = batch.Commands.ToList();
            // The human: walks the route to the Choir, then idles (no command at all) once the pull starts.
            if (!inChoir && run.State.Actors[HumanId] is { Alive: true } human && Follow.LeadRoute(tick, human, run) is { } walk)
            {
                commands.Add(walk);
            }

            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "choir")
                {
                    inChoir = true;
                    log.Add($"{e.Tick} pull_start choir");
                }
                else if (e.Type == "pull_clear" && ((JsString)e.Get("pullId")!).Value == "choir")
                {
                    clearTick = e.Tick;
                }
                else if (e.Type == "interrupt" && ((JsString)e.Get("abilityId")!).Value == HealCast)
                {
                    var src = ((JsInt)e.Get("sourceId")!).Value;
                    if (BotIds.Contains((int)src))
                    {
                        kicked++;
                    }
                    log.Add($"{e.Tick} interrupt by {src}");
                }
                else if (e.Type == "cast_complete" && ((JsString)e.Get("abilityId")!).Value == HealCast)
                {
                    completed++;
                    log.Add($"{e.Tick} heal completed");
                }
            }
            // A cast start is a Cantor whose CastId turned to Choral Mending this step.
            foreach (var a in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy))
            {
                var now = a.Alive && a.CastId == HealCast;
                if (now && !casting.Contains(a.Id))
                {
                    started++;
                    log.Add($"{run.State.Tick} heal started by {a.Id}");
                }
                if (now)
                {
                    casting.Add(a.Id);
                }
                else
                {
                    casting.Remove(a.Id);
                }
            }
        }
        return new Tally(started, kicked, completed, clearTick, log);
    }

    [Fact(DisplayName = "Choir kick: with the human idle, the bots kick at least every other Choral Mending the Cantor starts")]
    public void Bots_kick_the_choir_heal_cast_with_the_human_idle()
    {
        var t = Driven.Value;
        foreach (var line in t.Log)
        {
            _output.WriteLine(line);
        }
        _output.WriteLine($"CHOIR started={t.Started} kickedByBots={t.KickedByBots} completed={t.Completed} clearTick={t.ClearTick}");
        // Non-vacuity: the Cantor must have started casting, or a kick count of zero would mean nothing.
        Assert.True(t.Started >= 2, $"the Cantor started {t.Started} heal casts; the check would be vacuous");
        // Two bot kicks (Rebuke, Quench) each on a 480-tick cooldown against a heal every 200 ticks: the pair can cover
        // at least half the casts, so a ledger that works lands a kick on every other cast or better.
        Assert.True(t.KickedByBots * 2 >= t.Started, $"bots kicked {t.KickedByBots} of {t.Started} Choral Mendings ({t.Completed} completed)");
    }
}
