using System;
using System.IO;
using System.Linq;
using Keyrun.Host;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// HUMAN-PLAY (D65/D66) proof without launching: each per-class input log (game/fixtures/input/human-&lt;class&gt;.input-log.json,
/// the logs the orchestrator replays in the real game and HumanPlay-SceneTest.ps1 replays headless in CI game-e2e) is
/// played by <see cref="HumanReplay"/> for its first minute (1200 ticks). Screen: Down picks the class (0, 1 or 2 presses
/// from the Warden), Enter queues; dungeon: W walks seat 1 down the route into the Choir's aggro ring, Tab targets the
/// Choir Cantor, G pings it, 1-5 cast the rotation and Q kicks the Cantor's heal.
/// </summary>
public sealed class HumanReplayTests(ITestOutputHelper output)
{
    private const int FirstMinuteTicks = 1200;

    private static string Fixture(string classId)
    {
        var rel = $"game/fixtures/input/human-{classId}.input-log.json";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, rel)))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, $"{rel} not found above {AppContext.BaseDirectory}");
        return Path.Combine(dir!.FullName, rel);
    }

    [Fact(DisplayName = "Tab cycles the engaged enemies in a fight (never an idle pack in another room), and only enemies within 40 m out of one")]
    public void Tab_cycles_the_fight_not_the_dungeon()
    {
        System.Collections.Generic.IReadOnlyList<int>? atStart = null;
        System.Collections.Generic.IReadOnlyList<int>? inFight = null;
        long farthest = 0;
        HumanReplay.Play(Fixture("cinder"), 120, null, run =>
        {
            var me = run.State.Actors[HostRunSetup.HumanId];
            var ids = TabTargeting.Candidates(run.State, me).Select(a => a.Id).OrderBy(id => id).ToList();
            if (run.State.Tick == 1)
            {
                atStart = ids;
                farthest = TabTargeting.Candidates(run.State, me).Max(a => ((long)(a.Pos.X - me.Pos.X) * (a.Pos.X - me.Pos.X)) + ((long)(a.Pos.Z - me.Pos.Z) * (a.Pos.Z - me.Pos.Z)));
            }
            if (run.State.Tick == 100)
            {
                inFight = ids;
            }
        });
        Assert.NotNull(atStart);
        Assert.NotEmpty(atStart!);
        Assert.True(farthest <= TabTargeting.RangeMm * TabTargeting.RangeMm);
        // The Choir (pull 0: 100, 101, 102) is engaged at tick 78; the Pair Hall's idle melee (103-107) are never Tab targets.
        Assert.Equal(new[] { 100, 101, 102 }, inFight);
    }

    [Theory(DisplayName = "Each class's input log: the screen picks the class, Queue starts the run with seat 1 as that class, and the Choir engages and is fought")]
    [InlineData("warden")]
    [InlineData("chanter")]
    [InlineData("cinder")]
    public void Input_log_plays_the_first_minute_as_its_class(string classId)
    {
        var r = HumanReplay.Play(Fixture(classId), FirstMinuteTicks);
        var human = r.Run.State.Actors[HostRunSetup.HumanId];
        var choir = r.Events.FirstOrDefault(e => e.Type == "pull_start" && e.Get("pullId") is JsString { Value: "choir" });
        var cantors = r.Setup.Dungeon.Pulls[0].Spawns.Where(s => s.ClassId == "choir.lieutenant").Select(s => s.ActorId).ToHashSet();
        var casts = r.Events.Count(e => e.Type == "cast_complete" && e.Get("actorId") is JsInt { Value: HostRunSetup.HumanId });
        var kicks = r.Events.Where(e => e.Type == "interrupt" && e.Get("sourceId") is JsInt { Value: HostRunSetup.HumanId } &&
            e.Get("targetId") is JsInt t && cantors.Contains((int)t.Value)).Select(e => e.Tick).ToList();
        output.WriteLine($"{classId}: queued on frame {r.QueueFrame}, choir pull_start tick={choir?.Tick}, seat-1 casts={casts}, kicks on the Cantor at ticks {string.Join(",", kicks)}, pings={string.Join(",", r.Pings)}");

        Assert.Equal(classId, r.ClassId);
        Assert.Equal(classId, human.ClassId);
        Assert.Equal(new[] { "warden", "chanter", "cinder" }.Where(c => c != classId), r.Setup.BotActorIds.Select(id => r.Run.State.Actors[id].ClassId));
        Assert.NotNull(choir);
        Assert.True(casts >= 10, $"{classId}: seat 1 completed {casts} casts in the first minute (want >= 10)");
        Assert.NotEmpty(kicks);
        Assert.NotEmpty(r.Pings);
    }
}
