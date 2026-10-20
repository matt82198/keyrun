using System;
using System.IO;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>A ledger run for one playtester (`playtest run --player ID --policy FILE`).</summary>
/// <param name="PlayerId">The playtester id (a lowercase slug).</param>
/// <param name="Seed">The run seed.</param>
/// <param name="OutDir">Where summary.json and run.log are written.</param>
public sealed record SessionOptions(string PlayerId, long Seed, string OutDir)
{
    /// <summary>The playtester's plan file. Required: a ledger key only moves on a run the playtester planned.</summary>
    public string? PolicyPath { get; init; }

    /// <summary>The key asked for. A playtester plays the key its ledger holds, so any other value is refused.</summary>
    public int? Key { get; init; }

    /// <summary>The key ledger file.</summary>
    public string LedgerPath { get; init; } = Program.DefaultLedger;

    /// <summary>The directory holding one memory file per playtester.</summary>
    public string MemoryDir { get; init; } = PlaytestSession.DefaultMemoryDir;

    /// <summary>Hard stop, ticks (default: twice par); a run still going is "unfinished".</summary>
    public int? MaxTicks { get; init; }
}

/// <summary>A finished ledger run.</summary>
/// <param name="Outcome">How the key went.</param>
/// <param name="KeyBefore">The key played (the ledger's).</param>
/// <param name="KeyAfter">The key the ledger holds now.</param>
/// <param name="Row">The ledger row recorded.</param>
/// <param name="Memory">The memory entry appended.</param>
/// <param name="SummaryPath">The summary.json written (the receipt).</param>
/// <param name="Play">The played run.</param>
public sealed record SessionResult(KeyOutcome Outcome, int KeyBefore, int KeyAfter, LedgerRun Row, MemoryEntry Memory, string SummaryPath, PlaytestResult Play);

/// <summary>
/// One playtester run end to end: read the ledger key, play it under the playtester's plan, write summary.json and
/// run.log, record the ledger (timed: +tier; untimed: the same key) and append the run to the playtester's memory with a
/// receipt. Nothing is written when the options are refused (no plan, or a key other than the ledger's).
/// </summary>
public static class PlaytestSession
{
    /// <summary>The default memory directory (repo-root relative): one ID.json per playtester.</summary>
    public const string DefaultMemoryDir = "game/playtest/players";

    /// <summary>Plays the run.</summary>
    /// <param name="options">Who plays, with which plan and seed, and where files go.</param>
    /// <param name="registry">The content (default: the embedded content).</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">No plan file, a bad id, or a key other than the ledger's.</exception>
    /// <exception cref="PolicyException">The plan file is not a valid plan.</exception>
    public static SessionResult Play(SessionOptions options, C.ContentRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        PlaytesterMemory.RequireSlug(options.PlayerId);
        if (string.IsNullOrEmpty(options.PolicyPath))
        {
            throw new ArgumentException("a ledger run needs a policy file (--policy FILE); an empty plan {} is a playtester who knows nothing yet", nameof(options));
        }
        var content = registry ?? C.ContentLoader.Load();
        var ledger = KeyLedger.Load(options.LedgerPath);
        var key = ledger.KeyOf(options.PlayerId);
        if (options.Key is { } asked && asked != key)
        {
            throw new ArgumentException(FormattableString.Invariant(
                $"playtester {options.PlayerId} holds key {key} in the ledger; it plays that key, not {asked}"), nameof(options));
        }
        var memoryPath = PlaytesterMemory.PathFor(options.MemoryDir, options.PlayerId);
        var memory = PlaytesterMemory.Load(memoryPath, options.PlayerId);
        if (memory.Entries.Count != ledger.RunsOf(options.PlayerId).Count)
        {
            throw new InvalidDataException(FormattableString.Invariant(
                $"playtester {options.PlayerId}: memory has {memory.Entries.Count} run(s) but the ledger has {ledger.RunsOf(options.PlayerId).Count}"));
        }
        var policy = PolicyReader.Load(options.PolicyPath, content);

        var playOptions = new PlaytestOptions(key, options.Seed, policy) { MaxTicks = options.MaxTicks };
        var result = PlaytestRunner.Play(playOptions, content);
        Directory.CreateDirectory(options.OutDir);
        var logPath = Path.Combine(options.OutDir, "run.log").Replace('\\', '/');
        var summaryPath = Path.Combine(options.OutDir, "summary.json").Replace('\\', '/');
        File.WriteAllText(logPath, result.Log);

        var outcome = Summary.OutcomeOf(result);
        var row = ledger.Record(options.PlayerId, key, options.Seed, outcome, summaryPath);
        File.WriteAllText(summaryPath, Summary.ToJson(result, playOptions, new SummaryHeader(options.PlayerId, options.PolicyPath, logPath, key, row.KeyAfter, row.Run)));

        var receipt = new MemoryReceipt(summaryPath, PlaytesterMemory.Sha256Of(summaryPath), options.PolicyPath, PlaytesterMemory.Sha256Of(options.PolicyPath));
        var entry = memory.Append(new MemoryEntry(row.Run, key, options.Seed, outcome.Label, row.KeyAfter, receipt, PlaytesterMemory.Observe(result), Array.Empty<string>()));
        ledger.Save(options.LedgerPath);
        memory.Save(memoryPath);
        return new SessionResult(outcome, key, row.KeyAfter, row, entry, summaryPath, result);
    }
}
