using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Playtest;
using Keyrun.Sim.Encounter;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The seeded two-branch dungeon played headless (D45/D46, f-46043bc6): the run's layout is generated from the seed
/// (RoomGraphGen, WalkGridGen, BranchFill through <see cref="SeededLayout"/>), the autorun party (lowest living seat
/// leads, D44) walks the branch the seed picks and clears it to the boss. Seed 1 and a seed that picks the other branch,
/// both at key 2: each run is timed, kills the boss and enters only its own branch's rooms.
/// </summary>
public sealed class SeededBranchRunTests
{
    private readonly ITestOutputHelper _out;

    public SeededBranchRunTests(ITestOutputHelper output) => _out = output;

    /// <summary>The first seed from 2 up whose bot branch differs from seed 1's.</summary>
    internal static long OtherBranchSeed()
    {
        var one = SeededLayout.BotBranch(1);
        for (long s = 2; s < 1000; s++)
        {
            if (SeededLayout.BotBranch(s) != one)
            {
                return s;
            }
        }
        throw new InvalidOperationException("no seed picks the other branch");
    }

    /// <summary>The first seed whose walked branch teaches the Seed (its second pull), so both second lessons are played.</summary>
    internal static long SeedBranchSeed()
    {
        var reg = ContentLoader.Load();
        for (long s = 1; s < 1000; s++)
        {
            var g = Keyrun.Sim.Dungeon.RoomGraphGen.Generate(s, SeededLayout.Params);
            var fill = Keyrun.Sim.Dungeon.BranchFill.Fill(s, g, SeededRoster.Teaching(reg), SeededRoster.Optional(reg), SeededRoster.Params(reg));
            if (fill.Branches[SeededLayout.BotBranch(s)].SecondPull == "seed")
            {
                return s;
            }
        }
        throw new InvalidOperationException("no seed walks the Seed's branch");
    }

    [Fact(DisplayName = "Seeded branches (key 2, the first seed whose branch teaches the Seed): the party clears it to the boss within par")]
    public void Seed_branch_clears_within_par() => ClearsItsBranch(SeedBranchSeed());

    [Fact(DisplayName = "Seeded branches: two seeds pick different branches")]
    public void Two_seeds_pick_different_branches()
    {
        var other = OtherBranchSeed();
        Assert.NotEqual(SeededLayout.BotBranch(1), SeededLayout.BotBranch(other));
        Assert.InRange(SeededLayout.BotBranch(1), 0, 1);
        Assert.InRange(SeededLayout.BotBranch(other), 0, 1);
    }

    [Fact(DisplayName = "Seeded branches (key 2, seed 1): the party clears its branch to the boss within par")]
    public void Seed_one_clears_its_branch_within_par() => ClearsItsBranch(1);

    [Fact(DisplayName = "Seeded branches (key 2, the other-branch seed): the party clears the other branch to the boss within par")]
    public void Other_seed_clears_the_other_branch_within_par() => ClearsItsBranch(OtherBranchSeed());

    private void ClearsItsBranch(long seed)
    {
        var seeded = SeededLayout.Build(ContentLoader.Load(), seed);
        var result = PlaytestRunner.Play(new PlaytestOptions(2, seed, null), seeded.Registry);
        var events = result.Log.Split('\n')
            .Where(line => line.StartsWith("E ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line.AsSpan(2).ToString()).RootElement)
            .ToList();
        var forces = seeded.Registry.Forces;
        var table = forces.Values.ToDictionary(v => v.EnemyId, v => v.Points, StringComparer.Ordinal);
        var reading = Forces.Read(result.Run.State, table, forces.Required);
        var entered = events.Where(e => Str(e, "type") == "room_enter").Select(e => Str(e, "roomId") ?? Str(e, "room") ?? string.Empty).Distinct().ToList();
        var cleared = events.Where(e => Str(e, "type") == "pull_clear").Select(e => Str(e, "pullId")).ToList();
        _out.WriteLine($"seed {seed}: branch {seeded.Branch} rooms [{string.Join(",", seeded.Fill.Branches[seeded.Branch].Rooms)}] result {result.Run.RunEnd.Result} at {result.Run.RunEnd.ResultTick} of par {seeded.Registry.Dungeons["hollow-vault"].ParTicks}; pulls cleared [{string.Join(",", cleared)}]; forces {reading.Points}/{reading.Required}; rooms entered [{string.Join(",", entered)}]");

        Assert.Equal(RunResult.Timed, result.Run.RunEnd.Result);
        Assert.Contains(LayoutDef.BossPullId, cleared);
        Assert.True(result.Run.RunEnd.ResultTick <= seeded.Registry.Dungeons["hollow-vault"].ParTicks, $"seed {seed}: ended at {result.Run.RunEnd.ResultTick}");
        var side = seeded.Registry.Layout.SideBoxes.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(side);
        Assert.DoesNotContain(entered, side.Contains);
        Assert.All(seeded.Fill.Branches[seeded.Branch].Rooms, r => Assert.Contains(SeededLayout.RoomBoxId(r), entered));
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
