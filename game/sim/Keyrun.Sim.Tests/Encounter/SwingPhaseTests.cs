using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// SWING-PHASE (ruling R-307-C item 3; BIBLE section 3 "cast bars are the enemy's language", section 0 test 3 "no run
/// ruined by one mistake"). An auto-attack (a script step with no telegraph whose ability is an instant, target-shaped
/// damage hit with no cast bar) is a swing timer, not a cast: in WoW each enemy's swing timer starts when that enemy
/// engages and the pack's swings fall apart by arrival and swing speed, so a pack's auto-attacks never land as one
/// unannounced burst. Before this rule every member of an engaged group ran its script on the group's one engage
/// clock, so the five Pair melee swung pair.meleeSwing (70 damage, no bar) on the same tick, and on PR #307 four of
/// them killed a 250-health seat between two consecutive ticks (run C, tick 2108). Each member of an engaged group now
/// swings on its own phase of the swing period, so no two of a group's auto-attacks land on the same tick.
/// Driven through RunRoot.CreateRun / StepRun on the shipped k2 dungeon placed into the shipped layout.
/// </summary>
public sealed class SwingPhaseTests
{
    private const long Seed = 1;
    private const string PairSwing = "pair.meleeSwing";
    private const int WatchTicks = 700;

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink (the swing ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public SwingPhaseTests(ITestOutputHelper output) => _out = output;

    private sealed record Played(List<SimEvent> Events, List<int> PairIds, int EngageTick, Run Final);

    // The Pair alone, placed in its room as the host places it; seat 1 stands on the Pair's ring centre so the whole
    // pull engages on it at once and every member locks it (the body pull), the other two seats stand far outside.
    // With puller 2 the careless Cinder (seat 3, 250 health, not the tank) is the one on the ring instead.
    private static Played StandInThePair(int puller = 0)
    {
        var (nav, rooms) = SeedEngageTests.Layout();
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var pull = setup.Dungeon.Pulls.Single(p => p.Id == "pair");
        var origin = rooms.Single(r => r.Pull == "pair").Origin!.Value;
        var placed = pull with
        {
            Spawns = pull.Spawns.Select(s => s with { Pos = new Vec3(origin.X + s.Pos.X, s.Pos.Y, origin.Z + s.Pos.Z) }).ToList(),
        };
        var center = Pulls.RingCenter(placed);
        var far = new Vec3(0, 0, 0);
        var party = setup.Party.Take(3).Select((m, i) => m with
        {
            Pos = i == puller ? new Vec3(center.X - 1500, 0, center.Z) : new Vec3(far.X, 0, far.Z + (i * 1200)),
            Yaw = 0,
        }).ToList();
        var dungeon = setup.Dungeon with { Pulls = new[] { placed } };
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);
        var events = new List<SimEvent>();
        int? engage = null;
        for (var i = 0; i < WatchTicks; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            if (engage is null && stepped.Events.FirstOrDefault(e => e.Type == "pull_start") is { } ps)
            {
                engage = ps.Tick;
            }
        }
        Assert.True(engage is not null, "the Pair never engaged with seat 1 on its ring");
        return new Played(events, placed.Spawns.Select(s => s.ActorId).ToList(), engage!.Value, run);
    }

    private static long Int(SimEvent e, string key) => ((JsInt)e.Get(key)!).Value;

    private static List<SimEvent> PairSwings(Played p) => p.Events
        .Where(e => e.Type == "cast_complete" && ((JsString)e.Get("abilityId")!).Value == PairSwing && p.PairIds.Contains((int)Int(e, "actorId")))
        .ToList();

