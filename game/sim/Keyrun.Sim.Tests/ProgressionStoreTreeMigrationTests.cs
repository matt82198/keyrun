// Pins the Godot-free host progression store (game/src/HostCore ProgressionStoreCore) to the class-id v1 migration path of
// ProgressionSave (ABILITY-TREES s5.3; row f-6ee6bccf, G2.2 R-TREES-3 (b) residual). The host must pass the last queued
// class to the load, or a v1 save's history never becomes tree points for the human player. These are pins: the core
// already exists, so each fact is expected green; the structural guard is the 2-arg comparison in the first fact.
using System;
using System.Collections.Generic;
using System.IO;
using Keyrun.Host;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>The host progression store's tree migration and warn-once behaviour, exercised without Godot.</summary>
public sealed class ProgressionStoreTreeMigrationTests
{
    // A v1 save with timed and depleted history (counts toward tree points), no trees member.
    private const string V1Save = "{\"v\":1,\"ash\":0,\"runs\":3,\"prng\":[1,2,3,4],\"equipped\":[],\"last\":null,\"talents\":{},"
        + "\"history\":[{\"key\":5,\"result\":\"timed\",\"ascended\":false},{\"key\":3,\"result\":\"depleted\",\"ascended\":false},"
        + "{\"key\":9,\"result\":\"abandoned\",\"ascended\":false}],\"ascensionCards\":0}";

    private static string NewDir()
    {
        return Path.Combine(Path.GetTempPath(), "keyrun-store-migration-" + Guid.NewGuid().ToString("N"));
    }

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string SaveText(Progression progression) => ProgressionSave.Serialize(progression);

    private static Progression WardenTrees() => Progression.Start(7) with
    {
        Trees = new Dictionary<string, TreeState>
        {
            ["warden"] = new TreeState(Earned: 3, BestKey: 4, Nodes: new[] { "warden.strike.rank2", "warden.strike.enhanced" }),
        },
    };

    [Fact]
    public void Core_load_with_class_id_migrates_a_v1_save()
    {
        string dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            var core = new ProgressionStoreCore(dir, _ => { });
            File.WriteAllText(core.FilePath, V1Save);

            Progression loaded = core.Load(7, "warden");
            Progression withClass = ProgressionSave.LoadFile(core.FilePath, 7, "warden").Progression;
            Progression withoutClass = ProgressionSave.LoadFile(core.FilePath, 7).Progression;

            // Structural guard: if the core drops the class id, the migrated trees vanish and this goes red.
            Assert.Equal(SaveText(withClass), SaveText(loaded));
            Assert.NotEqual(SaveText(withoutClass), SaveText(loaded));
            Assert.Equal(2, loaded.Trees["warden"].Earned);
            Assert.Equal(5, loaded.Trees["warden"].BestKey);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Core_load_without_class_id_matches_two_arg_load()
    {
        string dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            var core = new ProgressionStoreCore(dir, _ => { });
            File.WriteAllText(core.FilePath, V1Save);

            Progression loaded = core.Load(7);
            Progression twoArg = ProgressionSave.LoadFile(core.FilePath, 7).Progression;

            Assert.Equal(SaveText(twoArg), SaveText(loaded));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Core_load_of_a_v2_save_is_unchanged_by_class_id()
    {
        string dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            var core = new ProgressionStoreCore(dir, _ => { });
            File.WriteAllText(core.FilePath, SaveText(WardenTrees()));

            Progression withClass = core.Load(7, "warden");
            Progression withoutClass = core.Load(7);
            Progression twoArg = ProgressionSave.LoadFile(core.FilePath, 7).Progression;

            Assert.Equal(SaveText(twoArg), SaveText(withClass));
            Assert.Equal(SaveText(twoArg), SaveText(withoutClass));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Core_save_then_load_round_trips()
    {
        string dir = NewDir();
        try
        {
            var warnings = new List<string>();
            var core = new ProgressionStoreCore(dir, warnings.Add);
            Progression built = WardenTrees();

            Assert.True(core.Save(built));
            Progression loaded = core.Load(7, "warden");

            Assert.Equal(SaveText(built), SaveText(loaded));
            Assert.Equal(built.Trees["warden"], loaded.Trees["warden"]);
            Assert.Empty(warnings);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Core_warns_once_on_an_unreadable_file()
    {
        string dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            var warnings = new List<string>();
            var core = new ProgressionStoreCore(dir, warnings.Add);
            File.WriteAllText(core.FilePath, "{ this is not a progression save");

            core.Load(7, "warden");
            core.Load(7, "warden");

            Assert.Single(warnings);
            Assert.StartsWith("PROGRESSION ", warnings[0], StringComparison.Ordinal);
            Assert.EndsWith("(" + core.FilePath + ")", warnings[0], StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dir);
        }
    }
}
