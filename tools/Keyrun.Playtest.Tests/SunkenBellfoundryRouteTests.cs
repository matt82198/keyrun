using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Playtest;
using Keyrun.Sim.Dungeon;
using Keyrun.Sim.Encounter;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The Sunken Bellfoundry as a three-segment route (D65/D66/D67): the segment fill gives every segment filled trash rooms on
/// both branches and exactly one boss, and the scripted autorun at key 2 seed 1 reaches all three bosses in order and ends
/// timed inside the content's par (24000 ticks = 20 min), engaging the final boss with the forces bar full.
/// </summary>
public sealed class SunkenBellfoundryRouteTests
{
    private const string Dungeon = "sunken-bellfoundry";

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink (the measured timeline is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public SunkenBellfoundryRouteTests(ITestOutputHelper output) => _out = output;

    [Fact(DisplayName = "Sunken Bellfoundry fill (seeds 1-5): every segment has a filled trash room on each branch and exactly one boss")]
    public void Every_segment_is_filled_on_both_branches_with_one_boss()
    {
        var reg = ContentLoader.Load();
        var segments = reg.Dungeons[Dungeon].Segments.Count;
        for (long seed = 1; seed <= 5; seed++)
        {
            var d = SeededLayout.BuildSegmented(reg, Dungeon, seed);
            Assert.Equal(segments, d.Graph.SegmentCount);
            for (var s = 0; s < segments; s++)
            {
                for (var b = 0; b < RoomGraphGen.BranchCount; b++)
                {
                    var filled = d.Fill.Packs.Count(p => p.Segment == s && p.Branch == b);
                    Assert.True(filled >= 1, $"seed {seed}: segment {s} branch {b} has {filled} filled trash rooms");
                }
                var boss = Assert.Single(d.Fill.Bosses, x => x.Segment == s);
                Assert.Equal(d.Graph.BossRooms[s], boss.RoomId);
                Assert.Equal(reg.Dungeons[Dungeon].Segments[s].Boss.EnemyId, boss.BossId);
            }
            Assert.Equal(segments, d.Fill.Bosses.Count);
        }
    }

    [Fact(DisplayName = "Sunken Bellfoundry route (key 2, seed 1): the autorun kills all three bosses in order, ends timed within par, and engages boss 3 with forces >= 10000 bp")]
    public void Autorun_clears_three_bosses_timed_within_par()
    {
        var reg = ContentLoader.Load();
        var par = reg.Dungeons[Dungeon].ParTicks;
        var bosses = SeededRoster.SegmentBosses(reg, Dungeon);
        var table = reg.Forces.Values.ToDictionary(v => v.EnemyId, v => v.Points, StringComparer.Ordinal);
        var engaged = new Dictionary<string, (int Tick, int Bp)>(StringComparer.Ordinal);
        var options = new PlaytestOptions(2, 1, null)
        {
            DungeonId = Dungeon,
            Observe = (tick, run, _) =>
            {
                foreach (var a in run.State.Actors.Values.Where(a => a.Alive && !a.Idle && bosses.Contains(a.ClassId, StringComparer.Ordinal)))
                {
                    if (!engaged.ContainsKey(a.ClassId))
                    {
                        engaged[a.ClassId] = (tick, Forces.Read(run.State, table, reg.Forces.Required).PercentBp);
                    }
                }
            },
        };

        var result = PlaytestRunner.Play(options, reg);

        var events = result.Log.Split('\n')
            .Where(line => line.StartsWith("E ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line.AsSpan(2).ToString()).RootElement)
            .ToList();
        var deaths = events
            .Where(e => e.GetProperty("type").GetString() == "death")
            .Select(e => (Tick: e.GetProperty("tick").GetInt32(), Class: result.Run.State.Actors[e.GetProperty("actorId").GetInt32()].ClassId))
            .Where(d => bosses.Contains(d.Class, StringComparer.Ordinal))
            .ToList();
        var wipes = events.Count(e => e.GetProperty("type").GetString() == "wipe");
        _out.WriteLine($"par {par}; run_end {result.Run.RunEnd.Result} at {result.Run.RunEnd.ResultTick}; wipes {wipes}");
        foreach (var boss in bosses)
        {
            _out.WriteLine($"{boss}: engaged {(engaged.TryGetValue(boss, out var e) ? $"tick {e.Tick} forces {e.Bp} bp" : "never")}; died {(deaths.FirstOrDefault(d => d.Class == boss) is { Class: not null } d ? $"tick {d.Tick}" : "never")}");
        }

        Assert.Equal(bosses, deaths.Select(d => d.Class).ToList());
        Assert.True(deaths.Select(d => d.Tick).SequenceEqual(deaths.Select(d => d.Tick).OrderBy(t => t)), "the bosses died out of order");
        Assert.Equal(RunResult.Timed, result.Run.RunEnd.Result);
        Assert.True(engaged.ContainsKey(bosses[^1]), "boss 3 was never engaged");
        Assert.True(engaged[bosses[^1]].Bp >= 10000, $"boss 3 engaged at {engaged[bosses[^1]].Bp} bp");
        Assert.True(result.Run.RunEnd.ResultTick <= par, $"run ended at {result.Run.RunEnd.ResultTick}, par {par}");
    }
}
