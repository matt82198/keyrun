// G2.2 design/ABILITY-TREES.md s5.5 item 5 ProgressionSaveTreesTests: a canonical v2 round trip; the v1 migration; every fail-closed case in s5.3.
// R-TREES-1 shape choice (the design leaves the API open): Progression gains Trees (IReadOnlyDictionary<string, TreeState>
// keyed by class id); ProgressionSave.Version becomes 2; ProgressionSave.Load gains a third argument, the class the v1 save
// last queued, which the v1 migration credits (s5.3 "credited to the class the save last queued"). Red until S1 PR 4.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>The v2 tree member of the progression save (ABILITY-TREES s5.3).</summary>
public sealed class ProgressionSaveTreesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keyrun-trees-" + Guid.NewGuid().ToString("N"));

    /// <summary>Removes the scratch directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Version_is_2_and_a_v2_progression_with_trees_round_trips_canonically()
    {
        // s5.5 item 5: a canonical v2 round trip (same progression, same text; trees survive the trip).
        Assert.Equal(2, ProgressionSave.Version);
        Progression built = Progression.Start(7) with
        {
            Trees = new Dictionary<string, TreeState>
            {
                ["warden"] = new TreeState(Earned: 3, BestKey: 4, Nodes: new[] { "warden.strike.rank2", "warden.strike.enhanced" }),
            },
        };

        string text = ProgressionSave.Serialize(built);
        ProgressionLoad loaded = ProgressionSave.Load(text, seed: 7, lastQueuedClassId: "warden");

        Assert.Null(loaded.Error);
        Assert.Equal(text, ProgressionSave.Serialize(loaded.Progression));
        Assert.Equal(built.Trees["warden"], loaded.Progression.Trees["warden"]);
    }

    [Fact]
    public void A_v1_save_migrates_completions_into_points_credited_to_the_last_queued_class()
    {
        // s5.5 item 5: the v1 migration. Timed and depleted history entries count (5 and 3, bestKey 5); abandoned does not.
        const string v1 = "{\"v\":1,\"ash\":0,\"runs\":3,\"prng\":[1,2,3,4],\"equipped\":[],\"last\":null,\"talents\":{},"
            + "\"history\":[{\"key\":5,\"result\":\"timed\",\"ascended\":false},{\"key\":3,\"result\":\"depleted\",\"ascended\":false},"
            + "{\"key\":9,\"result\":\"abandoned\",\"ascended\":false}],\"ascensionCards\":0}";

        ProgressionLoad loaded = ProgressionSave.Load(v1, seed: 7, lastQueuedClassId: "warden");

        Assert.Null(loaded.Error);
        TreeState warden = loaded.Progression.Trees["warden"];
        Assert.Equal(2, warden.Earned);
        Assert.Equal(5, warden.BestKey);
        Assert.Empty(warden.Nodes);
    }

    // Each s5.3 fail-closed case: the save is unusable (Error set, a fresh character), never silently accepted.
    public static TheoryData<string> FailClosedTreeMembers => new()
    {
        // unknown node id
        "{\"earned\":1,\"bestKey\":0,\"nodes\":[\"warden.nope\"]}",
        // missing requires: enhanced needs rank2, which is not owned
        "{\"earned\":2,\"bestKey\":1,\"nodes\":[\"warden.strike.enhanced\"]}",
        // minKey above bestKey: rank3 needs key 7, bestKey is 6
        "{\"earned\":4,\"bestKey\":6,\"nodes\":[\"warden.strike.rank2\",\"warden.strike.enhanced\",\"warden.strike.choice\",\"warden.strike.rank3\"]}",
        // more nodes than earned: two nodes, one point
        "{\"earned\":1,\"bestKey\":1,\"nodes\":[\"warden.strike.rank2\",\"warden.strike.enhanced\"]}",
    };

    [Theory]
    [MemberData(nameof(FailClosedTreeMembers))]
    public void A_bad_tree_member_makes_the_save_unusable_and_is_never_overwritten(string warden)
    {
        // s5.5 item 5: every fail-closed case in s5.3; SaveFile moves the unreadable save aside, never overwrites it.
        string text = "{\"v\":2,\"ash\":0,\"runs\":0,\"prng\":[1,2,3,4],\"equipped\":[],\"last\":null,\"talents\":{},"
            + "\"history\":[],\"ascensionCards\":0,\"trees\":{\"warden\":" + warden + "}}";

        ProgressionLoad loaded = ProgressionSave.Load(text, seed: 7, lastQueuedClassId: "warden");
        Assert.NotNull(loaded.Error);
        Assert.Empty(loaded.Progression.Trees);

        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, ProgressionSave.FileName);
        File.WriteAllText(path, text);
        Assert.Null(ProgressionSave.SaveFile(path, Progression.Start(7)));
        Assert.Contains(Directory.GetFiles(_dir), f => f.Contains(".unreadable-1"));
    }
}
