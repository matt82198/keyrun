using System;
using System.IO;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Per-playtester memory (game/playtest/players/ID.json): the runs a playtester played, what each run showed, and the
/// lessons it wrote, each tied to a run whose receipt (summary and policy file hashes) can be checked on disk.
/// </summary>
public sealed class PlaytesterMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "keyrun-memory-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temp root.</summary>
    public PlaytesterMemoryTests()
    {
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private MemoryEntry Entry(int run, string summaryText)
    {
        var summary = Path.Combine(_root, $"summary-{run}.json");
        File.WriteAllText(summary, summaryText);
        var receipt = new MemoryReceipt(summary, PlaytesterMemory.Sha256Of(summary), null, null);
        return new MemoryEntry(run, 2, run, "untimed (wiped)", 2, receipt, new[] { "result: untimed (wiped)" }, Array.Empty<string>());
    }

    [Fact(DisplayName = "Memory: a missing file is a playtester with no runs")]
    public void Missing_file_is_empty()
    {
        var m = PlaytesterMemory.Load(Path.Combine(_root, "nobody.json"), "nobody");
        Assert.Equal("nobody", m.PlayerId);
        Assert.Empty(m.Entries);
    }

    [Fact(DisplayName = "Memory: a lesson needs a run to cite, and cites the last run by default")]
    public void Lesson_cites_a_run()
    {
        var m = new PlaytesterMemory("p-01");
        Assert.Throws<InvalidOperationException>(() => m.AddLesson(null, "kick the healer"));
        m.Append(Entry(1, "{\"a\":1}"));
        m.Append(Entry(2, "{\"a\":2}"));
        Assert.Throws<ArgumentException>(() => m.AddLesson(3, "no such run"));
        Assert.Throws<ArgumentException>(() => m.AddLesson(null, "   "));
        var cited = m.AddLesson(null, "the caster in the first room healed its friends every few seconds");
        Assert.Equal(2, cited.Run);
        Assert.Single(m.Entries[1].Lessons);
        Assert.Empty(m.Entries[0].Lessons);
        Assert.Equal(1, m.AddLesson(1, "earlier note").Run);
    }

    [Fact(DisplayName = "Memory: runs are appended in order; a gap or repeat is refused")]
    public void Runs_append_in_order()
    {
        var m = new PlaytesterMemory("p-02");
        Assert.Throws<ArgumentException>(() => m.Append(Entry(2, "{}")));
        m.Append(Entry(1, "{}"));
        Assert.Throws<ArgumentException>(() => m.Append(Entry(1, "{}")));
    }

    [Fact(DisplayName = "Memory: the file round-trips byte for byte and keeps lessons")]
    public void Round_trips()
    {
        var m = new PlaytesterMemory("p-03");
        m.Append(Entry(1, "{\"x\":1}"));
        m.AddLesson(1, "too slow on the second room");
        var path = PlaytesterMemory.PathFor(_root, "p-03");
        m.Save(path);
        var again = PlaytesterMemory.Load(path, "p-03");
        Assert.Equal("too slow on the second room", Assert.Single(Assert.Single(again.Entries).Lessons));
        var copy = Path.Combine(_root, "copy.json");
        again.Save(copy);
        Assert.Equal(File.ReadAllText(path), File.ReadAllText(copy));
        Assert.Contains("too slow on the second room", again.ToText(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Memory: a summary edited after the run fails its receipt; a missing one too")]
    public void Verify_catches_a_tampered_or_missing_receipt()
    {
        var m = new PlaytesterMemory("p-04");
        var e1 = m.Append(Entry(1, "{\"result\":\"untimed (wiped)\"}"));
        var e2 = m.Append(Entry(2, "{\"result\":\"untimed (wiped)\"}"));
        Assert.Empty(m.Verify());
        File.WriteAllText(e1.Receipt.Summary, "{\"result\":\"timed +3\"}");
        File.Delete(e2.Receipt.Summary);
        var problems = m.Verify();
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("run 1", StringComparison.Ordinal) && p.Contains("hash", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("run 2", StringComparison.Ordinal) && p.Contains("missing", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "Memory: a playtester id is a plain slug (no path tricks)")]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("Upper")]
    public void Player_id_is_a_slug(string id)
    {
        Assert.Throws<ArgumentException>(() => PlaytesterMemory.PathFor(_root, id));
    }
}
