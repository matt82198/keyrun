using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// SLAM-TELEGRAPH (BIBLE s3: "Every dangerous enemy action is a cast with a visible bar and a floor telegraph (circle,
/// cone, line) drawn ahead of resolution"). The Hollow Warden's slam and the Sundered Twins' slam, scripted exactly as
/// content/data/enemies/hollow-warden.json holds them (Tempo-scaled to key 2, as the host scales them), run on the
/// armed boss run through <see cref="RunRoot.StepRun"/>. Each slam must be a real cast (nonzero castTicks, the cast
/// bar) whose floor telegraph is on the floor for the whole cast and resolves on the tick the slam's damage lands, so
/// the circle the player reads is still there when the hit arrives.
/// </summary>
public sealed class SlamTelegraphTests
{
    private const int Key = 2;
    private const int Ticks = 900;
    private const string BossSlam = "hollowWarden.slam";
    private const string TwinSlam = "twins.slam";

    private static List<JsObject> ContentEnemies() =>
        ((JsArray)JsValue.Parse(File.ReadAllText(Path.Combine(
            ContentRoots.RepoRootAbove(OracleFixtures.Directory), "content", "data", "enemies", "hollow-warden.json")))).Items
            .Cast<JsObject>().ToList();

    private static IReadOnlyList<ScriptStep> ContentScript(List<JsObject> enemies, string id) =>
        Tempo.ScaleScript(EncounterJson.ParseScript(enemies.Single(e => ((JsString)e.Get("id")!).Value == id).Get("script")!), Key);

