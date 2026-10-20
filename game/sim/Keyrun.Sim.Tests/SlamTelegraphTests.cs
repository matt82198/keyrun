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

    [Fact(DisplayName = "The Warden's slam and both Twins' slams keep their floor telegraph down for the whole cast and resolve it on the damage tick")]
    public void Slam_telegraphs_span_the_cast_and_resolve_with_the_damage()
    {
        var (events, pending, run) = Play();
        var casts = new (int Actor, string Slam)[] { (BossTestKit.Boss, BossSlam), (BossTestKit.TwinA, TwinSlam), (BossTestKit.TwinB, TwinSlam) };
        foreach (var (actor, slam) in casts)
        {
            var castTicks = run.Content.Abilities[slam].CastTicks;
            Assert.True(castTicks > 0, $"{slam}: castTicks {castTicks}, no cast bar");

            var landed = events.Where(e => e.Type == "cast_complete" && Int(e, "actorId") == actor && Str(e, "abilityId") == slam).ToList();
            Assert.True(landed.Count >= 2, $"{slam} by {actor}: {landed.Count} casts landed in {Ticks} ticks, expected at least 2");
            foreach (var done in landed)
            {
                var hitTick = done.Tick;
                Assert.Contains(events, e => e.Type == "damage" && e.Tick == hitTick && Int(e, "sourceId") == actor);

                var telegraph = events.SingleOrDefault(e => e.Type == "telegraph" && Int(e, "actorId") == actor && Str(e, "cast") == slam && Int(e, "resolvesTick") == hitTick);
                Assert.True(telegraph is not null, $"{slam} by {actor} landed on tick {hitTick} with no telegraph resolving on that tick");
                Assert.True(telegraph!.Tick <= hitTick - castTicks, $"{slam} by {actor}: telegraph drawn on tick {telegraph.Tick}, after its cast began on {hitTick - castTicks}");

                for (var t = hitTick - castTicks; t < hitTick; t++)
                {
                    Assert.True(
                        pending[t].Any(tg => tg.SourceId == actor && tg.AbilityId == slam && tg.ResolvesTick == hitTick),
                        $"{slam} by {actor}: no floor telegraph on tick {t}, {hitTick - t} ticks before its damage on {hitTick}");
                }
            }
        }
    }
}
