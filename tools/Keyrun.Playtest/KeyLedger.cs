using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Playtest;

/// <summary>How one run went for the key (Matt 2026-10-20: untimed keeps the key, timed raises it by the tier).</summary>
/// <param name="Timed">True when the dungeon was cleared with Slack left at or before par.</param>
/// <param name="Tier">+1, +2 or +3 for a timed run (clear time within 100%, 80% or 60% of par), 0 when untimed.</param>
/// <param name="Why">"timed", or why not: "wiped", "depleted", "over par" or "unfinished".</param>
public sealed record KeyOutcome(bool Timed, int Tier, string Why)
{
    /// <summary>Clear at or under this percent of par: +3.</summary>
    public const int ThreeChestPct = 60;

    /// <summary>Clear at or under this percent of par: +2.</summary>
    public const int TwoChestPct = 80;

    /// <summary>The result label the summary shows: "timed +2" or "untimed (wiped)".</summary>
    public string Label => Timed ? FormattableString.Invariant($"timed +{Tier}") : $"untimed ({Why})";

    /// <summary>
    /// The outcome of a finished run. <paramref name="cleared"/> false with the run ended is a wipe; a cleared run
    /// that ran out of Slack is depleted; a cleared run past par is over par; else timed with the tier by clear time.
    /// </summary>
    /// <param name="ended">True when the run reached run_end.</param>
    /// <param name="cleared">True when every pull including the boss was cleared.</param>
    /// <param name="depleted">True when the Slack bank ran out.</param>
    /// <param name="clearTicks">The run clock at the end (Timer.ClockTicks: elapsed plus death and wipe time).</param>
    /// <param name="parTicks">The dungeon's par.</param>
    /// <returns>The outcome.</returns>
    public static KeyOutcome Of(bool ended, bool cleared, bool depleted, long clearTicks, int parTicks)
    {
        if (parTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parTicks), parTicks, "par must be positive");
        }
        if (!ended)
        {
            return new KeyOutcome(false, 0, "unfinished");
        }
        if (!cleared)
        {
            return new KeyOutcome(false, 0, "wiped");
        }
        if (depleted)
        {
            return new KeyOutcome(false, 0, "depleted");
        }
        if (clearTicks > parTicks)
        {
            return new KeyOutcome(false, 0, "over par");
        }
        // The sim's own upgrade tier on the run clock (Timer.UpgradeTier: +3 within 60% of par, +2 within 80%, else +1).
        var tier = Timer.UpgradeTier(RunResult.Timed, clearTicks, parTicks);
        return new KeyOutcome(true, tier, "timed");
    }

    /// <summary>The sim's untimed run-end title for a layout run the timer cap ended (<see cref="RunEndTitle.OutOfTime"/>).</summary>
    private static readonly Regex OutOfTimeTitle = TitlePattern(RunEndTitle.OutOfTime(0));

    /// <summary>The sim's lasting-wipe run-end title (<see cref="RunEndTitle.Wiped"/>).</summary>
    private static readonly Regex WipedTitle = TitlePattern(RunEndTitle.Wiped(0, 0, 0));

    /// <summary>The sim's cleared-but-depleted run-end title (<see cref="RunEndTitle.ClearedUntimed"/>).</summary>
    private static readonly Regex ClearedUntimedTitle = TitlePattern(RunEndTitle.ClearedUntimed(0));

    /// <summary>
    /// A pattern for a factory's output: its own text with every run of digits standing for any count, so the match
    /// tracks the factory rather than a copy of its wording.
    /// </summary>
    /// <param name="sample">One factory output, built with sample arguments.</param>
    /// <returns>An anchored, culture-invariant pattern.</returns>
    private static Regex TitlePattern(string sample) =>
        new("^" + Regex.Replace(Regex.Escape(sample), @"\d+", @"\d+") + "$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The outcome of a finished run from the sim's own run-end title (<see cref="RunEndTitle.ForRun"/>), the sim's
    /// decision and the source of the label. An untimed title maps to its cause: out of time, abandoned or wiped. A
    /// cleared title takes its timed tier from the 5-argument <see cref="Of(bool, bool, bool, long, int)"/>, which reads the
    /// sim's <see cref="Timer.UpgradeTier"/> on the run clock the caller passes (Summary passes <see cref="Timer.ClockTicks"/>
    /// at the sim's finish tick). A null title (the run has not ended) is unfinished. The tier is never recomputed from
    /// elapsed time or par here, and never inferred from layout.
    /// </summary>
    /// <param name="runEndTitle">The sim's run-end title, or null while the run is live.</param>
    /// <param name="cleared">True when every pull including the boss was cleared.</param>
    /// <param name="clearTicks">The run clock at the end (Timer.ClockTicks: elapsed plus death and wipe time).</param>
    /// <param name="parTicks">The dungeon's par.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException">The title is not one the sim produces, or disagrees with <paramref name="cleared"/>.</exception>
    public static KeyOutcome Of(string? runEndTitle, bool cleared, long clearTicks, int parTicks)
    {
        if (parTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parTicks), parTicks, "par must be positive");
        }
        if (runEndTitle is null)
        {
            return new KeyOutcome(false, 0, "unfinished");
        }
        if (runEndTitle == RunEndTitle.Abandoned)
        {
            return new KeyOutcome(false, 0, "abandoned");
        }
        if (OutOfTimeTitle.IsMatch(runEndTitle))
        {
            return new KeyOutcome(false, 0, "out of time");
        }
        if (WipedTitle.IsMatch(runEndTitle))
        {
            return new KeyOutcome(false, 0, "wiped");
        }
        if (runEndTitle == RunEndTitle.WithinPar || runEndTitle == RunEndTitle.OverPar)
        {
            RequireCleared(runEndTitle, cleared);
            return Of(ended: true, cleared: true, depleted: false, clearTicks, parTicks);
        }
        if (ClearedUntimedTitle.IsMatch(runEndTitle))
        {
            RequireCleared(runEndTitle, cleared);
            return Of(ended: true, cleared: true, depleted: true, clearTicks, parTicks);
        }
        throw new ArgumentException($"unrecognised run-end title \"{runEndTitle}\"", nameof(runEndTitle));
    }

    /// <summary>Fails closed when a cleared title is given for a run that was not cleared.</summary>
    /// <param name="title">The title.</param>
    /// <param name="cleared">The cleared flag.</param>
    private static void RequireCleared(string title, bool cleared)
    {
        if (!cleared)
        {
            throw new ArgumentException($"run-end title \"{title}\" is for a cleared run, but the run was not cleared", nameof(title));
        }
    }

    /// <summary>The key after a run at <paramref name="key"/>: +tier when timed (capped at the top key), else the same key.</summary>
    /// <param name="key">The key played.</param>
    /// <returns>The next key.</returns>
    public int NextKey(int key) => Timed ? Math.Min(ScreenState.MaxKey, key + Tier) : key;
}

