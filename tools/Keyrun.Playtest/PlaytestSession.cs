using System;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>A ledger run for one playtester.</summary>
/// <param name="PlayerId">The playtester id.</param>
/// <param name="Seed">The seed.</param>
/// <param name="OutDir">Where summary.json and run.log go.</param>
public sealed record SessionOptions(string PlayerId, long Seed, string OutDir)
{
    /// <summary>The plan file (required).</summary>
    public string? PolicyPath { get; init; }

    /// <summary>The key asked for; must equal the ledger's key when given.</summary>
    public int? Key { get; init; }

    /// <summary>The ledger file.</summary>
    public string LedgerPath { get; init; } = Program.DefaultLedger;

    /// <summary>The memory directory.</summary>
    public string MemoryDir { get; init; } = PlaytestSession.DefaultMemoryDir;

    /// <summary>Hard stop, ticks.</summary>
    public int? MaxTicks { get; init; }
}

/// <summary>A finished ledger run.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="KeyBefore">Key played.</param>
/// <param name="KeyAfter">Key after.</param>
/// <param name="Row">Ledger row.</param>
/// <param name="Memory">Memory entry.</param>
/// <param name="SummaryPath">summary.json path.</param>
/// <param name="Play">The played run.</param>
public sealed record SessionResult(KeyOutcome Outcome, int KeyBefore, int KeyAfter, LedgerRun Row, MemoryEntry Memory, string SummaryPath, PlaytestResult Play);

/// <summary>Plays a ledger run.</summary>
public static class PlaytestSession
{
    /// <summary>The default memory directory.</summary>
    public const string DefaultMemoryDir = "game/playtest/players";

    /// <summary>Plays.</summary>
    /// <param name="options">Options.</param>
    /// <param name="registry">Content.</param>
    /// <returns>Result.</returns>
    public static SessionResult Play(SessionOptions options, C.ContentRegistry? registry = null) => throw new NotImplementedException();
}
