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
/// BIBLE s7 "Every run banks Ash, win or lose" and the gear the end-of-run choice equips must survive the process:
/// <see cref="ProgressionSave"/> is a pure, deterministic codec for <see cref="Progression"/> plus a file load/save
/// that never throws. A missing or corrupt save loads as empty progression and reports why. Every file test works in
/// its own temp directory, never the real Godot user dir.
/// </summary>
public sealed class ProgressionSaveTests : IDisposable
{
    private static readonly IReadOnlyList<Facet> WeaponPool = new[]
    {
        new Facet("warden.weapon.keenEdge", "Strike becomes a narrow frontal cone.", "warden.strike"),
        new Facet("warden.weapon.wideningBreak", "Guard Break trades range for a wider cone.", "warden.guardBreak"),
        new Facet("warden.weapon.twinStrike", "Reckoning becomes a piercing line.", "warden.reckoning"),
    };

    private readonly string _dir;

    /// <summary>Creates this test's private temp directory.</summary>
    public ProgressionSaveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "keyrun-progression-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>Removes the temp directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static Item Weapon(int tier = 3) =>
        new("reward-weapon-edge-t3-0", "weapon", tier, new Facet("weapon-edge", "Strike also applies \"Sundered\"", "ability-1"), Rewards.StartingCharges);

    private static Item Armor(int tier = 5) =>
        new("reward-armor-plate-t5-1", "armor", tier, new Facet("armor-plate", "Guard lasts longer", "ability-2"), Rewards.StartingCharges);

    // Two banked runs, two slots filled, one crafting charge spent: every field of Progression is non-default.
    private static Progression Played()
    {
        var p = Progression.Start(7).BankRun(RunResult.Timed).Take(Weapon()).BankRun(RunResult.Depleted).Take(Armor());
        return p.Reroll("weapon", WeaponPool).Progression;
    }

    private static void AssertSame(Progression expected, Progression actual)
    {
        Assert.Equal(expected.AshTotal, actual.AshTotal);
        Assert.Equal(expected.Runs, actual.Runs);
        Assert.Equal(expected.Prng, actual.Prng);
        Assert.Equal(expected.LastRun, actual.LastRun);
        Assert.Equal(expected.Equipped.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Equipped.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (slot, item) in expected.Equipped)
        {
            Assert.Equal(item, actual.Equipped[slot]);
        }
    }

    [Fact]
    public void Round_trip_keeps_ash_runs_gear_crafting_prng_and_last_run()
    {
        var played = Played();
        Assert.Equal(Ash.Timed + Ash.Depleted, played.AshTotal);
        Assert.Equal(1, played.Equipped["weapon"].Charges);

        var text = ProgressionSave.Serialize(played);
        var loaded = ProgressionSave.Load(text, seed: 999);

        Assert.Null(loaded.Error);
        AssertSame(played, loaded.Progression);
        Assert.Equal(text, ProgressionSave.Serialize(loaded.Progression));
    }

    [Fact]
    public void Serialize_is_deterministic_whatever_order_the_slots_were_filled()
    {
        var a = Progression.Start(3).Take(Weapon()).Take(Armor());
        var b = Progression.Start(3).Take(Armor()).Take(Weapon());

        Assert.Equal(ProgressionSave.Serialize(a), ProgressionSave.Serialize(b));
        Assert.Equal(ProgressionSave.Serialize(a), ProgressionSave.Serialize(a));
    }

    [Fact]
    public void A_reloaded_character_keeps_banking_and_crafting_exactly_where_it_left_off()
    {
        var played = Played();
        var reloaded = ProgressionSave.Load(ProgressionSave.Serialize(played), seed: 999).Progression;

        var next = reloaded.BankRun(RunResult.Abandoned);
        Assert.Equal(Ash.Timed + Ash.Depleted + Ash.Abandoned, next.AshTotal);
        Assert.Equal(3, next.Runs);

        var (_, expected) = played.Reroll("weapon", WeaponPool);
        var (_, actual) = reloaded.Reroll("weapon", WeaponPool);
        Assert.Equal(expected!.Item, actual!.Item);
        Assert.Equal(expected.Prng, actual.Prng);
    }

