using System;
using System.IO;
using System.Linq;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE s7 "After the cap: Ascension ... Ascension marks on the character" (finding f-275d47c9). Every banked run
/// records the key it was played at, how it ended, and whether the character was already ascended
/// (<see cref="RunRecord"/>), in <see cref="Progression.History"/> and in the save, so the marks are counted from saved
/// data: a mark is a timed run above key <see cref="Ascension.MarkKeyAbove"/> played while ascended.
/// </summary>
public sealed class AscensionMarksTests : IDisposable
{
    private readonly string _dir;

    /// <summary>Creates this test's private temp directory.</summary>
    public AscensionMarksTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "keyrun-ascension-marks-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>Removes the temp directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static Item At(string slot, int tier) =>
        new($"reward-{slot}-edge-t{tier}-0", slot, tier, new Facet($"{slot}-edge", "Strike also applies Sundered", "ability-1"), Rewards.StartingCharges);

    // Ascended (all five slots at tier 10) after one plain bank.
    private static Progression Ascended() =>
        Rewards.GearSlots.Aggregate(Progression.Start(7).BankRun(RunResult.Timed), (p, s) => p.Take(At(s, Progression.MaxTier)));

    // Two marks (timed +12, timed +11) among five ascended runs, and one pre-cap timed +15 that is not a mark.
    private static Progression Marked()
    {
        var early = Progression.Start(7).BankRun(RunResult.Timed, 15);
        var ascended = Rewards.GearSlots.Aggregate(early, (p, s) => p.Take(At(s, Progression.MaxTier)));
        return ascended
            .BankRun(RunResult.Timed, 12)
            .BankRun(RunResult.Timed, 10)
            .BankRun(RunResult.Depleted, 15)
            .BankRun(RunResult.Abandoned, 14)
            .BankRun(RunResult.Timed, 11);
    }

    [Fact]
    public void Banking_a_run_records_its_key_result_and_whether_the_character_was_ascended()
    {
        var p = Progression.Start(7).BankRun(RunResult.Depleted, 7);
        Assert.Equal(new[] { new RunRecord(7, RunResult.Depleted, Ascended: false) }, p.History);
        Assert.Equal(1, p.Runs);
        Assert.Equal(Ash.Depleted, p.AshTotal);

        var q = Ascended().BankRun(RunResult.Timed, 12);
        Assert.Equal(new RunRecord(12, RunResult.Timed, Ascended: true), q.History[^1]);
        Assert.Equal(Ash.Timed * 2, q.AshTotal);
    }

    [Fact]
    public void A_mark_is_a_timed_run_above_key_10_played_while_ascended()
    {
        Assert.True(Ascension.IsMark(new RunRecord(11, RunResult.Timed, true)));
        Assert.False(Ascension.IsMark(new RunRecord(10, RunResult.Timed, true)));
        Assert.False(Ascension.IsMark(new RunRecord(20, RunResult.Depleted, true)));
        Assert.False(Ascension.IsMark(new RunRecord(20, RunResult.Abandoned, true)));
        Assert.False(Ascension.IsMark(new RunRecord(20, RunResult.Timed, false)));
    }

    [Fact]
    public void Marks_count_only_the_ascended_timed_runs_above_key_10()
    {
        Assert.Equal(2, Ascension.Marks(Marked()));
        Assert.Equal(0, Ascension.Marks(Ascended()));
        Assert.Equal(0, Ascension.Marks(Progression.Start(7)));
    }

    [Fact]
    public void Marks_survive_a_save_file_and_a_fresh_load_of_it()
    {
        var path = Path.Combine(_dir, ProgressionSave.FileName);
        var played = Marked();

        Assert.Null(ProgressionSave.SaveFile(path, played));
        var loaded = ProgressionSave.LoadFile(path, seed: 1);

        Assert.Null(loaded.Error);
        Assert.Equal(played.History, loaded.Progression.History);
        Assert.Equal(2, Ascension.Marks(loaded.Progression));
        Assert.True(Ascension.IsAscended(loaded.Progression));
        Assert.Equal(ProgressionSave.Serialize(played), ProgressionSave.Serialize(loaded.Progression));
    }

    [Fact]
    public void A_mark_earned_after_a_reload_adds_to_the_saved_count()
    {
        var path = Path.Combine(_dir, ProgressionSave.FileName);
        Assert.Null(ProgressionSave.SaveFile(path, Marked()));

        var next = ProgressionSave.LoadFile(path, seed: 1).Progression.BankRun(RunResult.Timed, 13);
        Assert.Null(ProgressionSave.SaveFile(path, next));

        Assert.Equal(3, Ascension.Marks(ProgressionSave.LoadFile(path, seed: 1).Progression));
    }

    [Fact]
    public void A_save_written_before_the_history_field_loads_with_no_runs_recorded()
    {
        const string old = "{\"ash\":30,\"equipped\":[],\"last\":null,\"prng\":[1,2,3,4],\"runs\":1,\"talents\":{},\"v\":1}";

        var loaded = ProgressionSave.Load(old, seed: 5);

        Assert.Null(loaded.Error);
        Assert.Equal(1, loaded.Progression.Runs);
        Assert.Empty(loaded.Progression.History);
        Assert.Equal(0, Ascension.Marks(loaded.Progression));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"key\":0,\"result\":\"timed\",\"ascended\":true}]")]
    [InlineData("[{\"key\":12,\"result\":\"won\",\"ascended\":true}]")]
    [InlineData("[{\"key\":12,\"result\":\"timed\"}]")]
    [InlineData("[{\"key\":12,\"result\":\"timed\",\"ascended\":1}]")]
    [InlineData("[{\"key\":\"12\",\"result\":\"timed\",\"ascended\":true}]")]
    [InlineData("[{\"key\":12,\"result\":\"timed\",\"ascended\":true},{\"key\":12,\"result\":\"timed\",\"ascended\":true}]")]
    public void A_history_this_build_cannot_read_makes_the_save_unreadable(string history)
    {
        // runs is 1, so the last case (two records for one banked run) is inconsistent too.
        var text = "{\"ash\":30,\"equipped\":[],\"history\":" + history + ",\"last\":null,\"prng\":[1,2,3,4],\"runs\":1,\"talents\":{},\"v\":1}";

        var loaded = ProgressionSave.Load(text, seed: 5);

        Assert.False(string.IsNullOrEmpty(loaded.Error));
        Assert.Empty(loaded.Progression.History);
    }

    [Fact]
    public void A_key_below_1_cannot_be_banked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Progression.Start(7).BankRun(RunResult.Timed, 0));
    }

    [Fact]
    public void The_state_the_screen_shows_is_the_same_read_the_run_uses()
    {
        foreach (var p in new[] { Progression.Start(7), Ascended(), Marked(), Ascended().Take(At("ring", 9)) })
        {
            var state = Ascension.Of(p);
            Assert.Equal(Ascension.IsAscended(p), state.Ascended);
            Assert.Equal(Ascension.Marks(p), state.Marks);
            Assert.Equal(p.History.Count == 0 ? null : p.History[^1], state.LastRun);
        }
        Assert.Equal(4, Ascension.Of(Ascended().Take(At("ring", 9))).SlotsAtCap);
        Assert.Equal(0, Ascension.Of(Progression.Start(7)).SlotsAtCap);
        Assert.True(Ascension.Of(Marked()).LastRunMarked);
        Assert.False(Ascension.Of(Marked().BankRun(RunResult.Timed, 9)).LastRunMarked);
    }
}
