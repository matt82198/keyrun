using System;
using System.Collections.Generic;

namespace Keyrun.Playtest;

/// <summary>The files a remembered run can be checked against: the summary and the plan, each with its SHA-256.</summary>
/// <param name="Summary">The summary.json path as written.</param>
/// <param name="SummarySha256">Lowercase hex SHA-256 of the summary file.</param>
/// <param name="Policy">The policy file path, or null.</param>
/// <param name="PolicySha256">Lowercase hex SHA-256 of the policy file, or null.</param>
public sealed record MemoryReceipt(string Summary, string SummarySha256, string? Policy, string? PolicySha256);

/// <summary>One remembered run.</summary>
/// <param name="Run">1-based run number (the ledger's).</param>
/// <param name="Key">The key played.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Result">The outcome label.</param>
/// <param name="KeyAfter">The key after the run.</param>
/// <param name="Receipt">The receipt.</param>
/// <param name="Observed">What the run showed.</param>
/// <param name="Lessons">What the playtester wrote down.</param>
public sealed record MemoryEntry(int Run, int Key, long Seed, string Result, int KeyAfter, MemoryReceipt Receipt, IReadOnlyList<string> Observed, IReadOnlyList<string> Lessons);

/// <summary>A playtester's memory file.</summary>
public sealed class PlaytesterMemory
{
    /// <summary>Creates an empty memory.</summary>
    /// <param name="playerId">The playtester id.</param>
    public PlaytesterMemory(string playerId)
    {
        PlayerId = playerId;
    }

    /// <summary>The playtester id.</summary>
    public string PlayerId { get; }

    /// <summary>The runs, oldest first.</summary>
    public IReadOnlyList<MemoryEntry> Entries => throw new NotImplementedException();

    /// <summary>The memory file path.</summary>
    /// <param name="memoryDir">Directory.</param>
    /// <param name="playerId">Id.</param>
    /// <returns>Path.</returns>
    public static string PathFor(string memoryDir, string playerId) => throw new NotImplementedException();

    /// <summary>SHA-256 of a file.</summary>
    /// <param name="path">File.</param>
    /// <returns>Hex.</returns>
    public static string Sha256Of(string path) => throw new NotImplementedException();

    /// <summary>Loads a memory file.</summary>
    /// <param name="path">File.</param>
    /// <param name="playerId">Id.</param>
    /// <returns>Memory.</returns>
    public static PlaytesterMemory Load(string path, string playerId) => throw new NotImplementedException();

    /// <summary>Appends a run.</summary>
    /// <param name="entry">Entry.</param>
    /// <returns>The entry.</returns>
    public MemoryEntry Append(MemoryEntry entry) => throw new NotImplementedException();

    /// <summary>Adds a lesson.</summary>
    /// <param name="run">Run, or null for the last.</param>
    /// <param name="lesson">Text.</param>
    /// <returns>The updated entry.</returns>
    public MemoryEntry AddLesson(int? run, string lesson) => throw new NotImplementedException();

    /// <summary>Checks every receipt.</summary>
    /// <returns>Problems.</returns>
    public IReadOnlyList<string> Verify() => throw new NotImplementedException();

    /// <summary>Readable text.</summary>
    /// <returns>Text.</returns>
    public string ToText() => throw new NotImplementedException();

    /// <summary>Saves.</summary>
    /// <param name="path">File.</param>
    public void Save(string path) => throw new NotImplementedException();
}