    [Fact]
    public void No_save_text_is_a_fresh_character_with_no_error()
    {
        var loaded = ProgressionSave.Load(null, seed: 42);

        Assert.Null(loaded.Error);
        AssertSame(Progression.Start(42), loaded.Progression);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"v\":2,\"ash\":0,\"runs\":0,\"prng\":[1,2,3,4],\"equipped\":[],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":-1,\"runs\":0,\"prng\":[1,2,3,4],\"equipped\":[],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":-1,\"prng\":[1,2,3,4],\"equipped\":[],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":0,\"prng\":[0,0,0,0],\"equipped\":[],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":0,\"prng\":[1,2,3,4294967296],\"equipped\":[],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":0,\"prng\":[1,2,3],\"equipped\":[],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":0,\"prng\":[1,2,3,4],\"equipped\":[{\"id\":\"x\",\"slot\":\"hat\",\"tier\":1,\"charges\":2,\"facet\":{\"id\":\"f\",\"text\":\"t\",\"ability\":\"a\"}}],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":0,\"prng\":[1,2,3,4],\"equipped\":[{\"id\":\"x\",\"slot\":\"weapon\",\"tier\":11,\"charges\":2,\"facet\":{\"id\":\"f\",\"text\":\"t\",\"ability\":\"a\"}}],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":0,\"prng\":[1,2,3,4],\"equipped\":[{\"id\":\"x\",\"slot\":\"weapon\",\"tier\":0,\"charges\":2,\"facet\":{\"id\":\"f\",\"text\":\"t\",\"ability\":\"a\"}}],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":0,\"prng\":[1,2,3,4],\"equipped\":[{\"id\":\"x\",\"slot\":\"weapon\",\"tier\":1,\"charges\":3,\"facet\":{\"id\":\"f\",\"text\":\"t\",\"ability\":\"a\"}}],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":0,\"prng\":[1,2,3,4],\"equipped\":[{\"id\":\"x\",\"slot\":\"weapon\",\"tier\":1,\"charges\":2,\"facet\":{\"id\":\"f\",\"text\":\"t\",\"ability\":\"a\"}},{\"id\":\"y\",\"slot\":\"weapon\",\"tier\":1,\"charges\":2,\"facet\":{\"id\":\"f\",\"text\":\"t\",\"ability\":\"a\"}}],\"last\":null}")]
    [InlineData("{\"v\":1,\"ash\":0,\"runs\":0,\"prng\":[1,2,3,4],\"equipped\":[],\"last\":{\"result\":\"won\",\"ash\":50,\"taken\":null,\"was\":null}}")]
    public void Corrupt_save_text_loads_as_a_fresh_character_and_says_why(string text)
    {
        var loaded = ProgressionSave.Load(text, seed: 5);

        Assert.False(string.IsNullOrEmpty(loaded.Error));
        AssertSame(Progression.Start(5), loaded.Progression);
    }

    [Fact]
    public void Save_then_load_file_round_trips_in_a_new_directory_and_leaves_no_temp_file()
    {
        var path = Path.Combine(_dir, "nested", ProgressionSave.FileName);
        var played = Played();

        Assert.Null(ProgressionSave.SaveFile(path, played));
        var loaded = ProgressionSave.LoadFile(path, seed: 1);

        Assert.Null(loaded.Error);
        AssertSame(played, loaded.Progression);
        Assert.Equal(new[] { path }, Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void Saving_again_replaces_the_previous_save()
    {
        var path = Path.Combine(_dir, ProgressionSave.FileName);
        Assert.Null(ProgressionSave.SaveFile(path, Played()));
        var later = Played().BankRun(RunResult.Timed);

        Assert.Null(ProgressionSave.SaveFile(path, later));

        AssertSame(later, ProgressionSave.LoadFile(path, seed: 1).Progression);
    }

    [Fact]
    public void A_missing_file_loads_a_fresh_character_with_no_error()
    {
        var loaded = ProgressionSave.LoadFile(Path.Combine(_dir, "absent", ProgressionSave.FileName), seed: 9);

        Assert.Null(loaded.Error);
        AssertSame(Progression.Start(9), loaded.Progression);
    }

    [Fact]
    public void A_corrupt_file_loads_a_fresh_character_and_reports_the_error_without_throwing()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ProgressionSave.FileName);
        File.WriteAllText(path, "{\"v\":1,\"ash\":12");

        var loaded = ProgressionSave.LoadFile(path, seed: 9);

        Assert.False(string.IsNullOrEmpty(loaded.Error));
        AssertSame(Progression.Start(9), loaded.Progression);
    }

    [Fact]
    public void A_save_that_cannot_be_written_returns_the_error_without_throwing()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "a file where the save directory should be");

        var error = ProgressionSave.SaveFile(Path.Combine(blocker, ProgressionSave.FileName), Played());

        Assert.False(string.IsNullOrEmpty(error));
    }

    // A save from a newer build: a valid v1 body with the version bumped. This build cannot read it, but the player's
    // character is still in it, so it must survive the fresh character being banked over it.
    private static string NewerVersionText() =>
        ProgressionSave.Serialize(Played()).Replace("\"v\":1", "\"v\":2", StringComparison.Ordinal);

    // Loads the unreadable save at path, banks a run on the fresh character it falls back to, saves that over it, and
    // asserts the original bytes are still on disk beside the new save, which itself loads cleanly.
    private void AssertUnreadableSaveSurvivesTheNextBank(string originalText)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ProgressionSave.FileName);
        File.WriteAllText(path, originalText);
        var original = File.ReadAllBytes(path);

        var loaded = ProgressionSave.LoadFile(path, seed: 9);
        Assert.False(string.IsNullOrEmpty(loaded.Error));
        var banked = loaded.Progression.BankRun(RunResult.Timed);

        Assert.Null(ProgressionSave.SaveFile(path, banked));

        var kept = Directory.GetFiles(_dir).Where(f => f != path).ToArray();
        Assert.Single(kept);
        Assert.Equal(original, File.ReadAllBytes(kept[0]));
        var reloaded = ProgressionSave.LoadFile(path, seed: 1);
        Assert.Null(reloaded.Error);
        AssertSame(banked, reloaded.Progression);
    }

    [Fact]
    public void A_newer_version_save_is_kept_when_the_fresh_character_is_banked_over_it()
    {
        AssertUnreadableSaveSurvivesTheNextBank(NewerVersionText());
    }

    [Fact]
    public void A_garbage_save_is_kept_when_the_fresh_character_is_banked_over_it()
    {
        AssertUnreadableSaveSurvivesTheNextBank("{\"v\":1,\"ash\":12 not json at all");
    }

    [Fact]
    public void Every_unreadable_save_is_kept_and_no_earlier_backup_is_overwritten()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ProgressionSave.FileName);
        var first = NewerVersionText();
        const string second = "garbage written by something else";

        File.WriteAllText(path, first);
        Assert.Null(ProgressionSave.SaveFile(path, Progression.Start(1)));
        File.WriteAllText(path, second);
        Assert.Null(ProgressionSave.SaveFile(path, Progression.Start(2)));

        var kept = Directory.GetFiles(_dir).Where(f => f != path).Select(File.ReadAllText).OrderBy(t => t, StringComparer.Ordinal);
        Assert.Equal(new[] { first, second }.OrderBy(t => t, StringComparer.Ordinal), kept);
        AssertSame(Progression.Start(2), ProgressionSave.LoadFile(path, seed: 9).Progression);
    }

    [Fact]
    public void A_readable_save_is_replaced_in_place_with_no_backup_left_beside_it()
    {
        var path = Path.Combine(_dir, ProgressionSave.FileName);
        var played = Played();
        Assert.Null(ProgressionSave.SaveFile(path, played));
        var before = File.ReadAllBytes(path);

        var loaded = ProgressionSave.LoadFile(path, seed: 1);
        Assert.Null(loaded.Error);
        Assert.Null(ProgressionSave.SaveFile(path, loaded.Progression));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_dir));
    }
}
