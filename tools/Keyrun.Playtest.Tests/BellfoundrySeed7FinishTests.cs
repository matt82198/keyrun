using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Playtest;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Finding f-eb9ac9de: the scripted party at key 2 on the Sunken Bellfoundry, seed 7, reached 1090/1000 forces and never
/// engaged the final boss (the run depleted at tick 48000). A seeded dungeon must be finishable on every seed.
/// </summary>
public sealed class BellfoundrySeed7FinishTests
{
    private const string Dungeon = "sunken-bellfoundry";

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public BellfoundrySeed7FinishTests(ITestOutputHelper output) => _out = output;

    [Fact(DisplayName = "Sunken Bellfoundry (key 2, seed 7, scripted party): the final segment boss is dead at run end")]
    public void Final_boss_dies_on_seed_7()
    {
        var reg = ContentLoader.Load();
        var bosses = SeededRoster.SegmentBosses(reg, Dungeon);
        var finalBoss = bosses[^1];

        var result = PlaytestRunner.Play(new PlaytestOptions(2, 7, null) { DungeonId = Dungeon }, reg);

        var died = result.Log.Split('\n')
            .Where(line => line.StartsWith("E ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line.AsSpan(2).ToString()).RootElement)
            .Where(e => e.GetProperty("type").GetString() == "death")
            .Select(e => result.Run.State.Actors[e.GetProperty("actorId").GetInt32()].ClassId)
            .ToList();
        _out.WriteLine($"run_end {result.Run.RunEnd.Result} at {result.Run.RunEnd.ResultTick}; boss deaths: {string.Join(",", died.Where(c => bosses.Contains(c, StringComparer.Ordinal)))}");

        Assert.True(died.Contains(finalBoss, StringComparer.Ordinal), $"final boss {finalBoss} never died; run ended {result.Run.RunEnd.Result} at {result.Run.RunEnd.ResultTick}");
    }
}
