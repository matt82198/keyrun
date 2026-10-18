using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Tools.Replay;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Port of tests/sim/integration/break-bar.test.ts: the Sundered Twins' shared Break bar drains off interrupt/control
/// tagged casts and stuns both twins through createRun/stepRun alone (RunContent.BreakBarSources, the stepRun
/// effect-patch application). The TS reads loadContent(); the C# reads the same abilities and effects from the
/// hollow-vault setup (ORACLE.md section 4: content exactly as the TS run held it) and the Twins' scripts and bar from
/// content/data/enemies/hollow-warden.json.
/// </summary>
public sealed class RunBreakBarTests
{
    private const long Seed = 424242;
    private const int Ticks = 30;
    private const int WardenA = 1;
    private const int ChanterA = 2;
    private const int CinderA = 3;
    private const int Boss = 2000;
    private const int TwinA = 2001;
    private const int TwinB = 2002;

    private static Run BuildRun()
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("sundered-twins", new[]
            {
                new PullSpawn(Boss, "hollowWarden.boss", new Vec3(0, 0, 0), 0, 60000),
                new PullSpawn(TwinA, "hollowWarden.twinA", new Vec3(500, 0, 0), 0, 6000),
                new PullSpawn(TwinB, "hollowWarden.twinB", new Vec3(-500, 0, 0), 0, 6000),
            }),
        });
        var bossScript = EncounterJson.ParseScript(JsValue.Parse(
            "[{\"at\":\"loop\",\"every\":60,\"cast\":\"hollowWarden.slam\"},{\"at\":\"loop\",\"every\":160,\"cast\":\"hollowWarden.cleave\",\"telegraph\":{\"shape\":\"cone\",\"radius\":4000,\"angle1024\":200,\"leadTicks\":40}},{\"at\":\"loop\",\"every\":600,\"cast\":\"hollowWarden.lock\"}]"));
        var twinScript = EncounterJson.ParseScript(JsValue.Parse("[{\"at\":\"loop\",\"every\":60,\"cast\":\"twins.slam\"}]"));
        var bar = new BreakBarSource("twinsBreak", new[] { TwinA, TwinB }, 100, 34, 20, new[] { new BreakBarOnEmptyEffect("stunned", 100, 1) });

        var content = setup.Content with
        {
            Bounds = new Keyrun.Sim.Combat.Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Boss] = bossScript, [TwinA] = twinScript, [TwinB] = twinScript },
            BreakBarSources = new[] { bar },
            KeyLevel = null,
            ParTicks = null,
            Clauses = null,
        };
        var party = new[]
        {
            RunTestKit.Member(WardenA, "warden", ActorRole.Tank, 400, 100, 2000, 150),
            RunTestKit.Member(ChanterA, "chanter", ActorRole.Healer, -400, 100, 2000, 1),
            RunTestKit.Member(CinderA, "cinder", ActorRole.Damage, 400, -100, 2000, 150),
        };
        return RunRoot.CreateRun(Seed, party, dungeon, content);
    }

    private static readonly Command[] Scripted =
    {
        new CastCommand(2, WardenA, "warden.shieldBash", TwinA),
        new CastCommand(4, ChanterA, "chanter.rebuke", TwinB),
        new CastCommand(6, CinderA, "cinder.quench", TwinA),
    };

    [Fact]
    public void Drains_the_shared_bar_off_tagged_casts_and_stuns_both_twins_when_it_empties()
    {
        var (run, events) = RunTestKit.StepUntil(BuildRun(), Ticks, Scripted);

        var stagger = Assert.Single(events, e => e.Type == "stagger");
        Assert.Contains("\"actorIds\":[2001,2002]", stagger.ToCanonicalJson());
        Assert.Contains("\"barId\":\"twinsBreak\"", stagger.ToCanonicalJson());
        Assert.Equal(0, run.Encounter.BreakBars["twinsBreak"].Current);
        Assert.Contains(run.State.Actors[TwinA].Effects, e => e.EffectId == "stunned");
        Assert.Contains(run.State.Actors[TwinB].Effects, e => e.EffectId == "stunned");
    }

    [Fact]
    public void Never_drains_the_bar_with_zero_tagged_casts()
    {
        var (run, events) = RunTestKit.StepUntil(BuildRun(), Ticks, Array.Empty<Command>());

        Assert.DoesNotContain(events, e => e.Type == "stagger");
        Assert.Equal(100, run.Encounter.BreakBars["twinsBreak"].Current);
        Assert.DoesNotContain(run.State.Actors[TwinA].Effects, e => e.EffectId == "stunned");
    }
}
