using System;
using System.IO;
using System.Linq;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// A playtester starts without the answers and can fail (Matt 2026-10-20, verbatim: "ensure the bots can fail lol, they
/// should not clear the first dungeon on the first try unless they have played wow lmfao"). A fresh playtester is an id
/// the ledger has never seen, playing an empty plan: its first Hollow Vault key is not timed, the ledger keeps it at the
/// start key (an untimed key stays the same level), and the run lands in the playtester's memory with a receipt whose
/// hashes match the files on disk. Every test drives <see cref="PlaytestSession.Play"/>, the path `playtest run --player`
/// takes.
/// </summary>
public sealed class FreshPlaytesterTests : IDisposable
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "keyrun-playtest-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temp root every file of the test lives under.</summary>
    public FreshPlaytesterTests()
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

    private string Policy(string name, string json)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, json);
        return path;
    }

    private SessionOptions Options(string player, string? policyPath, long seed, string outName) =>
        new(player, seed, Path.Combine(_root, outName))
        {
            PolicyPath = policyPath,
            LedgerPath = Path.Combine(_root, "ledger.json"),
            MemoryDir = Path.Combine(_root, "players"),
        };

    [Fact(DisplayName = "Fresh playtester: an empty plan does not time the first key, and the ledger keeps it at the start key")]
    public void Fresh_playtester_fails_the_first_key_and_keeps_its_level()
    {
        var empty = Policy("empty.json", "{ \"reason\": \"first run, I know nothing yet\" }");
        var first = PlaytestSession.Play(Options("fresh-01", empty, 1, "run-1"), Registry);

        Assert.Equal(Keyrun.Sim.Runs.ScreenState.MinKey, first.KeyBefore);
        Assert.False(first.Outcome.Timed, $"a fresh playtester timed its first key: {first.Outcome.Label}");
        Assert.Equal(first.KeyBefore, first.KeyAfter);
        Assert.Equal(1, first.Row.Run);

        var ledger = KeyLedger.Load(Path.Combine(_root, "ledger.json"));
        Assert.Equal(Keyrun.Sim.Runs.ScreenState.MinKey, ledger.KeyOf("fresh-01"));
        Assert.Single(ledger.RunsOf("fresh-01"));
        Assert.StartsWith("untimed (", ledger.RunsOf("fresh-01")[0].Result, StringComparison.Ordinal);

        // The second try plays the key the ledger holds, the same key.
        var second = PlaytestSession.Play(Options("fresh-01", empty, 2, "run-2") with { MaxTicks = 200 }, Registry);
        Assert.Equal(first.KeyAfter, second.KeyBefore);
        Assert.Equal(2, second.Row.Run);
    }

    [Fact(DisplayName = "Fresh playtester: the run is remembered with a receipt that matches the files and what the run showed")]
    public void Run_lands_in_memory_with_a_receipt()
    {
        var empty = Policy("empty.json", "{}");
        var r = PlaytestSession.Play(Options("fresh-02", empty, 1, "run-1") with { MaxTicks = 200 }, Registry);

        var memory = PlaytesterMemory.Load(PlaytesterMemory.PathFor(Path.Combine(_root, "players"), "fresh-02"), "fresh-02");
        var entry = Assert.Single(memory.Entries);
        Assert.Equal(1, entry.Run);
        Assert.Equal(r.KeyBefore, entry.Key);
        Assert.Equal(r.Outcome.Label, entry.Result);
        Assert.Equal(r.KeyAfter, entry.KeyAfter);
        Assert.Equal(r.SummaryPath, entry.Receipt.Summary);
        Assert.Equal(PlaytesterMemory.Sha256Of(r.SummaryPath), entry.Receipt.SummarySha256);
        Assert.Equal(empty, entry.Receipt.Policy);
        Assert.Equal(PlaytesterMemory.Sha256Of(empty), entry.Receipt.PolicySha256);
        Assert.Contains(entry.Observed, line => line.StartsWith("result: ", StringComparison.Ordinal));
        Assert.Empty(entry.Lessons);
        Assert.Empty(memory.Verify());
    }

    [Fact(DisplayName = "Fresh playtester: a ledger run needs a plan file (no hidden scripted party pushes a key)")]
    public void Ledger_run_requires_a_policy()
    {
        var e = Assert.Throws<ArgumentException>(() => PlaytestSession.Play(Options("fresh-03", null, 1, "run-1"), Registry));
        Assert.Contains("policy", e.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "ledger.json")));
    }

    [Fact(DisplayName = "Fresh playtester: a key other than the one the ledger holds is refused")]
    public void Key_other_than_the_ledger_key_is_refused()
    {
        var empty = Policy("empty.json", "{}");
        var o = Options("fresh-04", empty, 1, "run-1") with { Key = 10 };
        var e = Assert.Throws<ArgumentException>(() => PlaytestSession.Play(o, Registry));
        Assert.Contains("ledger", e.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "ledger.json")));
    }

    [Fact(DisplayName = "Fresh playtester: a returning playtester plays the key the ledger holds, and an untimed run leaves it there")]
    public void Ledger_key_moves_by_the_outcome_of_the_key_played()
    {
        // The session records through KeyLedger.Record; seed a known history and check the next run plays from it.
        var ledgerPath = Path.Combine(_root, "ledger.json");
        var ledger = new KeyLedger();
        ledger.Record("vet-01", 2, 7, KeyOutcome.Of(true, true, false, 1000, 14400), "old/summary.json");
        ledger.Save(ledgerPath);
        Assert.Equal(5, KeyLedger.Load(ledgerPath).KeyOf("vet-01"));

        var empty = Policy("empty.json", "{}");
        var r = PlaytestSession.Play(Options("vet-01", empty, 1, "run-2") with { MaxTicks = 200 }, Registry);
        Assert.Equal(5, r.KeyBefore);
        Assert.Equal(2, r.Row.Run);
        Assert.Equal("untimed (unfinished)", r.Outcome.Label);
        Assert.Equal(5, KeyLedger.Load(ledgerPath).KeyOf("vet-01"));
        Assert.Equal(new[] { 5, 5 }, KeyLedger.Load(ledgerPath).RunsOf("vet-01").Select(x => x.KeyAfter).ToArray());
    }
}
