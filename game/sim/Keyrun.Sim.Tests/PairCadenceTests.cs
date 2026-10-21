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
/// The Pair's teaching cadence (design/BIBLE.md section 9, "The Pair": "every 20 s one player is marked to
/// <i>spread</i> (a ring: stand apart), ten seconds later the pack calls a <i>stack</i> (a circle: stand together,
/// damage split)"). At 20 Hz that is a spread cast starting every 400 ticks and the stack call starting 200 ticks
/// after the first spread. Nothing pinned that offset before this test.
/// <para>
/// Drives a real host run (<see cref="RunRoot.StepRun"/>) with the Pair's enemy script as content/data/enemies/pair.json
/// ships it, the same composition as <see cref="TelegraphOnlyWhenCastStartsTests"/>, and reads each mark cast's start
/// tick from the live actor (<c>CastStartTick</c>, the tick its cast bar begins).
/// </para>
/// </summary>
public sealed class PairCadenceTests
{
    private const int ActorId = 500;
    private const int Ticks = 1200;
    private const string Spread = "pair.spreadMark";
    private const string Stack = "pair.stackCall";

    /// <summary>BIBLE section 9: the stack call follows the spread by ten seconds, 200 ticks at 20 Hz.</summary>
    private const int StackAfterSpreadTicks = 200;

    [Fact]
    public void The_first_stack_call_begins_exactly_200_ticks_after_the_first_spread_mark()
    {
        var starts = FirstCastStarts(PairScript());

        Assert.True(starts.ContainsKey(Spread), "no pair.spreadMark cast started in the watch");
        Assert.True(starts.ContainsKey(Stack), "no pair.stackCall cast started in the watch");
        Assert.Equal(StackAfterSpreadTicks, starts[Stack] - starts[Spread]);
    }

    /// <summary>
    /// The pair.json script as shipped, parsed with the encounter reader the host uses. Read from disk so the test
    /// fails on the content itself, not on a copy of it.
    /// </summary>
    private static IReadOnlyList<ScriptStep> PairScript()
    {
        var path = Path.Combine(ContentRoots.RepoRootAbove(OracleFixtures.Directory), "content", "data", "enemies", "pair.json");
        var enemy = ((JsArray)JsValue.Parse(File.ReadAllText(path))).Items.Cast<JsObject>()
            .Single(e => ((JsString)e.Get("id")!).Value == "pair.melee");
        return EncounterJson.ParseScript(enemy.Get("script")!);
    }

    /// <summary>
    /// One Pair warrior at the origin with the party well clear of it, stepped <see cref="Ticks"/> ticks with no party
    /// commands. Returns the tick each mark cast's cast bar first began, keyed by ability id.
    /// </summary>
    private static Dictionary<string, int> FirstCastStarts(IReadOnlyList<ScriptStep> script)
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("solo", new[] { new PullSpawn(ActorId, "pair.melee", new Vec3(0, 0, 0), 512, 100_000_000) }),
        });
        var content = setup.Content with
        {
            Bounds = new Keyrun.Sim.Combat.Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [ActorId] = script },
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
        var starts = new Dictionary<string, int>();
        while (run.State.Tick < Ticks)
        {
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
            if (run.State.Actors[ActorId] is { CastId: { } castId, CastStartTick: { } castStart })
            {
                starts.TryAdd(castId, castStart);
            }
        }
        return starts;
    }
}
