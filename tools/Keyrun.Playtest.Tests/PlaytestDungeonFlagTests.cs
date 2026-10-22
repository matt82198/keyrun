using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// f-3bec9ba9: the playtest CLI accepts <c>--dungeon ID</c> (default hollow-vault) and its summary.json reports the
/// Enemy Forces bar and the kill count from the sim's own counters (<see cref="Keyrun.Sim.Encounter.Forces"/>). Each test
/// drives <see cref="Program.Main"/> end to end and reads the file it writes, so it proves the flag reaches the run.
/// </summary>
public sealed class PlaytestDungeonFlagTests
{
    /// <summary>Runs the CLI into a fresh temp directory and returns the exit code and the parsed summary.json.</summary>
    /// <param name="extra">Extra CLI arguments appended after the run arguments.</param>
    /// <returns>The exit code, the output directory, and the parsed summary.</returns>
    private static (int Exit, string Dir, JsonDocument? Summary) RunCli(params string[] extra)
    {
        var dir = Path.Combine(Path.GetTempPath(), "playtest-dungeon-flag-" + Guid.NewGuid().ToString("N"));
        var args = new[] { "run", "--key", "2", "--seed", "1", "--out", dir, "--max-ticks", "200" };
        var exit = Program.Main([.. args, .. extra]);
        var path = Path.Combine(dir, "summary.json");
        return (exit, dir, File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null!);
    }

    [Fact(DisplayName = "run --dungeon sunken-bellfoundry plays that dungeon: summary.json names it")]
    public void Dungeon_flag_reaches_the_run()
    {
        var (exit, dir, doc) = RunCli("--dungeon", "sunken-bellfoundry");
        try
        {
            Assert.Equal(0, exit);
            Assert.NotNull(doc);
            Assert.Equal("sunken-bellfoundry", doc.RootElement.GetProperty("dungeon").GetString());
        }
        finally
        {
            doc?.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(DisplayName = "run without --dungeon keeps hollow-vault as the default")]
    public void Dungeon_defaults_to_hollow_vault()
    {
        var (exit, dir, doc) = RunCli();
        try
        {
            Assert.Equal(0, exit);
            Assert.NotNull(doc);
            Assert.Equal("hollow-vault", doc.RootElement.GetProperty("dungeon").GetString());
        }
        finally
        {
            doc?.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact(DisplayName = "summary.json carries numeric forcesPercent, integer forcesPoints/forcesRequired and integer kills")]
    public void Summary_reports_forces_and_kills()
    {
        var (exit, dir, doc) = RunCli("--dungeon", "sunken-bellfoundry");
        try
        {
            Assert.Equal(0, exit);
            Assert.NotNull(doc);
            var root = doc.RootElement;
            Assert.Equal(JsonValueKind.Number, root.GetProperty("forcesPercent").ValueKind);
            Assert.Equal(JsonValueKind.Number, root.GetProperty("forcesPoints").ValueKind);
            Assert.Equal(JsonValueKind.Number, root.GetProperty("forcesRequired").ValueKind);
            Assert.True(root.GetProperty("kills").TryGetInt32(out var kills));
            Assert.True(kills >= 0);
            Assert.True(root.GetProperty("forcesPercent").GetDouble() >= 0);
        }
        finally
        {
            doc?.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