    [Fact(DisplayName = "Swing phase: every Pair melee auto-attacks after engage, and no two of the pull's swings land on the same tick")]
    public void A_pulls_auto_attacks_never_share_a_tick()
    {
        var played = StandInThePair();
        var swings = PairSwings(played);
        _out.WriteLine($"pair engage={played.EngageTick} swings: " + string.Join(" ", swings.Select(e => $"{Int(e, "actorId")}@{e.Tick}")));

        // Every member swings (the phase delays a swing timer, it never drops one).
        Assert.All(played.PairIds, id => Assert.Contains(swings, e => Int(e, "actorId") == id));
        var shared = swings.GroupBy(e => e.Tick).Where(g => g.Count() > 1).Select(g => $"{g.Key}x{g.Count()}").ToList();
        Assert.True(shared.Count == 0, $"Pair swings shared a tick: {string.Join(", ", shared)}");
    }

    [Fact(DisplayName = "Swing phase: member k of the Pair starts its swing timer k/n of the swing period after member 0, who keeps the content schedule")]
    public void Each_member_keeps_its_swing_period()
    {
        var played = StandInThePair();
        var swings = PairSwings(played);
        // The period, from the pull's first member (member 0 keeps the content schedule): its first two swings.
        var lead = swings.Where(e => Int(e, "actorId") == played.PairIds.Min()).Select(e => e.Tick).ToList();
        Assert.True(lead.Count >= 2, $"enemy {played.PairIds.Min()} swung {lead.Count} time(s) in {WatchTicks} ticks");
        var period = lead[1] - lead[0];
        var members = played.PairIds.OrderBy(id => id).ToList();
        for (var k = 0; k < members.Count; k++)
        {
            // Member k's swing timer starts k/n of the period after member 0's, and it keeps swinging after that.
            var ticks = swings.Where(e => Int(e, "actorId") == members[k]).Select(e => e.Tick).ToList();
            Assert.True(ticks.Count >= 2, $"enemy {members[k]} swung {ticks.Count} time(s) in {WatchTicks} ticks");
            Assert.Equal(lead[0] + (k * period / members.Count), ticks[0]);
        }
        // The pull's first swing is not later than it was before the phase: the phase only spreads the rest.
        var first = swings.Min(e => e.Tick);
        Assert.True(first <= played.EngageTick + 60 + 1, $"the Pair's first swing came at {first}, engage {played.EngageTick}");
    }

