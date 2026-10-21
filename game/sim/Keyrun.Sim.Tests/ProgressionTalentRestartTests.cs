using System;
using System.Collections.Generic;
using System.IO;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE s6 talent picks survive a restart (finding f-7d47bf98, PR #268 scenario): the picks the screen saves must be
/// on disk, and a fresh load from that file (a new process reading the save back) must carry the same picks. The codec
/// round trip is covered in <see cref="ProgressionSaveTests"/>; this pins the file path the game really uses
/// (<see cref="ProgressionSave.SaveFile"/> then <see cref="ProgressionSave.LoadFile"/>), which no other test drives.
/// </summary>
public sealed class ProgressionTalentRestartTests
{
    // The screen's build: ability id to picked option id (Warden Strike and Guard Break forks).
    private static readonly KeyValuePair<string, string>[] Build =
    {
        new("warden.strike", "rend"),
        new("warden.guardBreak", "pin"),
    };

    private static Progression Played() => Progression.Start(7).BankRun(RunResult.Timed);

    [Fact]
    public void Talent_picks_saved_to_the_progression_file_are_present_after_a_fresh_load_from_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "keyrun-talent-restart-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, ProgressionSave.FileName);
            var saved = Played().WithTalents(Build);

            // Launch 1: the screen saves the progression with its picks.
            var saveError = ProgressionSave.SaveFile(path, saved);
            Assert.Null(saveError);

            // Launch 2: a fresh load from the file alone rebuilds the progression, picks included.
            var loaded = ProgressionSave.LoadFile(path, seed: 999);

            Assert.Null(loaded.Error);
            Assert.Equal(Build.Length, loaded.Progression.Talents.Count);
            foreach (var (ability, option) in Build)
            {
                Assert.True(loaded.Progression.Talents.TryGetValue(ability, out var picked), $"pick for {ability} was lost on restart");
                Assert.Equal(option, picked);
            }
            Assert.Equal(saved.AshTotal, loaded.Progression.AshTotal);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
