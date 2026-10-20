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
/// PATH-PACKS on PRESPAWN-AGGRO (BIBLE section 9.1, Matt 2026-10-20 "no more gates, just path to boss with packs" and
/// "not literally spawning enemies on the player"; new behaviour, no TS oracle): on a layout run every trash pack stands
/// idle in the state from tick 0, and on the first tick a living party member stands inside its aggro ring every member
/// engages together, with one <c>pack_engage {packId, actorIds}</c>. A pack is one <see cref="Aggro.GroupsOf"/> group with
/// pull index -1: it engages once, never moves the pull index, never shuts a gate, and its members' scripts run on the
/// pack's own clock from the engage tick.
/// </summary>
public sealed class LayoutPackTests
{
    private const int PackA = 301;
    private const int PackB = 302;
    private const int PackC = 303;
    private const long Ring = 3000;
    private static readonly Vec3 Center = new(15000, 0, 0);
    private static readonly int[] Members = { PackA, PackB, PackC };

    private static readonly Bounds Hall = new(-5000, 30000, -5000, 5000);
    private static readonly Bounds Corridor = new(29000, 36000, -1000, 1000);
    private static readonly Bounds End = new(35000, 50000, -5000, 5000);

    private static NavLayout Layout() => new(
        new[]
        {
            new NavBox("hall", Hall, true, null),
            new NavBox("corridor", Corridor, false, null),
            new NavBox("end", End, true, null),
        },
        new[] { new NavTrigger("p1", new Bounds(37000, 50000, -5000, 5000)) },
        new[] { new Vec3(29500, 0, 0), new Vec3(35500, 0, 0), new Vec3(45000, 0, 0) });