    [Fact(DisplayName = "Swing phase: only the auto-attack moves; the Pair's telegraphed casts and every cast-bar ability keep their schedule")]
    public void Only_auto_attacks_are_phased()
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var abilities = setup.Content.Abilities;
        var pairId = setup.Dungeon.Pulls.Single(p => p.Id == "pair").Spawns.Max(s => s.ActorId);
        var script = setup.Content.EnemyScripts[pairId];
        var swing = Assert.Single(script, s => s.Cast == PairSwing);
        Assert.True(Scripts.IsAutoAttack(swing, abilities[PairSwing]));
        // The same instant hit announced by a floor telegraph is a cast the party reads, not a swing timer.
        var announced = swing with { Telegraph = new TelegraphSpec("circle", 40) { Radius = 1500 } };
        Assert.False(Scripts.IsAutoAttack(announced, abilities[PairSwing]));
        Assert.Same(announced, Scripts.SwingPhased(announced, abilities[PairSwing], 3, 5));
        foreach (var step in script.Where(s => s.Cast != PairSwing))
        {
            // pair.spreadMark and pair.stackCall carry a floor telegraph: they are announced casts, never phased.
            Assert.False(Scripts.IsAutoAttack(step, abilities.GetValueOrDefault(step.Cast)), $"{step.Cast} was taken for an auto-attack");
            Assert.Same(step, Scripts.SwingPhased(step, abilities.GetValueOrDefault(step.Cast), 3, 5));
        }
        // Every shipped script step whose ability has a cast bar keeps its schedule, whatever the member.
        foreach (var step in setup.Content.EnemyScripts.Values.SelectMany(s => s).Where(s => abilities.TryGetValue(s.Cast, out var a) && a.CastTicks > 0))
        {
            Assert.Same(step, Scripts.SwingPhased(step, abilities[step.Cast], 1, 2));
        }
        // The swing itself: member 0 and a group of one keep the schedule; member 3 of 5 moves 3/5 of the period.
        Assert.Same(swing, Scripts.SwingPhased(swing, abilities[PairSwing], 0, 5));
        Assert.Same(swing, Scripts.SwingPhased(swing, abilities[PairSwing], 0, 1));
        var phased = Scripts.SwingPhased(swing, abilities[PairSwing], 3, 5);
        Assert.Equal(swing.Every, phased.Every);
        Assert.Equal(swing.AtTick + (3 * swing.Every!.Value / 5), phased.AtTick);
    }

    [Fact(DisplayName = "Swing phase: no seat goes from full health to dead between two consecutive ticks on auto-attacks alone")]
    public void Auto_attacks_alone_never_kill_a_full_seat_in_one_tick()
    {
        var played = StandInThePair();
        var swingDamage = played.Events
            .Where(e => e.Type == "damage" && played.PairIds.Contains((int)Int(e, "sourceId")) && Int(e, "healthDamage") > 0)
            .GroupBy(e => (e.Tick, Target: Int(e, "targetId")))
            .Select(g => (g.Key.Tick, g.Key.Target, Damage: g.Sum(e => Int(e, "healthDamage"))))
            .ToList();
        Assert.NotEmpty(swingDamage);
        var worst = swingDamage.Max(d => d.Damage);
        _out.WriteLine($"worst one-tick Pair damage to one seat: {worst}");
        // 250 is the careless Cinder's seat health in run C (the seat that died at 2108 on #307).
        Assert.True(worst < 250, $"the Pair dealt {worst} to one seat on one tick");
    }

    [Fact(DisplayName = "Swing phase: a careless Cinder body-pulls the Pair; every member swings at it on its own tick and it is alive after the first two swing ticks (f-2e1fc45a)")]
    public void A_careless_body_pull_on_a_non_tank_seat_is_not_deleted_between_two_ticks()
    {
        const int Cinder = 3;
        var played = StandInThePair(puller: 2);
        // Not the Warden: the seat the finding names is a 250-health damage seat.
        Assert.Equal("cinder", played.Final.State.Actors[Cinder].ClassId);
        var swingHits = played.Events
            .Where(e => e.Type == "damage" && played.PairIds.Contains((int)Int(e, "sourceId")) && Int(e, "targetId") == Cinder)
            .ToList();
        _out.WriteLine("pair hits on seat 3: " + string.Join(" ", swingHits.Select(e => $"{Int(e, "sourceId")}@{e.Tick}:{Int(e, "healthDamage")}")));

        // The body pull: at least four Pair members locked the Cinder and hit it (the finding's four swings; a fifth
        // member's first swing may come after the seat is already dead).
        var hitters = swingHits.Select(e => (int)Int(e, "sourceId")).Distinct().ToList();
        Assert.True(hitters.Count >= 4, $"only {hitters.Count} Pair member(s) hit seat 3");
        // Each enemy's swing timer runs on its own: no two of the members' first hits share a tick.
        var firstHits = hitters.Select(id => swingHits.First(e => Int(e, "sourceId") == id).Tick).ToList();
        Assert.True(firstHits.Count == firstHits.Distinct().Count(), "first Pair hits shared a tick: " + string.Join(" ", firstHits));
        // The seat survives the first two hit ticks: it is not taken from full to dead between two ticks.
        var hitTicks = swingHits.Select(e => e.Tick).Distinct().OrderBy(t => t).ToList();
        Assert.True(hitTicks.Count >= 2, $"the Pair hit the Cinder on {hitTicks.Count} tick(s)");
        var death = played.Events.FirstOrDefault(e => e.Type == "death" && Int(e, "actorId") == Cinder);
        Assert.True(death is null || death.Tick > hitTicks[1], $"seat 3 died on tick {death?.Tick}, the Pair's second hit tick is {hitTicks[1]}");
    }
}