    /// <summary>The BossTestKit run with the boss and Twin scripts taken from content/data instead of the TS setup.</summary>
    private static Run BuildRun()
    {
        var enemies = ContentEnemies();
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("boss", new[] { new PullSpawn(BossTestKit.Boss, "hollowWarden.boss", new Vec3(0, 0, 0), 512, 60000) }),
        });
        var content = setup.Content with
        {
            Bounds = new Keyrun.Sim.Combat.Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [BossTestKit.Boss] = ContentScript(enemies, "hollowWarden.boss") },
            BreakBarSources = null,
            KeyLevel = null,
            ParTicks = null,
            Clauses = null,
        };
        var party = new[]
        {
            RunTestKit.Member(BossTestKit.Warden, "warden", ActorRole.Tank, 0, 1500, BossTestKit.PartyHealth, 100),
            RunTestKit.Member(BossTestKit.Chanter, "chanter", ActorRole.Healer, 500, 2000, BossTestKit.PartyHealth, 1),
            RunTestKit.Member(BossTestKit.Cinder, "cinder", ActorRole.Damage, -500, 2000, BossTestKit.PartyHealth, 100),
        };
        var run = RunRoot.CreateRun(BossTestKit.Seed, party, dungeon, content);
        var kit = BossTestKit.Config();
        var config = kit with
        {
            Twins = new[]
            {
                kit.Twins[0] with { Script = ContentScript(enemies, "hollowWarden.twinA") },
                kit.Twins[1] with { Script = ContentScript(enemies, "hollowWarden.twinB") },
            },
        };
        return run with { Encounter = BossRules.Arm(run.Encounter, config) };
    }

    /// <summary>Steps tick by tick, recording every event and the telegraph registry left pending after each tick.</summary>
    private static (List<SimEvent> Events, Dictionary<int, IReadOnlyList<Telegraph>> Pending, Run Run) Play()
    {
        var run = BuildRun();
        var events = new List<SimEvent>();
        var pending = new Dictionary<int, IReadOnlyList<Telegraph>>();
        while (run.State.Tick < Ticks)
        {
            var tick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            pending[tick] = run.Encounter.Telegraphs;
        }
        return (events, pending, run);
    }

    private static long Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    /// <summary>
    /// For every <paramref name="cast"/> by <paramref name="actor"/> that lands (cast_complete): a telegraph resolves on
    /// the landing tick, and it is on the floor (pending in the registry) for the last <c>min(castTicks, lead)</c> ticks
    /// before it, where lead is the step's Tempo-scaled telegraph lead. With lead &gt;= castTicks that is the whole cast
    /// bar; Tempo may shrink the lead below the cast time (BIBLE s8), never the warning before the hit.
    /// </summary>
    private static void AssertSpansCast(
        List<SimEvent> events, Dictionary<int, IReadOnlyList<Telegraph>> pending, Run run, int actor, string cast,
        IReadOnlyList<ScriptStep> script, int minLanded, bool requireDamage)
    {
        var castTicks = run.Content.Abilities[cast].CastTicks;
        Assert.True(castTicks > 0, $"{cast}: castTicks {castTicks}, no cast bar");
        var lead = script.Single(s => s.Cast == cast).Telegraph?.LeadTicks ?? 0;
        Assert.True(lead > 0, $"{cast}: no floor telegraph in the script");
        var window = Math.Min(castTicks, lead);

        var landed = events.Where(e => e.Type == "cast_complete" && Int(e, "actorId") == actor && Str(e, "abilityId") == cast).ToList();
        Assert.True(landed.Count >= minLanded, $"{cast} by {actor}: {landed.Count} casts landed, expected at least {minLanded}");
        foreach (var done in landed)
        {
            var hitTick = done.Tick;
            if (requireDamage)
            {
                Assert.Contains(events, e => e.Type == "damage" && e.Tick == hitTick && Int(e, "sourceId") == actor);
            }

            var telegraph = events.SingleOrDefault(e => e.Type == "telegraph" && Int(e, "actorId") == actor && Str(e, "cast") == cast && Int(e, "resolvesTick") == hitTick);
            Assert.True(telegraph is not null, $"{cast} by {actor} landed on tick {hitTick} with no telegraph resolving on that tick");
            Assert.True(telegraph!.Tick <= hitTick - window, $"{cast} by {actor}: telegraph drawn on tick {telegraph.Tick}, later than {hitTick - window}");

            for (var t = hitTick - window; t < hitTick; t++)
            {
                Assert.True(
                    pending[t].Any(tg => tg.SourceId == actor && tg.AbilityId == cast && tg.ResolvesTick == hitTick),
                    $"{cast} by {actor}: no floor telegraph on tick {t}, {hitTick - t} ticks before it lands on {hitTick}");
            }
        }
    }

    [Fact(DisplayName = "The Warden's slam and both Twins' slams keep their floor telegraph down for the whole cast and resolve it on the damage tick")]
    public void Slam_telegraphs_span_the_cast_and_resolve_with_the_damage()
    {
        var (events, pending, run) = Play();
        var enemies = ContentEnemies();
        AssertSpansCast(events, pending, run, BossTestKit.Boss, BossSlam, ContentScript(enemies, "hollowWarden.boss"), 2, requireDamage: true);
        AssertSpansCast(events, pending, run, BossTestKit.TwinA, TwinSlam, ContentScript(enemies, "hollowWarden.twinA"), 2, requireDamage: true);
        AssertSpansCast(events, pending, run, BossTestKit.TwinB, TwinSlam, ContentScript(enemies, "hollowWarden.twinB"), 2, requireDamage: true);
    }

    [Fact(DisplayName = "The Warden's Cleave keeps its cone telegraph down until the cleave lands")]
    public void Cleave_telegraph_spans_the_cast()
    {
        var (events, pending, run) = Play();
        AssertSpansCast(events, pending, run, BossTestKit.Boss, "hollowWarden.cleave", ContentScript(ContentEnemies(), "hollowWarden.boss"), 3, requireDamage: false);
    }

    private const int SeedActor = 108;

    /// <summary>A Seed-only run (k2 oracle content, the Seed's script from content/data/enemies/seed.json), the party in lash range.</summary>
    private static (List<SimEvent> Events, Dictionary<int, IReadOnlyList<Telegraph>> Pending, Run Run, IReadOnlyList<ScriptStep> Script) PlaySeed()
    {
        var path = Path.Combine(ContentRoots.RepoRootAbove(OracleFixtures.Directory), "content", "data", "enemies", "seed.json");
        var seedDef = ((JsArray)JsValue.Parse(File.ReadAllText(path))).Items.Cast<JsObject>().Single(e => ((JsString)e.Get("id")!).Value == "seed.elite");
        var script = Tempo.ScaleScript(EncounterJson.ParseScript(seedDef.Get("script")!), Key);
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("seed", new[] { new PullSpawn(SeedActor, "seed.elite", new Vec3(0, 0, 0), 512, 60000) }),
        });
        var content = setup.Content with
        {
            Bounds = new Keyrun.Sim.Combat.Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [SeedActor] = script },
            BreakBarSources = null,
            KeyLevel = null,
            ParTicks = null,
            Clauses = null,
        };
        var party = new[]
        {
            RunTestKit.Member(BossTestKit.Warden, "warden", ActorRole.Tank, 0, 1000, BossTestKit.PartyHealth, 100),
            RunTestKit.Member(BossTestKit.Chanter, "chanter", ActorRole.Healer, 500, 1000, BossTestKit.PartyHealth, 1),
            RunTestKit.Member(BossTestKit.Cinder, "cinder", ActorRole.Damage, -500, 1000, BossTestKit.PartyHealth, 100),
        };
        var run = RunRoot.CreateRun(BossTestKit.Seed, party, dungeon, content);
        var events = new List<SimEvent>();
        var pending = new Dictionary<int, IReadOnlyList<Telegraph>>();
        while (run.State.Tick < Ticks)
        {
            var tick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            pending[tick] = run.Encounter.Telegraphs;
        }
        return (events, pending, run, script);
    }

    [Fact(DisplayName = "The Seed's slam and lash keep their floor telegraph down until they land and resolve it on the damage tick")]
    public void Seed_slam_and_lash_telegraphs_span_the_cast()
    {
        var (events, pending, run, script) = PlaySeed();
        AssertSpansCast(events, pending, run, SeedActor, "seed.slam", script, 2, requireDamage: true);
        AssertSpansCast(events, pending, run, SeedActor, "seed.lash", script, 2, requireDamage: true);
    }
}
