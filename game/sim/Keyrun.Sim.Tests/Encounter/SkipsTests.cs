using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// R-SYNERGY-1 / R-SYNERGY-2, lane L4 (design/DUNGEON-SYNERGY.md section 2.6): the skip record. A pack is skipped on the
/// first tick every living seated player stands in a room past the pack's post while the pack is alive and was never
/// engaged; a pack that engaged and then leashed (or otherwise dropped out of the engaged set) is recorded as leashed. A
/// skip never touches the penalty clock or the forces bar: skipped enemies simply stay un-banked.
/// </summary>
public sealed class SkipsTests
{
    private const int PartyA = 1;
    private const int PartyB = 2;
    private const int PackAEnemy = 10;
    private const int PackBEnemy = 20;

    // Three rooms in route order (box k overlaps box k+1 at the doorway).
    private static NavLayout Layout() => new(
        new List<NavBox>
        {
            new("r0", new Bounds(0, 10000, 0, 4000), true, null),
            new("r1", new Bounds(10000, 20000, 0, 4000), true, null),
            new("r2", new Bounds(20000, 30000, 0, 4000), true, null),
        },
        new List<NavTrigger>(),
        new List<Vec3> { new(10000, 0, 2000), new(20000, 0, 2000), new(30000, 0, 2000) });

    private static IReadOnlyList<AggroGroup> Groups() => new List<AggroGroup>
    {
        new("pack-a", -1, new[] { PackAEnemy }, new Vec3(5000, 0, 2000), 2000, Array.Empty<string>()),
        new("pack-b", -1, new[] { PackBEnemy }, new Vec3(15000, 0, 2000), 2000, Array.Empty<string>()),
    };

    private static ForcesGoal Goal() => new(new Dictionary<string, int> { ["grunt"] = 6, ["brute"] = 9 }, 100);

    private static SimState State(long partyAX, long partyBX)
    {
        var s = CombatTestKit.State(
            CombatTestKit.Actor(PartyA, ActorKind.Player, x: partyAX, z: 2000),
            CombatTestKit.Actor(PartyB, ActorKind.Player, x: partyBX, z: 2000),
            CombatTestKit.Actor(PackAEnemy, ActorKind.Enemy, x: 5000, z: 2000, classId: "grunt"),
            CombatTestKit.Actor(PackBEnemy, ActorKind.Enemy, x: 15000, z: 2000, classId: "brute"));
        return s;
    }

    private static EncounterState Encounter(params string[] engaged) =>
        EncounterState.Create(2, 100000, Clauses.DefaultEncounterRules(2)) with { Engaged = engaged };

    [Fact(DisplayName = "Skips (a): a pack never engaged by the time every seat is past its room is recorded skipped with id, tick and party position")]
    public void Never_engaged_pack_is_skipped_when_the_party_is_past()
    {
        var events = new List<SimEvent>();
        var enc = Encounter();
        var state = State(25000, 27000);
        state.Tick = 7;
        enc = Skips.Step(state, enc, Groups(), Layout(), Goal(), null, events.Add);

        var skipped = enc.Skips.Entries.Where(e => e.Outcome == SkipOutcome.Skipped).ToList();
        Assert.Equal(new[] { "pack-a", "pack-b" }, skipped.Select(e => e.PackId).ToArray());
        Assert.All(skipped, e =>
        {
            Assert.Equal(7, e.Tick);
            Assert.Equal(new Vec3(26000, 0, 2000), e.PartyPos);
        });
        Assert.Equal(2, events.Count(e => e.Type == "pack_skipped"));
    }

    [Fact(DisplayName = "Skips (a): a seat still behind the pack's room, or a dead pack, is not a skip; and a pack is recorded once")]
    public void Nothing_is_skipped_until_every_seat_is_past_and_each_pack_records_once()
    {
        var events = new List<SimEvent>();
        var enc = Encounter();
        var state = State(25000, 5000);
        enc = Skips.Step(state, enc, Groups(), Layout(), Goal(), null, events.Add);
        Assert.Empty(enc.Skips.Entries);

        // Pack A dies (killed, not skipped); the party then passes both.
        state.Actors[PackAEnemy].Alive = false;
        state.Actors[PartyB].Pos = new Vec3(26000, 0, 2000);
        state.Tick = 3;
        enc = Skips.Step(state, enc, Groups(), Layout(), Goal(), null, events.Add);
        Assert.Equal(new[] { "pack-b" }, enc.Skips.Entries.Select(e => e.PackId).ToArray());
        state.Tick = 4;
        enc = Skips.Step(state, enc, Groups(), Layout(), Goal(), null, events.Add);
        Assert.Single(enc.Skips.Entries);
        Assert.Single(events, e => e.Type == "pack_skipped");
    }