    private static readonly DungeonDef Dungeon = new("packs", new[]
    {
        new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 48000, 0, 10) }),
    });

    private static PackGroup Pack() => new("watch", new[]
    {
        RunTestKit.Spawn(PackA, "dummy", 15000, -1000, 10),
        RunTestKit.Spawn(PackB, "dummy", 15000, 0, 10),
        RunTestKit.Spawn(PackC, "dummy", 15000, 1000, 10),
    }, Center, Ring);

    private static Run Create(IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>>? scripts = null)
    {
        var nuke = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1, "physical") });
        var layout = Layout();
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["nuke"] = nuke }, bounds: layout.Extent) with
        {
            Layout = layout,
            Packs = new[] { Pack() },
            EnemyScripts = scripts ?? new Dictionary<int, IReadOnlyList<ScriptStep>>(),
        };
        return RunRoot.CreateRun(
            1,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 400, 100),
                RunTestKit.Member(2, "chanter", ActorRole.Healer, -2000, 0, 250, 100),
            },
            Dungeon,
            content);
    }

    private static MoveCommand East(int tick, int actor) => new(tick, actor, new Dir2(1000, 0), 0);

    private static IReadOnlyList<int> ActorIds(SimEvent e) =>
        ((JsArray)e.Get("actorIds")!).Items.Select(v => (int)((JsInt)v).Value).ToList();

    // Walks the Warden east until the pack engages (or 300 ticks pass); the Chanter idles in the hall.
    private static (Run Before, Run After, List<SimEvent> Events, IReadOnlyList<SimEvent> EngageTick) WalkIn(Run run)
    {
        var events = new List<SimEvent>();
        for (var i = 0; i < 300; i++)
        {
            var before = run;
            var s = RunRoot.StepRun(run, new Command[] { East(run.State.Tick, 1) });
            events.AddRange(s.Events);
            run = s.Run;
            if (s.Events.Any(e => e.Type == "pack_engage"))
            {
                return (before, run, events, s.Events);
            }
        }
        return (run, run, events, Array.Empty<SimEvent>());
    }

    [Fact(DisplayName = "Path packs: every pack member stands idle in the state from tick 0, where the pack placed it")]
    public void Every_member_stands_idle_from_tick_zero()
    {
        var run = Create();
        Assert.Equal(0, run.State.Tick);
        foreach (var spawn in Pack().Spawns)
        {
            Assert.True(run.State.Actors.TryGetValue(spawn.ActorId, out var a), $"member {spawn.ActorId} is not in the state at tick 0");
            Assert.True(a!.Alive && a.Idle && a.Kind == ActorKind.Enemy, $"member {spawn.ActorId}: alive={a.Alive} idle={a.Idle}");
            Assert.Equal(spawn.Pos, a.Pos);
        }
        // Up to the tick before its engage, the pack is still there, still idle, and has not moved.
        var (before, _, events, engage) = WalkIn(run);
        Assert.NotEmpty(engage);
        Assert.False(Aggro.InRing(before.State.Actors[1].Pos, Center, Ring), "the Warden was already in the ring the tick before");
        Assert.All(Pack().Spawns, s => Assert.True(before.State.Actors[s.ActorId].Idle && before.State.Actors[s.ActorId].Pos == s.Pos));
        Assert.Single(events, e => e.Type == "pack_engage");
    }

    [Fact(DisplayName = "Path packs: no pack member enters the state after tick 0")]
    public void No_member_enters_the_state_later()
    {
        var run = Create();
        var (_, after, _, engage) = WalkIn(run);
        Assert.NotEmpty(engage);
        Assert.Equal(run.State.Actors.Keys.OrderBy(k => k), after.State.Actors.Keys.OrderBy(k => k));
    }

    [Fact(DisplayName = "Path packs: the whole pack engages on the tick a party member steps into its ring")]
    public void The_whole_pack_engages_together()
    {
        var (_, after, _, engage) = WalkIn(Create());
        var e = Assert.Single(engage, x => x.Type == "pack_engage");
        Assert.Equal("watch", ((JsString)e.Get("packId")!).Value);
        Assert.Equal(Members, ActorIds(e));
        Assert.True(Aggro.InRing(after.State.Actors[1].Pos, Center, Ring));
        foreach (var id in Members)
        {
            Assert.True(after.State.Actors.TryGetValue(id, out var a) && a.Alive && !a.Idle && a.Kind == ActorKind.Enemy, $"member {id} did not engage with its pack");
        }
        Assert.True(Pulls.IsEngaged(after.Encounter, "watch"));
    }

    [Fact(DisplayName = "Path packs: a pack is not a pull: no pull_start, no gate, the pull index holds")]
    public void A_pack_is_not_a_pull()
    {
        var (_, after, events, engage) = WalkIn(Create());
        Assert.NotEmpty(engage);
        Assert.DoesNotContain(events, e => e.Type is "pull_start" or "gate_closed" or "pull_clear");
        Assert.Equal(0, after.Encounter.PullIndex);
        Assert.False(Pulls.IsEngaged(after.Encounter, after.Dungeon, 0));
        Assert.True(after.State.Actors[101].Idle, "the pull stayed idle while the pack engaged");
    }

    [Fact(DisplayName = "Path packs: a pack engages once, however long the party stays in its ring")]
    public void A_pack_engages_once()
    {
        var (_, after, events, engage) = WalkIn(Create());
        Assert.NotEmpty(engage);
        var more = RunTestKit.StepUntil(after, after.State.Tick + 60).Events;
        Assert.DoesNotContain(more, e => e.Type == "pack_engage");
        Assert.Single(events, e => e.Type == "pack_engage");
    }

    [Fact(DisplayName = "Path packs: a pack is an aggro group with pull index -1 after the pulls")]
    public void A_pack_is_an_aggro_group_after_the_pulls()
    {
        var groups = Aggro.GroupsOf(Dungeon, 0, new[] { Pack() });
        Assert.Equal(new[] { "p1", "watch" }, groups.Select(g => g.Id));
        var g = groups[1];
        Assert.Equal(-1, g.PullIndex);
        Assert.Equal(Members, g.ActorIds);
        Assert.Equal(Center, g.Center);
        Assert.Equal(Ring, g.AggroMm);
        Assert.Empty(g.Links);
        Assert.Equal(new[] { "p1" }, Aggro.GroupsOf(Dungeon, 0).Select(x => x.Id));
    }

    [Fact(DisplayName = "Path packs: the ring edge is inclusive to the millimetre, dead members and open seats never engage")]
    public void The_ring_edge_is_inclusive_and_only_living_seated_players_count()
    {
        var run = Create();
        var groups = Aggro.GroupsOf(run.Dungeon, run.State.Tick, run.Content.Packs);
        SimState At(long x, bool alive = true)
        {
            var s = run.State.Clone();
            s.Actors[1].Pos = new Vec3(x, 0, 0);
            s.Actors[1].Alive = alive;
            return s;
        }
        IReadOnlyList<string> Hit(SimState s, IReadOnlyList<int>? open = null) => Aggro.Triggered(run.State, s, groups, run.Encounter, open);
        Assert.DoesNotContain("watch", Hit(At(Center.X - Ring - 1)));
        Assert.Contains("watch", Hit(At(Center.X - Ring)));
        Assert.DoesNotContain("watch", Hit(At(Center.X, alive: false)));
        Assert.DoesNotContain("watch", Hit(At(Center.X), new[] { 1 }));
    }

    [Fact(DisplayName = "Path packs: a pack member's script runs on the pack clock from its engage tick")]
    public void Scripts_run_on_the_pack_clock()
    {
        var scripts = new Dictionary<int, IReadOnlyList<ScriptStep>>
        {
            [PackA] = new[] { ScriptStep.At(10, "nuke") },
        };
        var (_, after, _, engage) = WalkIn(Create(scripts));
        Assert.NotEmpty(engage);
        var engageTick = engage.First(e => e.Type == "pack_engage").Tick;
        Assert.True(engageTick > 10, $"engage tick {engageTick} must come after the unshifted cast tick for the check to mean anything");
        Assert.NotNull(after.ActiveScripts);
        Assert.Equal(engageTick + 10, Assert.Single(after.ActiveScripts![PackA]).AtTick);
    }
}
