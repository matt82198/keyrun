using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// PACK-CLEAR (R-WOW-2, event seq 857, item 1; WoW M+ credits forces per pack death). Every trash pack gets its own clear
/// event: the tick the LAST living member of a pack dies the sim emits exactly one <c>pack_clear</c> naming the pack
/// (<c>{tick, type:"pack_clear", packId}</c>). Today only Pulls.CheckPullAdvance emits <c>pull_clear</c>, and only for the
/// scripted active pull, so a pack the party kills out of order emits nothing. Here p2 is killed while the active pull
/// index is still 0 (p1 untouched): the old path stays silent, so these tests are red until the emit exists.
/// </summary>
public sealed class PackClearTests
{
    private static readonly Bounds Entrance = new(-5000, 5000, -5000, 5000);
    private static readonly Bounds Corridor1 = new(4000, 12000, -1000, 1000);
    private static readonly Bounds Room1 = new(11000, 25000, -5000, 5000);
    private static readonly Bounds Corridor2 = new(24000, 30000, -1000, 1000);
    private static readonly Bounds Room2 = new(29000, 40000, -5000, 5000);

    private static NavLayout Layout() => new(
        new[]
        {
            new NavBox("entrance", Entrance, true, null),
            new NavBox("corridor-1", Corridor1, false, null),
            new NavBox("room-1", Room1, true, null),
            new NavBox("corridor-2", Corridor2, false, null),
            new NavBox("room-2", Room2, true, null),
        },
        new[] { new NavTrigger("p1", new Bounds(13000, 25000, -5000, 5000)), new NavTrigger("p2", new Bounds(31000, 40000, -5000, 5000)) },
        new[] { new Vec3(4500, 0, 0), new Vec3(11500, 0, 0), new Vec3(24500, 0, 0), new Vec3(29500, 0, 0), new Vec3(35000, 0, 0) });

    // p1 (101, 102) is never touched; p2 (201, 202) is killed out of order. One nuke (1000) kills a 1000-health member.
    private static Run CreateRun()
    {
        var dungeon = new DungeonDef("nav", new[]
        {
            new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 22000, 2000, 1000), RunTestKit.Spawn(102, "dummy", 22000, -2000, 1000) }),
            new PullDef("p2", new[] { RunTestKit.Spawn(201, "dummy", 38000, 2000, 1000), RunTestKit.Spawn(202, "dummy", 38000, -2000, 1000) }),
        });
        var nuke = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1000, "physical") });
        var layout = Layout();
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["nuke"] = nuke }, bounds: layout.Extent) with { Layout = layout };
        return RunRoot.CreateRun(
            1,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 4000, 100),
                RunTestKit.Member(2, "chanter", ActorRole.Healer, -2000, 0, 2500, 100),
            },
            dungeon,
            content);
    }

    private static (Run Run, IReadOnlyList<SimEvent> Events) Step(Run run, params Command[] commands)
    {
        var r = RunRoot.StepRun(run, commands);
        return (r.Run, r.Events);
    }

    private static string PackId(SimEvent e) => ((JsString)e.Get("packId")!).Value;

    [Fact(DisplayName = "Pack clear: no pack_clear while any member of the pack lives, then exactly one on the tick the last dies")]
    public void Last_member_death_emits_exactly_one_pack_clear()
    {
        var run = CreateRun();
        var events = new List<SimEvent>();

        var first = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 201));
        events.AddRange(first.Events);
        Assert.Contains(first.Events, e => e.Type == "death");
        Assert.False(first.Run.State.Actors[201].Alive);
        Assert.True(first.Run.State.Actors[202].Alive);
        Assert.DoesNotContain(events, e => e.Type == "pack_clear");

        run = first.Run;
        for (var i = 0; i < 5; i++)
        {
            var idle = Step(run);
            events.AddRange(idle.Events);
            run = idle.Run;
            Assert.DoesNotContain(events, e => e.Type == "pack_clear");
        }

        var last = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 202));
        events.AddRange(last.Events);
        Assert.False(last.Run.State.Actors[202].Alive);
        var lastDeath = Assert.Single(last.Events, e => e.Type == "death");
        var clear = Assert.Single(events, e => e.Type == "pack_clear");
        Assert.Equal("p2", PackId(clear));
        Assert.Equal(lastDeath.Tick, clear.Tick);

        run = last.Run;
        for (var i = 0; i < 20; i++)
        {
            var after = Step(run);
            events.AddRange(after.Events);
            run = after.Run;
        }
        Assert.Single(events, e => e.Type == "pack_clear");
    }

    [Fact(DisplayName = "Pack clear: a pack never engaged emits no pack_clear")]
    public void An_unengaged_pack_emits_no_pack_clear()
    {
        var run = CreateRun();
        var events = new List<SimEvent>();
        var killed = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 201));
        events.AddRange(killed.Events);
        run = killed.Run;
        for (var i = 0; i < 60; i++)
        {
            var s = Step(run);
            events.AddRange(s.Events);
            run = s.Run;
        }
        Assert.True(run.State.Actors[101].Alive);
        Assert.True(run.State.Actors[102].Alive);
        Assert.DoesNotContain(events, e => e.Type == "pack_clear" && PackId(e) == "p1");

        // Anti-vacuity: finishing p2 emits its own clear, and still none for the untouched p1.
        var finished = Step(run, new CastCommand(run.State.Tick, 1, "nuke", 202));
        events.AddRange(finished.Events);
        var clear = Assert.Single(events, e => e.Type == "pack_clear");
        Assert.Equal("p2", PackId(clear));
    }
}
