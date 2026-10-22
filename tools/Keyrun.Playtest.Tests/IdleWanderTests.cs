using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Idle wander (finding f-c1044aa4, extends f-f0c886eb and R-PATROL-4; Matt 2026-10-22, verbatim: "the mobs need to move
/// around in their aggro area while not aggro'd, or they look harmless"). On the shipped Hollow Vault at key 2 every
/// un-aggroed, non-boss pack and pull walks deterministically inside its own aggro radius: it moves, it never leaves its
/// own ring, it never walks into another group's ring, it stays on walkable floor, the same seed gives the same walk tick
/// for tick, and a wandering enemy never wakes its pack earlier than a standing one (the personal ring and the assist
/// radius are measured from where the enemy stands at rest, never from where it wandered to). Properties of the real
/// content, not trajectory pins (R-PINS-1).
/// </summary>
public sealed class IdleWanderTests
{
    public static IEnumerable<object[]> Seeds => Enumerable.Range(1, 3).Select(s => new object[] { (long)s });

    /// <summary>Ticks the party stands at the entrance, far outside every ring, while the packs idle (60 s at 20 Hz).</summary>
    private const int IdleTicks = 1200;

    /// <summary>Ticks the walking seat waits outside every radius before it walks in (the pack is mid-wander when it arrives).</summary>
    private const int WaitTicks = 300;

    private readonly ITestOutputHelper _out;

    public IdleWanderTests(ITestOutputHelper output) => _out = output;

    private static readonly HostRunSetup Setup = HostRunSetup.Build(ContentLoader.Load(), "hollow-vault", 2, "warden", withBots: true);

    // The groups that wander: packs and pulls, never the boss, never the room-gated Seed, never a patrol (it has a route).
    private static IReadOnlyList<AggroGroup> Wanderers() =>
        Aggro.GroupsOf(Setup.Dungeon, 0, Setup.Content.Packs, Setup.Content.Layout, null)
            .Where(g => !g.Id.StartsWith("boss", StringComparison.Ordinal) && g.ActivateRoom is null)
            .ToList();

    private static long D2(Vec3 a, Vec3 b) => ((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z));

    // Steps IdleTicks with nobody moving; returns each member's position per tick (index 0 = after the first step) plus events.
    private static (Dictionary<int, List<Vec3>> Walk, List<SimEvent> Events) Idle(long seed, RunContent? content = null)
    {
        var run = RunRoot.CreateRun(seed, Setup.Party, Setup.Dungeon, content ?? Setup.Content, null);
        var ids = Wanderers().SelectMany(g => g.ActorIds).ToList();
        var walk = ids.ToDictionary(id => id, _ => new List<Vec3>());
        var events = new List<SimEvent>();
        for (var t = 0; t < IdleTicks; t++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            events.AddRange(stepped.Events);
            foreach (var id in ids)
            {
                walk[id].Add(run.State.Actors[id].Pos);
            }
        }
        return (walk, events);
    }

    [Theory(DisplayName = "Idle wander: before the party arrives every member of every non-boss pack moves, and never leaves its own aggro radius, enters another group's, or leaves the floor")]
    [MemberData(nameof(Seeds))]
    public void Every_idle_member_moves_inside_its_own_ring_only(long seed)
    {
        var groups = Wanderers();
        Assert.True(groups.Count >= 3, $"the shipped Hollow Vault has at least three wandering groups, got {groups.Count}");
        var (walk, events) = Idle(seed);
        var grid = NavGrid.Of(Setup.Content.Layout!);
        // Every static ring counts as "another pack's aggro radius": the boss and the Seed included.
        var rings = Aggro.GroupsOf(Setup.Dungeon, 0, Setup.Content.Packs, Setup.Content.Layout, null);
        Assert.DoesNotContain(events, e => e.Type is "pull_start" or "pack_engage");
        foreach (var g in groups)
        {
            foreach (var id in g.ActorIds)
            {
                var path = walk[id];
                var moves = Enumerable.Range(1, path.Count - 1).Count(i => path[i] != path[i - 1]);
                _out.WriteLine($"seed {seed} {g.Id} actor {id}: moved on {moves} of {path.Count - 1} ticks, farthest from ring centre {path.Max(p => Fixed.Isqrt(D2(p, g.Center)))} of {g.AggroMm} mm");
                if (IdleWander.HoldsPost(Setup.Content, id))
                {
                    // A kickable caster holds its post (A_kickable_caster_holds_its_post); every other member walks.
                    Assert.True(moves == 0, $"{g.Id} actor {id} holds its post but moved on {moves} idle ticks");
                }
                else
                {
                    Assert.True(moves > 0, $"{g.Id} actor {id} never moved in {IdleTicks} idle ticks");
                }
                for (var i = 0; i < path.Count; i++)
                {
                    Assert.True(D2(path[i], g.Center) <= g.AggroMm * g.AggroMm, $"{g.Id} actor {id} left its aggro radius at tick {i + 1}: {path[i]} centre {g.Center} r {g.AggroMm}");
                    Assert.True(grid.Walkable(path[i]), $"{g.Id} actor {id} stood off the floor at tick {i + 1}: {path[i]}");
                    foreach (var other in rings.Where(o => o.Id != g.Id))
                    {
                        Assert.True(D2(path[i], other.Center) > other.AggroMm * other.AggroMm, $"{g.Id} actor {id} walked into {other.Id}'s aggro radius at tick {i + 1}: {path[i]}");
                    }
                }
            }
        }
    }

    [Theory(DisplayName = "Idle wander: a wandering pack wakes on the same tick a standing one does, never earlier, for a seat walking up to each pack")]
    [MemberData(nameof(Seeds))]
    public void A_wandering_pack_never_aggros_earlier_than_a_standing_one(long seed)
    {
        var standing = Setup.Content with { IdleWander = false };
        Assert.True(Setup.Content.IdleWander, "the shipped host assembly turns idle wander on");
        var grid = NavGrid.Of(Setup.Content.Layout!);
        var tested = 0;
        foreach (var g in Wanderers())
        {
            // The Warrior Remnant stands in the Seed's room, whose room gate wakes it from anywhere in the chamber: no approach
            // isolates it, so it is skipped here (its walk is still held to the ring and floor by the first property).
            if (ApproachFrom(g, grid) is not { } start)
            {
                _out.WriteLine($"seed {seed} {g.Id}: no isolated approach, skipped");
                continue;
            }
            tested++;
            var wandering = FirstEngage(seed, Setup.Content, g, start);
            var still = FirstEngage(seed, standing, g, start);
            _out.WriteLine($"seed {seed} {g.Id}: first engage tick standing {still}, wandering {wandering}, party walks in from {start}");
            Assert.NotNull(still);
            Assert.NotNull(wandering);
            Assert.True(wandering >= still, $"{g.Id} woke at tick {wandering} wandering but {still} standing");
            Assert.True(still > WaitTicks, $"{g.Id} woke at tick {still}, before the seat walked: the wait proved nothing");
        }
        Assert.True(tested >= 4, $"only {tested} packs had an isolated approach");
    }

    // The first of eight approaches (west first) whose start, 1500 mm outside the pack's ring, and whole straight line in to
    // the ring centre are walkable floor, so a seat walking it is clamped by nothing.
    private static Vec3? ApproachFrom(AggroGroup g, NavGrid grid)
    {
        // The farthest any member's own sight reaches from the ring centre, so the start is outside every radius and the
        // seat can wait there while the pack wanders.
        var probe = RunRoot.CreateRun(1, Setup.Party, Setup.Dungeon, Setup.Content, null);
        var reach = g.AggroMm;
        foreach (var id in g.ActorIds)
        {
            var a = probe.State.Actors[id];
            reach = Math.Max(reach, Fixed.Isqrt(D2(a.Pos, g.Center)) + (a.AggroMm ?? 0));
        }
        foreach (var (dx, dz) in new (long, long)[] { (-1, 0), (0, -1), (0, 1), (1, 0), (-1, -1), (-1, 1), (1, -1), (1, 1) })
        {
            var norm = Fixed.Isqrt(((dx * dx) + (dz * dz)) * 1_000_000);
            var far = reach + 1500;
            var start = new Vec3(g.Center.X + (dx * 1000 * far / norm), 0, g.Center.Z + (dz * 1000 * far / norm));
            // Outside every other group's ring too (and the Seed's room), so only this pack can be what wakes.
            var alone = Aggro.GroupsOf(Setup.Dungeon, 0, Setup.Content.Packs, Setup.Content.Layout, null)
                .Where(o => o.Id != g.Id)
                .All(o => o.ActivateRoom is { } room ? !Keyrun.Sim.Combat.Movement.Contains(room, start) : D2(start, o.Center) > (o.AggroMm + 1500) * (o.AggroMm + 1500));
            var clear = alone && Enumerable.Range(0, 51).All(i => grid.Walkable(new Vec3(start.X + ((g.Center.X - start.X) * i / 50), 0, start.Z + ((g.Center.Z - start.Z) * i / 50))));
            if (clear)
            {
                return start;
            }
        }
        return null;
    }

    // Seat 0 stands at `start` and walks at the ring centre; every other seat stays at the entrance. Returns the first tick the
    // group engages (pull_start or pack_engage naming it), or null within 1500 ticks.
    private static int? FirstEngage(long seed, RunContent content, AggroGroup g, Vec3 start)
    {
        var run = RunRoot.CreateRun(seed, Setup.Party, Setup.Dungeon, content, null);
        var seat = Setup.Party[0].Id;
        run.State.Actors[seat].Pos = start;
        for (var t = 0; t < 1500; t++)
        {
            // The seat waits WaitTicks outside every radius (the pack idles and wanders), then walks in.
            var move = t < WaitTicks ? null : NavFollow.Toward(run.State.Tick, seat, run.State.Actors[seat].Pos, g.Center);
            var stepped = RunRoot.StepRun(run, move is null ? Array.Empty<Command>() : new Command[] { move });
            run = stepped.Run;
            var hit = stepped.Events.FirstOrDefault(e => (e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == g.Id) || (e.Type == "pack_engage" && ((JsString)e.Get("packId")!).Value == g.Id));
            if (hit is not null)
            {
                return hit.Tick;
            }
        }
        return null;
    }

    // Regression (PR #636 pins R-PINS-1 and LOOSE-PICKUP): a wandering Cantor stood 450 mm farther from the Chanter than it
    // does at rest, outside Silencing Rebuke's reach, so the ledger-named kicker could not kick the Mending; and its stroll
    // flipped which loose Choir enemy was nearest to the Warden, so the Taunt went to the one hitting nobody while the one
    // beating the Cinder was taunted too late. A caster that casts an interruptible ability holds its post: it never leaves
    // its rest position while idle, and its pack mates still walk.
    [Theory(DisplayName = "Idle wander: a caster with an interruptible cast holds its post at rest while the rest of its pack still walks")]
    [MemberData(nameof(Seeds))]
    public void A_kickable_caster_holds_its_post(long seed)
    {
        var (walk, _) = Idle(seed);
        var holders = 0;
        foreach (var g in Wanderers())
        {
            var posts = g.ActorIds.Where(id => IdleWander.HoldsPost(Setup.Content, id)).ToList();
            holders += posts.Count;
            foreach (var id in posts)
            {
                Assert.True(walk[id].Distinct().Count() == 1, $"seed {seed} {g.Id} actor {id} casts an interruptible ability and must hold its post, but it stood on {walk[id].Distinct().Count()} different spots");
            }
            if (posts.Count > 0 && posts.Count < g.ActorIds.Count)
            {
                Assert.Contains(g.ActorIds.Except(posts), id => walk[id].Distinct().Count() > 1);
            }
        }
        Assert.True(holders >= 1, "the shipped Hollow Vault has a kickable caster in a wandering group: the check would be vacuous");
    }

    [Fact(DisplayName = "Idle wander: the same seed walks the same positions tick for tick, and the seed shapes the walk")]
    public void The_walk_is_a_pure_function_of_the_seed()
    {
        var a = Idle(1).Walk;
        var b = Idle(1).Walk;
        foreach (var id in a.Keys)
        {
            Assert.Equal(a[id], b[id]);
        }
        var other = Idle(2).Walk;
        Assert.Contains(a.Keys, id => !a[id].SequenceEqual(other[id]));
    }
}
