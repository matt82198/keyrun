using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Pack aggro, chase and chaining as properties of the shipped Hollow Vault at key 2, seeds 1..5 (finding f-f0c886eb;
/// Matt 2026-10-22: "the first couple packs just stand still ... you can just go one at a time"). The sim rules landed in
/// #573 (EnemyAggro, Aggro.Triggered, NavFollow.RouteChase, Leash); this suite pins the WoW Mythic+ behaviour on the real
/// content as properties of the seed, never as a trajectory pin (R-PINS-1, f-b5b9d0e9): the boundary of each rule is
/// found by placing the party at a computed distance from the real placement, never by a recorded tick.
/// (a) social aggro: one seat inside one mob's own radius engages the whole pack, on that tick, in one pull_start.
/// (b) pathed chase: a mob whose straight line to the party leaves the walkable space (room corner to corridor) still
///     reaches melee range, and never stands outside the layout. The shipped layout has no obstacle boxes, so the wall is
///     the room edge; the pillar case is AggroChaseTests.Chase_paths_around_the_pillar_and_stops_at_melee_range.
/// (c) chaining: a chaser within the assist radius of an idle pack brings in the whole pack (body pull); a party kiting
///     the Choir east engages the Pair while the Choir is still engaged (the pulls add up, nothing prevents it).
/// </summary>
public sealed class PackAggroChasePropertyTests
{
    public static IEnumerable<object[]> Seeds => Enumerable.Range(1, 5).Select(s => new object[] { (long)s });

    private const long MeleeMm = 2000;

    private readonly ITestOutputHelper _out;

    public PackAggroChasePropertyTests(ITestOutputHelper output) => _out = output;

    private static readonly HostRunSetup Setup = HostRunSetup.Build(ContentLoader.Load(), "hollow-vault", 2, "warden", withBots: true);

    // One idle tick first: the run settles stacked spawns apart on its first step, so every position a test reads or
    // measures a radius from is the settled one (the party stands at the entrance, far outside every radius).
    private static Run Create(long seed) => RunRoot.StepRun(RunRoot.CreateRun(seed, Setup.Party, Setup.Dungeon, Setup.Content, null), Array.Empty<Command>()).Run;

    private static IReadOnlyList<int> Seats => Setup.Party.Select(p => p.Id).ToList();

    private static void PlaceParty(Run run, long x, long z)
    {
        // Seat 0 at the point, the others in line 3000 mm behind each other (west): stacked seats are pushed apart on the
        // next step, which would move the seat a test measures a radius to.
        var seats = Seats;
        for (var i = 0; i < seats.Count; i++)
        {
            run.State.Actors[seats[i]].Pos = new Vec3(x - (3000 * i), 0, z);
        }
    }

    private static (Run Run, List<SimEvent> Events) Step(Run run, int ticks, Func<Run, IReadOnlyList<Command>>? commands = null, Action<Run>? each = null)
    {
        var events = new List<SimEvent>();
        for (var i = 0; i < ticks; i++)
        {
            var stepped = RunRoot.StepRun(run, commands?.Invoke(run) ?? Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            each?.Invoke(run);
        }
        return (run, events);
    }

    private static List<SimEvent> Starts(IEnumerable<SimEvent> events, string pullId) =>
        events.Where(e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == pullId).ToList();

    private static IReadOnlyList<int> Members(string pullId) => Setup.Dungeon.Pulls.Single(p => p.Id == pullId).Spawns.Select(s => s.ActorId).ToList();

    private static long Dist(Vec3 a, Vec3 b) => Keyrun.Sim.Combat.Movement.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    private sealed record Mob(Vec3 Pos, long? AggroMm);

    // The mob of the pull nearest to the origin, at the position the created run places it (a spawn's formation offset is
    // applied at creation, so the actor, not the spawn record, is where the radius is measured from).
    private static Mob Nearest(string pullId, Vec3 from)
    {
        var probe = Create(1);
        var spawn = Setup.Dungeon.Pulls.Single(p => p.Id == pullId).Spawns.OrderBy(s => Dist(probe.State.Actors[s.ActorId].Pos, from)).First();
        return new Mob(probe.State.Actors[spawn.ActorId].Pos, spawn.AggroMm);
    }

    [Theory(DisplayName = "Pack aggro (a): a seat exactly at one mob's own radius engages the whole pack on that tick, 1 mm farther engages nothing")]
    [MemberData(nameof(Seeds))]
    public void One_mob_seeing_a_seat_engages_the_whole_pack_the_same_tick(long seed)
    {
        foreach (var pullId in new[] { "choir", "pair" })
        {
            var mob = Nearest(pullId, new Vec3(0, 0, 0));
            var radius = mob.AggroMm ?? throw new InvalidOperationException(pullId + " mob carries no aggro radius");
            var members = Members(pullId);

            var outside = Create(seed);
            PlaceParty(outside, mob.Pos.X - radius - 1, mob.Pos.Z);
            var (idle, idleEvents) = Step(outside, 1);
            Assert.True(Starts(idleEvents, pullId).Count == 0, $"{pullId} woke at radius+1 party={outside.State.Actors[Seats[0]].Pos} mob={mob.Pos} r={radius} m={string.Join(" ", members.Select(id => id + ":" + outside.State.Actors[id].AggroMm + ":" + Dist(outside.State.Actors[id].Pos, outside.State.Actors[Seats[0]].Pos)))} ev={string.Join(";", idleEvents.Select(e => e.Type))}");
            Assert.All(members, id => Assert.True(idle.State.Actors[id].Idle, $"{pullId} {id} woke 1 mm outside its radius"));

            var inside = Create(seed);
            PlaceParty(inside, mob.Pos.X - radius, mob.Pos.Z);
            var (engaged, events) = Step(inside, 1);
            var start = Assert.Single(Starts(events, pullId));
            var named = ((JsArray)start.Get("actorIds")!).Items.Select(v => (int)((JsInt)v).Value).OrderBy(i => i).ToList();
            _out.WriteLine($"seed {seed} {pullId}: engaged on tick {start.Tick} naming {string.Join(",", named)} at radius {radius}");
            Assert.Equal(members.OrderBy(i => i).ToList(), named);
            Assert.All(members, id => Assert.False(engaged.State.Actors[id].Idle, $"{pullId} {id} stayed idle in an engaged pack"));
            Assert.Contains(pullId, engaged.Encounter.Engaged);
        }
    }

    // The Choir room with a wall across it: x 15000..16000, z -7000..3000, leaving a 5000 mm gap to the north and a 1000 mm
    // gap to the south. The shipped layout has no obstacle boxes (sliding along room edges is enough to cross its
    // corridors), so the property that needs a path adds the wall to the real layout.
    private static readonly Keyrun.Sim.Combat.Bounds Wall = new(15000, 16000, -7000, 3000);

    [Theory(DisplayName = "Pack chase (b): a Choir mob aggroed behind a wall reaches melee range of the party by walking around it, never inside it")]
    [MemberData(nameof(Seeds))]
    public void An_aggroed_mob_behind_a_wall_walks_around_it_to_melee(long seed)
    {
        var layout = Setup.Content.Layout! with { Obstacles = new[] { Wall } };
        var created = RunRoot.CreateRun(seed, Setup.Party, Setup.Dungeon, Setup.Content with { Layout = layout }, null);
        var run = RunRoot.StepRun(created, Array.Empty<Command>()).Run;
        var choir = Members("choir");
        var chaser = choir[0];
        // The chaser east of the wall, level with the seat, so the straight line runs through the wall; the rest wait far
        // east so the chaser's way is not a crowd's.
        run.State.Actors[chaser].Pos = new Vec3(18000, 0, 0);
        run.State.Actors[choir[1]].Pos = new Vec3(24000, 0, 5000);
        run.State.Actors[choir[2]].Pos = new Vec3(24000, 0, -5000);
        PlaceParty(run, 12000, 0);
        var grid = Keyrun.Sim.Nav.NavGrid.Of(layout);
        var detour = false;
        var inside = 0;
        var (final, events) = Step(run, 600, null, r =>
        {
            var actor = r.State.Actors[chaser];
            if (!actor.Alive)
            {
                return;
            }
            inside += Keyrun.Sim.Nav.NavGrid.Blocked(layout.Obstacles, actor.Pos) ? 1 : 0;
            detour |= actor.Pos.Z > 3000 || actor.Pos.Z < -7000;
        });
        // Melee range is the chase's own: it closes to its engage range of its target and swings, so the observable is the
        // chaser's first damage event (the swing landing), not a centre-to-centre distance.
        int? reached = events.FirstOrDefault(e => e.Type == "damage" && ((JsInt)e.Get("sourceId")!).Value == chaser)?.Tick;
        _out.WriteLine($"seed {seed}: chaser {chaser} first swing tick {reached}, detoured {detour}, ticks inside the wall {inside}, walkable at end {grid.Walkable(final.State.Actors[chaser].Pos)}");
        Assert.NotEmpty(Starts(events, "choir"));
        Assert.Equal(0, inside);
        Assert.True(detour, "the chaser never left the wall's z band: it did not walk around");
        Assert.NotNull(reached);
    }

    [Theory(DisplayName = "Pack chain (c): a chaser within the assist radius of an idle pack engages it whole on that tick; beyond the radius it stays idle")]
    [MemberData(nameof(Seeds))]
    public void A_chaser_in_assist_range_body_pulls_the_pack(long seed)
    {
        var assist = Members("pair").Select(id => Setup.Dungeon.Pulls.Single(p => p.Id == "pair").Spawns.Single(s => s.ActorId == id).AssistMm).Distinct().Single()
                     ?? throw new InvalidOperationException("pair carries no assist radius");

        Run Engaged(out int chaser)
        {
            // The Choir engages legitimately (a seat inside its mob's radius); then one Choir member is the chaser, dragged to
            // the Pair while the seats stay far outside every Pair radius, so only the assist rule can reach the Pair.
            var run = Create(seed);
            var mob = Nearest("choir", new Vec3(0, 0, 0));
            PlaceParty(run, mob.Pos.X - 7000, mob.Pos.Z);
            var (after, events) = Step(run, 1);
            Assert.Single(Starts(events, "choir"));
            chaser = Members("choir")[0];
            return after;
        }

        var edge = Nearest("pair", new Vec3(0, 0, 0));

        var near = Engaged(out var c1);
        near.State.Actors[c1].Pos = new Vec3(edge.Pos.X - (assist - 500), 0, edge.Pos.Z);
        var (joined, joinedEvents) = Step(near, 1);
        var start = Assert.Single(Starts(joinedEvents, "pair"));
        _out.WriteLine($"seed {seed}: pair body-pulled on tick {start.Tick} by chaser {c1} at {assist - 500} mm (assist {assist})");
        Assert.Equal(Members("pair").OrderBy(i => i).ToList(), ((JsArray)start.Get("actorIds")!).Items.Select(v => (int)((JsInt)v).Value).OrderBy(i => i).ToList());
        Assert.All(Members("pair"), id => Assert.False(joined.State.Actors[id].Idle));

        var far = Engaged(out var c2);
        far.State.Actors[c2].Pos = new Vec3(edge.Pos.X - (assist + 500), 0, edge.Pos.Z);
        var (still, stillEvents) = Step(far, 1);
        Assert.True(Starts(stillEvents, "pair").Count == 0, $"pair woke; chaser {far.State.Actors[c2].Pos} edge {edge.Pos} ev={string.Join(";", stillEvents.Select(e => e.Type))}");
        Assert.All(Members("pair"), id => Assert.True(still.State.Actors[id].Idle));
    }

    [Theory(DisplayName = "Pack chain (c): a party kiting the Choir east engages the Pair while the Choir is still engaged (big pulls are possible)")]
    [MemberData(nameof(Seeds))]
    public void Kiting_the_choir_into_the_pair_stacks_both_pulls(long seed)
    {
        var run = Create(seed);
        var mob = Nearest("choir", new Vec3(0, 0, 0));
        PlaceParty(run, mob.Pos.X - 7000, mob.Pos.Z);
        var choir = Members("choir");
        IReadOnlyList<Command> Kite(Run r) => Seats.Where(id => r.State.Actors[id].Alive)
            .Select(id => (Command)new MoveCommand(r.State.Tick, id, new Dir2(1000, 0), 0)).ToList();
        int? choirTick = null;
        int? pairTick = null;
        var choirAliveAtPair = 0;
        var evaded = false;
        var (final, events) = Step(run, 2500, Kite, r =>
        {
            if (choirTick is null && r.Encounter.Engaged.Contains("choir"))
            {
                choirTick = r.State.Tick;
            }
            if (pairTick is null && r.Encounter.Engaged.Contains("pair"))
            {
                pairTick = r.State.Tick;
                choirAliveAtPair = choir.Count(id => r.State.Actors[id].Alive && !r.State.Actors[id].Idle);
            }
            evaded |= choir.Any(id => r.State.Actors[id].Evading);
        });
        _out.WriteLine($"seed {seed}: choir engaged {choirTick}, pair engaged {pairTick} with {choirAliveAtPair} choir mobs still fighting, choir evaded {evaded}");
        Assert.NotNull(choirTick);
        Assert.NotNull(pairTick);
        Assert.True(pairTick > choirTick);
        Assert.True(choirAliveAtPair > 0, "the Choir was gone or reset before the Pair engaged: nothing chained");
        Assert.Contains(events, e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "pair");
        _ = final;
    }

    [Theory(DisplayName = "Pack aggro autorun (k2): the scripted bots clear the key within par on seeds 1..5 with real aggro, chase and chaining")]
    [MemberData(nameof(Seeds))]
    public void The_bots_clear_key_two_within_par(long seed)
    {
        var result = PlaytestRunner.Play(new PlaytestOptions(2, seed, null), ContentLoader.Load());
        var engages = result.Log.Split('\n').Where(l => l.StartsWith("E ", StringComparison.Ordinal) && (l.Contains("\"pull_start\"") || l.Contains("\"pack_engage\"")))
            .Select(l => l.Length > 120 ? l[..120] : l);
        _out.WriteLine(string.Join("\n", engages));
        _out.WriteLine($"seed {seed}: {result.Run.RunEnd.Result?.ToJs()} at tick {result.Run.RunEnd.ResultTick} (par {result.Setup.Content.ParTicks})");
        Assert.Equal("timed", result.Run.RunEnd.Result?.ToJs());
        Assert.True(result.Run.RunEnd.ResultTick <= result.Setup.Content.ParTicks, "the clear ran past par");
    }
}