    [Fact(DisplayName = "Skips (a): an abandoned seat standing behind does not hold the boundary open")]
    public void Open_seats_are_ignored()
    {
        var enc = Skips.Step(State(25000, 1000), Encounter(), Groups(), Layout(), Goal(), new[] { PartyB }, _ => { });
        Assert.Equal(2, enc.Skips.Entries.Count);
    }

    [Fact(DisplayName = "Skips (b): a pack that engaged and then left the engaged set (a leash) is recorded leashed, never skipped")]
    public void Engaged_then_leashed_pack_is_leashed_not_skipped()
    {
        var events = new List<SimEvent>();
        var state = State(5000, 5000);
        var enc = Skips.Step(state, Encounter("pack-b"), Groups(), Layout(), Goal(), null, events.Add);
        Assert.Empty(enc.Skips.Entries);
        Assert.Contains("pack-b", enc.Skips.EverEngaged);

        // The leash walks pack-b out of the engaged set; the party then crosses the boundary.
        enc = enc with { Engaged = Array.Empty<string>() };
        state.Actors[PartyA].Pos = new Vec3(25000, 0, 2000);
        state.Actors[PartyB].Pos = new Vec3(25000, 0, 2000);
        state.Tick = 9;
        enc = Skips.Step(state, enc, Groups(), Layout(), Goal(), null, events.Add);

        Assert.Equal(SkipOutcome.Leashed, enc.Skips.Entries.Single(e => e.PackId == "pack-b").Outcome);
        Assert.Equal(SkipOutcome.Skipped, enc.Skips.Entries.Single(e => e.PackId == "pack-a").Outcome);
        Assert.DoesNotContain(events, e => e.Type == "pack_skipped" && e.Get("packId") is JsString { Value: "pack-b" });
    }

    [Fact(DisplayName = "Skips (c): skipped packs bank no forces, owe the bar what is left, and never move the penalty clock")]
    public void Skips_do_not_bank_forces_and_cost_no_penalty()
    {
        var events = new List<SimEvent>();
        var state = State(25000, 25000);
        var before = Encounter();
        var enc = Skips.Step(state, before, Groups(), Layout(), Goal(), null, events.Add);

        Assert.Equal(0, Forces.Points(state, Goal().PointsByEnemy));
        Assert.Equal(before.PenaltyTicks, enc.PenaltyTicks);
        Assert.Equal(before.Deaths, enc.Deaths);
        var a = events.Single(e => e.Type == "pack_skipped" && e.Get("packId") is JsString { Value: "pack-a" });
        Assert.Equal(100, ((JsInt)a.Get("forcesLeft")!).Value);
        Assert.Equal(6, enc.Skips.Entries.Single(e => e.PackId == "pack-a").ForcesForgone);
        Assert.Equal(9, enc.Skips.Entries.Single(e => e.PackId == "pack-b").ForcesForgone);

        // Killing a skipped pack later banks exactly its value: skipped is a log fact, not a state.
        state.Actors[PackAEnemy].Alive = false;
        Assert.Equal(6, Forces.Points(state, Goal().PointsByEnemy));
    }

    [Fact(DisplayName = "Skips (d): two runs of the same scripted walk produce an identical record")]
    public void The_record_is_deterministic()
    {
        List<string> Walk()
        {
            var enc = Encounter();
            var state = State(1000, 1000);
            var log = new List<string>();
            for (var step = 0; step < 6; step++)
            {
                state.Tick = step;
                state.Actors[PartyA].Pos = new Vec3(1000 + (step * 5000), 0, 2000);
                state.Actors[PartyB].Pos = new Vec3(1000 + (step * 5000), 0, 2000);
                enc = Skips.Step(state, enc, Groups(), Layout(), Goal(), null, e => log.Add(e.Tick + ":" + e.Type));
            }
            log.AddRange(enc.Skips.Entries.Select(e => $"{e.PackId}@{e.Tick}/{e.PartyPos.X},{e.PartyPos.Z}/{e.Outcome}/{e.ForcesForgone}"));
            return log;
        }

        var first = Walk();
        Assert.Equal(first, Walk());
        Assert.Contains(first, l => l.StartsWith("pack-a@", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Skips (e): the run log gets one skip line per skipped pack, in the run log's KEY=value style; a leash is silent")]
    public void The_log_line_names_pack_tick_and_position()
    {
        var events = new List<SimEvent>();
        var enc = Skips.Step(State(25000, 27000), Encounter(), Groups(), Layout(), Goal(), null, events.Add);
        var lines = events.Select(Skips.LogLine).Where(l => l is not null).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal("SKIP pack=pack-a tick=0 at=26000,2000 forcesLeft=100", lines[0]);
        Assert.Equal("SKIP pack=pack-b tick=0 at=26000,2000 forcesLeft=100", lines[1]);
        Assert.Null(Skips.LogLine(new SimEvent(0, "pack_engage", ("packId", JsValue.Str("x")))));
        Assert.Equal(2, enc.Skips.Entries.Count);
    }
}
