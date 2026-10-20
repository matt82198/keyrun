using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE s6 treats the talents picked on the between-runs screen as the player's build, so they must survive a game
/// restart: <see cref="ProgressionSave"/> keeps each class's picks (forked ability id to option id) in a versioned
/// <c>talents</c> field of the same save file. A save written before the field existed still loads (with no picks), a
/// talents block this build cannot read makes the save unreadable (so PR 241's move-aside keeps it), and
/// <see cref="TalentRestore"/> drops a saved pick the current content no longer has, with a line saying so.
/// </summary>
public sealed class ProgressionTalentSaveTests : IDisposable
{
    private readonly string _dir;

    /// <summary>Creates this test's private temp directory.</summary>
    public ProgressionTalentSaveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "keyrun-talent-save-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>Removes the temp directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static Item Weapon() =>
        new("reward-weapon-edge-t3-0", "weapon", 3, new Facet("weapon-edge", "Strike also applies \"Sundered\"", "ability-1"), Rewards.StartingCharges);

    private static Progression Played() => Progression.Start(7).BankRun(RunResult.Timed).Take(Weapon());

    private static SavedTalents Picks() => SavedTalents.None
        .With("warden", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["warden.guardBreak"] = "warden.guardBreak.wide",
            ["warden.strike"] = "warden.strike.keen",
        })
        .With("chanter", new Dictionary<string, string>(StringComparer.Ordinal) { ["chanter.verse"] = "chanter.verse.long" });

    private static void AssertPicks(SavedTalents expected, SavedTalents actual)
    {
        Assert.Equal(expected.Classes.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Classes.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (cls, choices) in expected.Classes)
        {
            Assert.Equal(
                choices.OrderBy(kv => kv.Key, StringComparer.Ordinal),
                actual.For(cls).OrderBy(kv => kv.Key, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Picks_saved_with_the_progression_come_back_after_a_reload()
    {
        var text = ProgressionSave.Serialize(Played(), Picks());
        var loaded = ProgressionSave.Load(text, seed: 999);

        Assert.Null(loaded.Error);
        AssertPicks(Picks(), loaded.Talents);
        Assert.Equal(Played().AshTotal, loaded.Progression.AshTotal);
        Assert.Equal(text, ProgressionSave.Serialize(loaded.Progression, loaded.Talents));
    }

    [Fact]
    public void Picks_survive_a_save_file_and_a_fresh_load_of_it()
    {
        var path = Path.Combine(_dir, ProgressionSave.FileName);

        Assert.Null(ProgressionSave.SaveFile(path, Played(), Picks()));
        var loaded = ProgressionSave.LoadFile(path, seed: 1);

        Assert.Null(loaded.Error);
        AssertPicks(Picks(), loaded.Talents);
        Assert.Equal(new[] { path }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void Serialized_picks_are_the_same_text_whatever_order_they_were_made_in()
    {
        var reversed = SavedTalents.None
            .With("chanter", new Dictionary<string, string>(StringComparer.Ordinal) { ["chanter.verse"] = "chanter.verse.long" })
            .With("warden", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["warden.strike"] = "warden.strike.keen",
                ["warden.guardBreak"] = "warden.guardBreak.wide",
            });

        Assert.Equal(ProgressionSave.Serialize(Played(), Picks()), ProgressionSave.Serialize(Played(), reversed));
    }

    [Fact]
    public void A_save_written_before_the_talents_field_loads_with_no_picks()
    {
        const string old = "{\"ash\":30,\"equipped\":[],\"last\":null,\"prng\":[1,2,3,4],\"runs\":1,\"v\":1}";

        var loaded = ProgressionSave.Load(old, seed: 5);

        Assert.Null(loaded.Error);
        Assert.Equal(30, loaded.Progression.AshTotal);
        Assert.Equal(0, loaded.Talents.Count);
        Assert.Empty(loaded.Talents.For("warden"));
    }

    [Fact]
    public void No_picks_write_exactly_the_old_save_text()
    {
        Assert.Equal(ProgressionSave.Serialize(Played()), ProgressionSave.Serialize(Played(), SavedTalents.None));
        Assert.DoesNotContain("talents", ProgressionSave.Serialize(Played()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_respecced_class_drops_out_of_the_save()
    {
        var respecced = Picks().With("warden", new Dictionary<string, string>(StringComparer.Ordinal));

        var loaded = ProgressionSave.Load(ProgressionSave.Serialize(Played(), respecced), seed: 1);

        Assert.Empty(loaded.Talents.For("warden"));
        Assert.Equal("chanter.verse.long", loaded.Talents.For("chanter")["chanter.verse"]);
    }

    [Theory]
    [InlineData("{\"v\":2,\"classes\":{}}")]
    [InlineData("{\"classes\":{}}")]
    [InlineData("[]")]
    [InlineData("{\"v\":1,\"classes\":[]}")]
    [InlineData("{\"v\":1,\"classes\":{\"warden\":{\"warden.strike\":3}}}")]
    [InlineData("{\"v\":1,\"classes\":{\"warden\":{\"warden.strike\":\"\"}}}")]
    [InlineData("{\"v\":1,\"classes\":{\"warden\":[]}}")]
    public void A_talents_block_this_build_cannot_read_makes_the_save_unreadable(string talents)
    {
        var text = "{\"ash\":30,\"equipped\":[],\"last\":null,\"prng\":[1,2,3,4],\"runs\":1,\"talents\":" + talents + ",\"v\":1}";

        var loaded = ProgressionSave.Load(text, seed: 5);

        Assert.False(string.IsNullOrEmpty(loaded.Error));
        Assert.Equal(0, loaded.Progression.AshTotal);
        Assert.Equal(0, loaded.Talents.Count);
    }

    [Fact]
    public void A_save_with_newer_talents_is_moved_aside_not_overwritten()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ProgressionSave.FileName);
        // Canonical JSON sorts keys, so the text ends with the talents block's own "v" and then the save's "v".
        var current = ProgressionSave.Serialize(Played(), Picks());
        Assert.EndsWith(",\"v\":1},\"v\":1}", current, StringComparison.Ordinal);
        var newer = current[..^",\"v\":1},\"v\":1}".Length] + ",\"v\":2},\"v\":1}";
        File.WriteAllText(path, newer);
        var original = File.ReadAllBytes(path);
        var loaded = ProgressionSave.LoadFile(path, seed: 9);
        Assert.False(string.IsNullOrEmpty(loaded.Error));

        Assert.Null(ProgressionSave.SaveFile(path, loaded.Progression.BankRun(RunResult.Timed), loaded.Talents));

        var kept = Directory.GetFiles(_dir).Where(f => f != path).ToArray();
        Assert.Single(kept);
        Assert.Equal(original, File.ReadAllBytes(kept[0]));
        Assert.Null(ProgressionSave.LoadFile(path, seed: 1).Error);
    }

    private static readonly IReadOnlyList<TalentForkIds> WardenForks = new[]
    {
        new TalentForkIds("warden.strike", new[] { "warden.strike.keen", "warden.strike.heavy" }),
        new TalentForkIds("warden.guardBreak", new[] { "warden.guardBreak.long", "warden.guardBreak.wide" }),
    };

    [Fact]
    public void Restore_maps_saved_picks_onto_fork_and_option_indices()
    {
        var restore = TalentRestore.Match(Picks().For("warden"), WardenForks);

        Assert.Equal(new[] { (0, 0), (1, 1) }, restore.Picks);
        Assert.Empty(restore.Dropped);
    }

    [Fact]
    public void Restore_drops_an_unknown_talent_id_and_says_which_and_why()
    {
        var saved = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["warden.strike"] = "warden.strike.removed",
            ["warden.gone"] = "warden.gone.any",
            ["warden.guardBreak"] = "warden.guardBreak.wide",
        };

        var restore = TalentRestore.Match(saved, WardenForks);

        Assert.Equal(new[] { (1, 1) }, restore.Picks);
        Assert.Equal(
            new[]
            {
                "ability=warden.gone option=warden.gone.any reason=unknown-fork",
                "ability=warden.strike option=warden.strike.removed reason=unknown-option",
            },
            restore.Dropped);
    }

    [Fact]
    public void Restore_of_nothing_saved_picks_nothing()
    {
        var restore = TalentRestore.Match(SavedTalents.None.For("warden"), WardenForks);

        Assert.Empty(restore.Picks);
        Assert.Empty(restore.Dropped);
    }
}
