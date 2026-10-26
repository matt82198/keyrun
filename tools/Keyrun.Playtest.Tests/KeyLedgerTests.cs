using System.IO;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The key rule (Matt 2026-10-20, verbatim: "untimed key stays the same level; timed +1/+2/+3 by tier"): a clear with
/// Slack left inside 60% / 80% / 100% of par raises the key by 3 / 2 / 1; a wipe, a depleted bank, a clear past par or
/// an unfinished run leaves it where it was.
/// </summary>
public sealed class KeyLedgerTests
{
    private const int Par = 14400;

    [Theory(DisplayName = "Key rule: timed tiers by clear time against par")]
    [InlineData(1, 3)]
    [InlineData(8640, 3)]     // exactly 60% of par
    [InlineData(8641, 2)]
    [InlineData(11520, 2)]    // exactly 80%
    [InlineData(11521, 1)]
    [InlineData(14400, 1)]    // exactly par
    public void Timed_tiers(int clearTicks, int tier)
    {
        var o = KeyOutcome.Of(ended: true, cleared: true, depleted: false, clearTicks, Par);
        Assert.True(o.Timed);
        Assert.Equal(tier, o.Tier);
        Assert.Equal($"timed +{tier}", o.Label);
        Assert.Equal(5 + tier, o.NextKey(5));
    }

    [Theory(DisplayName = "Key rule: untimed keeps the key level")]
    [InlineData(true, true, false, 14401, "over par")]
    [InlineData(true, true, true, 3000, "depleted")]
    [InlineData(false, false, false, 28800, "unfinished")]
    public void Untimed_keeps_the_key(bool ended, bool cleared, bool depleted, int clearTicks, string why)
    {
        var o = KeyOutcome.Of(ended, cleared, depleted, clearTicks, Par);
        Assert.False(o.Timed);
        Assert.Equal(0, o.Tier);
        Assert.Equal(why, o.Why);
        Assert.Equal($"untimed ({why})", o.Label);
        Assert.Equal(7, o.NextKey(7));
    }

    /// <summary>One row per sim run-end cause that is untimed and uncleared: the title the end screen shows and the ledger label.</summary>
    public static TheoryData<string, string> UntimedRunEndCauses() => new()
    {
        { RunEndTitle.OutOfTime(2), "untimed (out of time)" },
        { RunEndTitle.Abandoned, "untimed (abandoned)" },
        { RunEndTitle.Wiped(1, 2, 2), "untimed (wiped)" },
    };

    [Theory(DisplayName = "Key rule: an untimed uncleared run is labelled by the sim's run-end title")]
    [MemberData(nameof(UntimedRunEndCauses))]
    public void Untimed_label_is_the_run_end_title_cause(string runEndTitle, string label)
    {
        // KeyOutcome.Of(runEndTitle, cleared, clearTicks, parTicks): the title is RunEndTitle's own text, the sim's decision.
        var o = KeyOutcome.Of(runEndTitle, cleared: false, clearTicks: 3000, parTicks: Par);
        Assert.False(o.Timed);
        Assert.Equal(label, o.Label);
    }

    [Fact(DisplayName = "Key rule: the top key caps a timed raise")]
    public void Top_key_caps()
    {
        var o = KeyOutcome.Of(true, true, false, 100, Par);
        Assert.Equal(20, o.NextKey(19));
    }

    [Fact(DisplayName = "Ledger: a new playtester starts at key 2; timed raises it, untimed leaves it; the file round-trips")]
    public void Ledger_records_and_round_trips()
    {
        var ledger = new KeyLedger();
        Assert.Equal(2, ledger.KeyOf("new-01"));
        var timed = ledger.Record("new-01", 2, 1, KeyOutcome.Of(true, true, false, 10000, Par), "a/summary.json");
        Assert.Equal(4, timed.KeyAfter);
        Assert.Equal(4, ledger.KeyOf("new-01"));
        var untimed = ledger.Record("new-01", 4, 2, KeyOutcome.Of(true, false, true, 3000, Par), "b/summary.json");
        Assert.Equal(4, untimed.KeyAfter);
        Assert.Equal(2, untimed.Run);
        Assert.Equal(2, ledger.KeyOf("vet-01"));

        var path = Path.Combine(Path.GetTempPath(), "keyrun-ledger-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ledger.Save(path);
            var again = KeyLedger.Load(path);
            Assert.Equal(4, again.KeyOf("new-01"));
            Assert.Equal(2, again.RunsOf("new-01").Count);
            Assert.Equal("untimed (wiped)", again.RunsOf("new-01")[1].Result);
            Assert.Equal("b/summary.json", again.RunsOf("new-01")[1].Summary);
            Assert.Equal(File.ReadAllText(path), ReSaved(again, path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ReSaved(KeyLedger ledger, string path)
    {
        var copy = path + ".2";
        try
        {
            ledger.Save(copy);
            return File.ReadAllText(copy);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact(DisplayName = "Ledger: a missing file is an empty ledger")]
    public void Missing_file_is_empty()
    {
        var ledger = KeyLedger.Load(Path.Combine(Path.GetTempPath(), "no-such-ledger-" + System.Guid.NewGuid().ToString("N") + ".json"));
        Assert.Equal(2, ledger.KeyOf("anyone"));
        Assert.Empty(ledger.RunsOf("anyone"));
    }
}
