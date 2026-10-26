using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// SLAM-TELEGRAPH (spansCast) on the LIVE content path: the Hollow Warden's slam telegraph stays drawn on the floor from
/// the tick its cast starts until the cast lands, unhasted (fact 1) and Chill-hasted (fact 2). The run and scripts are
/// built as <c>SlamTelegraphTests.BuildRun</c> builds them: the k2 oracle setup for the base content, with the boss and
/// Twin scripts replaced by the live content/data/enemies/hollow-warden.json scripts (Tempo-scaled to key 2). Each fact
/// precondition-asserts the boss slam step carries a SpansCast telegraph, so the test cannot silently run the replay
/// path (the oracle setup forces SpansCast=false). Everything runs through <see cref="RunRoot.StepRun"/>.
/// </summary>
public sealed class SpannedTelegraphHasteTests
{
    private const int Key = 2;
    private const string Slam = "hollowWarden.slam";
    private const int WatchTicks = 2400;
    private const string Message = "a spansCast telegraph stays drawn until its cast lands";

    private static List<JsObject> LiveEnemies() =>
        ((JsArray)JsValue.Parse(File.ReadAllText(Path.Combine(
            ContentRoots.RepoRootAbove(OracleFixtures.Directory), "content", "data", "enemies", "hollow-warden.json")))).Items
            .Cast<JsObject>().ToList();

    private static IReadOnlyList<ScriptStep> LiveScript(List<JsObject> enemies, string id) =>
        Tempo.ScaleScript(EncounterJson.ParseScript(enemies.Single(e => ((JsString)e.Get("id")!).Value == id).Get("script")!), Key);

    /// <summary>The SlamTelegraphTests.BuildRun run: the armed boss pull with the live boss and Twin scripts.</summary>
    private static Run BuildLiveRun()
    {
        var enemies = LiveEnemies();
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("boss", new[] { new PullSpawn(BossTestKit.Boss, "hollowWarden.boss", new Vec3(0, 0, 0), 512, 60000) }),
        });
        var content = setup.Content with
        {
            Bounds = new Keyrun.Sim.Combat.Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [BossTestKit.Boss] = LiveScript(enemies, "hollowWarden.boss") },
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
                kit.Twins[0] with { Script = LiveScript(enemies, "hollowWarden.twinA") },
                kit.Twins[1] with { Script = LiveScript(enemies, "hollowWarden.twinB") },
            },
        };
        return run with { Encounter = BossRules.Arm(run.Encounter, config) };
    }

    /// <summary>Fails unless the boss's slam step in the live run's script carries a SpansCast floor telegraph.</summary>
    private static void AssertLiveSpansCast(Run run)
    {
        var step = run.Content.EnemyScripts[BossTestKit.Boss].Single(s => s.Cast == Slam);
        Assert.True(step.Telegraph is { SpansCast: true }, $"{Message}: the live {Slam} step has no SpansCast telegraph (replay path, not live content)");
    }

    private sealed record Watch(int? DrawTick, int? LandTick, int? ResolveTick, List<int> Undrawn);

    /// <summary>
    /// Steps the run and watches the first boss slam: the first tick its telegraph is in the encounter registry, the tick
    /// the slam cast lands (the boss's CastEndsTick), the first tick after the draw the telegraph is gone, and every tick
    /// from the draw through the landing tick inclusive where it is not drawn.
    /// </summary>
    private static Watch WatchFirstSlam(Run run, bool chilled)
    {
        if (chilled)
        {
            Assert.True(run.Content.Effects.TryGetValue("chill", out var chill), $"{Message}: content has no chill effect");
            Effects.ApplyEffectInstance(run.State.Actors[BossTestKit.Boss], chill!, BossTestKit.Boss, WatchTicks, 1, run.State.Tick);
        }

        var drawn = new HashSet<int>();
        int? landTick = null;
        while (run.State.Tick < WatchTicks)
        {
            run = RunRoot.StepRun(run, new List<Command>()).Run;
            var tick = run.State.Tick;
            if (run.Encounter.Telegraphs.Any(t => t.SourceId == BossTestKit.Boss && t.AbilityId == Slam))
            {
                drawn.Add(tick);
            }
            var boss = run.State.Actors[BossTestKit.Boss];
            if (landTick is null && boss.CastId == Slam)
            {
                landTick = boss.CastEndsTick;
            }
        }

        if (drawn.Count == 0 || landTick is null)
        {
            return new Watch(null, landTick, null, new List<int>());
        }
        var drawTick = drawn.Min();
        int? resolveTick = null;
        for (var t = drawTick; t < WatchTicks; t++)
        {
            if (!drawn.Contains(t))
            {
                resolveTick = t;
                break;
            }
        }
        var undrawn = Enumerable.Range(drawTick, Math.Max(0, landTick.Value - drawTick + 1))
            .Where(t => !drawn.Contains(t))
            .ToList();
        return new Watch(drawTick, landTick, resolveTick, undrawn);
    }

    [Fact(DisplayName = "Live content, unhasted: the Warden's slam telegraph stays drawn until the slam cast lands")]
    public void UnhastedSpannedSlamTelegraphStaysDrawnUntilItsCastLands()
    {
        var run = BuildLiveRun();
        AssertLiveSpansCast(run);
        var w = WatchFirstSlam(run, chilled: false);

        Assert.True(w.LandTick is not null, $"{Message}: the boss never started {Slam}");
        Assert.True(w.DrawTick is not null, $"{Message}: no slam telegraph was drawn before its cast landed at {w.LandTick}");
        Assert.True(w.Undrawn.Count == 0,
            $"{Message}: the telegraph was not drawn on every tick from its first draw {w.DrawTick} through the landing tick {w.LandTick} inclusive; undrawn ticks {string.Join(",", w.Undrawn)}");
    }

    [Fact(DisplayName = "Live content, Chill-hasted: the Warden's slam telegraph stays drawn until the hasted slam cast lands")]
    public void HastedSpannedSlamTelegraphStaysDrawnUntilTheHastedCastLands()
    {
        var run = BuildLiveRun();
        AssertLiveSpansCast(run);
        var w = WatchFirstSlam(run, chilled: true);

        Assert.True(w.LandTick is not null, $"{Message}: the boss never started {Slam}");
        Assert.True(w.DrawTick is not null, $"{Message}: no slam telegraph was drawn before its hasted cast landed at {w.LandTick}");
        Assert.True(w.Undrawn.Count == 0,
            $"{Message}: the telegraph was gone at tick {w.ResolveTick} but the hasted cast lands at tick {w.LandTick}; undrawn ticks {string.Join(",", w.Undrawn)}");
    }
}