/// <summary>One ledger row: a run a playtester finished.</summary>
/// <param name="Run">1-based run number for that playtester.</param>
/// <param name="Key">The key played.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Result">The outcome label ("timed +1", "untimed (wiped)").</param>
/// <param name="KeyAfter">The key the ledger holds after the run.</param>
/// <param name="Summary">The summary.json path (the receipt), as given on the command line.</param>
public sealed record LedgerRun(int Run, int Key, long Seed, string Result, int KeyAfter, string Summary);

/// <summary>
/// The key ledger (game/playtest/ledger.json): the current key per playtester id and every run it played. A new
/// playtester starts at <see cref="ScreenState.MinKey"/>. Written with sorted ids so the file diffs cleanly.
/// </summary>
public sealed class KeyLedger
{
    private readonly SortedDictionary<string, (int Key, List<LedgerRun> Runs)> _players = new(StringComparer.Ordinal);

    /// <summary>The key <paramref name="playerId"/> holds (the start key for a new id).</summary>
    /// <param name="playerId">Playtester id.</param>
    /// <returns>The key.</returns>
    public int KeyOf(string playerId) => _players.TryGetValue(playerId, out var p) ? p.Key : ScreenState.MinKey;

    /// <summary>The runs <paramref name="playerId"/> has recorded, oldest first.</summary>
    /// <param name="playerId">Playtester id.</param>
    /// <returns>The runs.</returns>
    public IReadOnlyList<LedgerRun> RunsOf(string playerId) =>
        _players.TryGetValue(playerId, out var p) ? p.Runs : Array.Empty<LedgerRun>();

    /// <summary>Records a run and moves the player's key by the outcome (timed: key + tier; untimed: the key played).</summary>
    /// <param name="playerId">Playtester id.</param>
    /// <param name="key">The key played.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="summaryPath">The summary.json path.</param>
    /// <returns>The recorded row.</returns>
    public LedgerRun Record(string playerId, int key, long seed, KeyOutcome outcome, string summaryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(playerId);
        ArgumentNullException.ThrowIfNull(outcome);
        if (!_players.TryGetValue(playerId, out var p))
        {
            p = (ScreenState.MinKey, new List<LedgerRun>());
        }
        var row = new LedgerRun(p.Runs.Count + 1, key, seed, outcome.Label, outcome.NextKey(key), summaryPath);
        p.Runs.Add(row);
        _players[playerId] = (row.KeyAfter, p.Runs);
        return row;
    }

    /// <summary>Loads <paramref name="path"/>; a missing file is an empty ledger.</summary>
    /// <param name="path">The ledger file.</param>
    /// <returns>The ledger.</returns>
    public static KeyLedger Load(string path)
    {
        var ledger = new KeyLedger();
        if (!File.Exists(path))
        {
            return ledger;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"ledger {path}: missing \"players\" object");
        }
        foreach (var player in players.EnumerateObject())
        {
            var key = player.Value.GetProperty("key").GetInt32();
            var runs = player.Value.TryGetProperty("runs", out var r)
                ? r.EnumerateArray().Select(x => new LedgerRun(
                    x.GetProperty("run").GetInt32(),
                    x.GetProperty("key").GetInt32(),
                    x.GetProperty("seed").GetInt64(),
                    x.GetProperty("result").GetString() ?? string.Empty,
                    x.GetProperty("keyAfter").GetInt32(),
                    x.GetProperty("summary").GetString() ?? string.Empty)).ToList()
                : new List<LedgerRun>();
            ledger._players[player.Name] = (key, runs);
        }
        return ledger;
    }

    /// <summary>Writes the ledger as indented JSON (players sorted by id, LF line ends).</summary>
    /// <param name="path">The ledger file.</param>
    public void Save(string path)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartObject("players");
            foreach (var (id, p) in _players)
            {
                w.WriteStartObject(id);
                w.WriteNumber("key", p.Key);
                w.WriteStartArray("runs");
                foreach (var run in p.Runs)
                {
                    w.WriteStartObject();
                    w.WriteNumber("run", run.Run);
                    w.WriteNumber("key", run.Key);
                    w.WriteNumber("seed", run.Seed);
                    w.WriteString("result", run.Result);
                    w.WriteNumber("keyAfter", run.KeyAfter);
                    w.WriteString("summary", run.Summary);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }
}
