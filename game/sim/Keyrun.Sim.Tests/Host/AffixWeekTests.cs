using System;
using System.Globalization;
using System.Text.Json;
using Keyrun.Host;
using Keyrun.Tools.Replay;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Host;

/// <summary>
/// The weekly affix rotation (Matt 2026-10-20, "lean into m+ design heavily while in the dungeon"): the host derives the
/// rotation index from the WoW US weekly reset (Tuesday 15:00 UTC) before the run's start instant, outside the sim,
/// records it in the run options and the input-log header, and a replay reads the recorded index back, never the clock.
/// </summary>
public sealed class AffixWeekTests
{
    private static readonly AffixTable BibleTable = new(new AffixScale(2000, 3000), new AffixScale(2500, 1500), 5, 10);

    private static DateTime U(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    [Theory]
    [InlineData("2026-10-20T15:00:00Z", "2026-10-26T23:59:00Z")] // Tuesday reset and the Monday night after it
    [InlineData("2026-10-20T15:00:00Z", "2026-10-27T14:59:59Z")] // Tuesday reset and one second before the next one
    [InlineData("2026-12-29T15:00:00Z", "2027-01-04T12:00:00Z")] // a reset week that straddles New Year
    public void Every_instant_of_one_reset_week_gets_the_same_index(string a, string b)
    {
        Assert.Equal(AffixWeek.IndexFor(U(a), 4), AffixWeek.IndexFor(U(b), 4));
    }

    [Fact]
    public void The_week_rolls_at_Tuesday_15_00_UTC_not_at_midnight_or_on_Monday()
    {
        var mondayLate = AffixWeek.IndexFor(U("2026-10-19T23:59:00Z"), 4);
        var tuesdayBefore = AffixWeek.IndexFor(U("2026-10-20T14:59:00Z"), 4);
        var tuesdayReset = AffixWeek.IndexFor(U("2026-10-20T15:00:00Z"), 4);
        Assert.Equal(mondayLate, tuesdayBefore);
        Assert.Equal((tuesdayBefore + 1) % 4, tuesdayReset);
    }

    [Theory]
    [InlineData("2026-10-27T14:59:59Z", "2026-10-27T15:00:00Z")]
    [InlineData("2027-01-05T14:59:59Z", "2027-01-05T15:00:00Z")]
    [InlineData("2025-12-30T14:59:59Z", "2025-12-30T15:00:00Z")] // the epoch reset
    public void The_next_reset_gets_the_next_index(string before, string after)
    {
        Assert.Equal((AffixWeek.IndexFor(U(before), 4) + 1) % 4, AffixWeek.IndexFor(U(after), 4));
    }

    [Fact]
    public void The_index_counts_resets_from_2025_12_30_and_wraps_at_the_rotation_length()
    {
        Assert.Equal(0, AffixWeek.IndexFor(U("2025-12-30T15:00:00Z"), 4)); // the epoch reset
        Assert.Equal(1, AffixWeek.IndexFor(U("2026-01-06T15:00:00Z"), 4));
        Assert.Equal(3, AffixWeek.IndexFor(U("2026-01-20T15:00:00Z"), 4));
        Assert.Equal(0, AffixWeek.IndexFor(U("2026-01-27T15:00:00Z"), 4)); // the fourth reset wraps to 0
        Assert.Equal(2, AffixWeek.IndexFor(U("2026-10-20T15:00:00Z"), 4)); // 42 resets on
        Assert.Equal(0, AffixWeek.IndexFor(U("2026-10-20T15:00:00Z"), 6)); // 42 % 6
        Assert.Equal(3, AffixWeek.IndexFor(U("2025-12-30T14:59:59Z"), 4)); // a week before the epoch, never negative
        for (var hour = 0; hour < 24 * 7 * 40; hour += 5)
        {
            Assert.InRange(AffixWeek.IndexFor(U("2025-11-01T00:00:00Z").AddHours(hour), 6), 0, 5);
        }
    }

    [Fact]
    public void The_index_is_computed_in_UTC_and_a_local_time_is_refused()
    {
        Assert.Throws<ArgumentException>(() => AffixWeek.IndexFor(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Local), 4));
        Assert.Throws<ArgumentException>(() => AffixWeek.IndexFor(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Unspecified), 4));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 6)]
    [InlineData(4, 4)]
    [InlineData(5, 10)]
    public void The_rotation_length_covers_both_the_affix_parity_and_every_Clause(int clauseCount, int expected)
    {
        Assert.Equal(expected, AffixWeek.RotationLength(clauseCount));
    }

    [Fact]
    public void Consecutive_weeks_alternate_Fortified_and_Tyrannical_on_keys_5_to_9()
    {
        var reset = U("2026-10-20T15:00:00Z");
        var seen = new string[4];
        for (var w = 0; w < 4; w++)
        {
            var index = AffixWeek.IndexFor(reset.AddDays(7 * w), AffixWeek.RotationLength(4));
            seen[w] = string.Join("+", Affixes.AffixesFor(BibleTable, 7, index).Names);
        }
        Assert.Equal(new[] { "Fortified", "Tyrannical", "Fortified", "Tyrannical" }, seen);
    }

    [Theory]
    [InlineData("2026-10-20T15:00:00Z")]
    [InlineData("2027-03-09T03:00:00Z")]
    [InlineData("2031-07-01T20:00:00Z")]
    public void A_replay_uses_the_recorded_index_never_the_clock(string now)
    {
        Assert.Equal(1, AffixWeek.ForRun(replaying: true, recorded: 1, overrideIndex: null, U(now), 4));
        Assert.Equal(3, AffixWeek.ForRun(replaying: true, recorded: 3, overrideIndex: null, U(now), 4));
        // The recorded header wins over a --rotation override.
        Assert.Equal(3, AffixWeek.ForRun(replaying: true, recorded: 3, overrideIndex: 1, U(now), 4));
        // A log recorded before the header carried the index (every committed fixture) replays at index 0, the host's
        // value when those logs were written, not at this week's.
        Assert.Equal(0, AffixWeek.ForRun(replaying: true, recorded: null, overrideIndex: null, U(now), 4));
    }

    [Fact]
    public void A_live_run_takes_this_weeks_index_unless_rotation_is_overridden()
    {
        var now = U("2026-10-20T16:00:00Z");
        Assert.Equal(AffixWeek.IndexFor(now, 4), AffixWeek.ForRun(replaying: false, recorded: null, overrideIndex: null, now, 4));
        Assert.Equal(AffixWeek.IndexFor(now.AddDays(7), 4), AffixWeek.ForRun(replaying: false, recorded: null, overrideIndex: null, now.AddDays(7), 4));
        // --rotation N pins the index, so a scene test at keys 5-9 never depends on the date.
        Assert.Equal(1, AffixWeek.ForRun(replaying: false, recorded: null, overrideIndex: 1, now, 4));
        Assert.Equal(1, AffixWeek.ForRun(replaying: false, recorded: null, overrideIndex: 1, now.AddDays(7), 4));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    public void The_rotation_flag_takes_a_non_negative_integer(string raw, int expected)
    {
        Assert.Equal(expected, AffixWeek.ParseOverride(raw));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("x")]
    [InlineData("")]
    [InlineData("1.5")]
    public void The_rotation_flag_refuses_anything_else(string raw)
    {
        Assert.Throws<ArgumentException>(() => AffixWeek.ParseOverride(raw));
    }

    [Fact]
    public void The_input_log_header_round_trips_the_index()
    {
        var json = "{\n  \"schema\": \"keyrun.input-log/1\",\n  \"seed\": 1,\n  \"key\": 7,\n" + AffixWeek.HeaderLine(3) + "  \"events\": []\n}\n";
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, AffixWeek.ReadRecorded(doc.RootElement, "t"));
        Assert.Equal("  \"rotationIndex\": 3,\n", AffixWeek.HeaderLine(3));
    }

    [Fact]
    public void A_header_without_the_index_reads_null_and_a_bad_one_is_refused()
    {
        using var none = JsonDocument.Parse("{ \"seed\": 1 }");
        Assert.Null(AffixWeek.ReadRecorded(none.RootElement, "t"));
        foreach (var bad in new[] { "-1", "1.5", "\"2\"", "null", "4294967296" })
        {
            using var doc = JsonDocument.Parse("{ \"rotationIndex\": " + bad + " }");
            Assert.Throws<ArgumentException>(() => AffixWeek.ReadRecorded(doc.RootElement, "t"));
        }
    }

    [Fact]
    public void An_oracle_setup_carries_its_recorded_rotation_index_into_the_run_options()
    {
        var text = System.IO.File.ReadAllText(OracleFixtures.PathOf("hollow-vault-k10.setup.json"));
        Assert.Contains("\"runOptions\":{}", text, StringComparison.Ordinal);
        var setup = RunSetup.Parse(text.Replace("\"runOptions\":{}", "\"runOptions\":{\"rotationIndex\":3}", StringComparison.Ordinal), 7);
        Assert.Equal(3, setup.Options!.RotationIndex);
        Assert.Equal(0, RunSetup.Parse(text, 7).Options!.RotationIndex);
    }
}
