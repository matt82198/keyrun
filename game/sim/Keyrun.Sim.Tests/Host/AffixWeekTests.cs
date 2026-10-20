using System;
using System.Text.Json;
using Keyrun.Host;
using Keyrun.Tools.Replay;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Host;

/// <summary>
/// The weekly affix rotation (Matt 2026-10-20, "lean into m+ design heavily while in the dungeon"): the host derives the
/// rotation index from the ISO week of the wall-clock date at run start, outside the sim, records it in the run options
/// and the input-log header, and a replay reads the recorded index back, never today's date.
/// </summary>
public sealed class AffixWeekTests
{
    private static readonly AffixTable BibleTable = new(new AffixScale(2000, 3000), new AffixScale(2500, 1500), 5, 10);

    [Theory]
    [InlineData("2026-10-19", "2026-10-20")] // Monday and Tuesday of ISO 2026-W43
    [InlineData("2026-10-19", "2026-10-25")] // Monday and Sunday of the same week
    [InlineData("2026-12-28", "2027-01-03")] // ISO 2026-W53 straddles the calendar year
    public void Every_day_of_one_ISO_week_gets_the_same_index(string a, string b)
    {
        Assert.Equal(AffixWeek.IndexFor(DateOnly.Parse(a), 4), AffixWeek.IndexFor(DateOnly.Parse(b), 4));
    }

    [Theory]
    [InlineData("2026-10-25", "2026-10-26")] // Sunday W43 -> Monday W44
    [InlineData("2027-01-03", "2027-01-04")] // Sunday 2026-W53 -> Monday 2027-W01
    [InlineData("2025-12-28", "2025-12-29")] // the epoch boundary: 2025-W52 -> 2026-W01
    public void The_next_ISO_week_gets_the_next_index(string sunday, string monday)
    {
        var before = AffixWeek.IndexFor(DateOnly.Parse(sunday), 4);
        Assert.Equal((before + 1) % 4, AffixWeek.IndexFor(DateOnly.Parse(monday), 4));
    }

    [Fact]
    public void The_index_counts_weeks_from_2026_W01_and_wraps_at_the_rotation_length()
    {
        Assert.Equal(0, AffixWeek.IndexFor(new DateOnly(2025, 12, 29), 4)); // 2026-W01 Monday
        Assert.Equal(1, AffixWeek.IndexFor(new DateOnly(2026, 1, 5), 4));
        Assert.Equal(3, AffixWeek.IndexFor(new DateOnly(2026, 1, 19), 4));
        Assert.Equal(0, AffixWeek.IndexFor(new DateOnly(2026, 1, 26), 4)); // week 4 wraps to 0
        Assert.Equal(2, AffixWeek.IndexFor(new DateOnly(2026, 10, 20), 4)); // 2026-W43 = 42 weeks on
        Assert.Equal(0, AffixWeek.IndexFor(new DateOnly(2026, 10, 20), 6)); // 42 % 6
        Assert.Equal(3, AffixWeek.IndexFor(new DateOnly(2025, 12, 28), 4)); // a week before the epoch, never negative
        for (var day = 0; day < 7 * 40; day++)
        {
            var i = AffixWeek.IndexFor(new DateOnly(2026, 1, 1).AddDays(day), 6);
            Assert.InRange(i, 0, 5);
        }
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
        var monday = new DateOnly(2026, 10, 19);
        var seen = new string[4];
        for (var w = 0; w < 4; w++)
        {
            var index = AffixWeek.IndexFor(monday.AddDays(7 * w), AffixWeek.RotationLength(4));
            seen[w] = string.Join("+", Affixes.AffixesFor(BibleTable, 7, index).Names);
        }
        Assert.Equal(new[] { "Fortified", "Tyrannical", "Fortified", "Tyrannical" }, seen);
    }

    [Theory]
    [InlineData("2026-10-20")]
    [InlineData("2027-03-09")]
    [InlineData("2031-07-01")]
    public void A_replay_uses_the_recorded_index_never_todays_date(string today)
    {
        Assert.Equal(1, AffixWeek.ForRun(replaying: true, recorded: 1, DateOnly.Parse(today), 4));
        Assert.Equal(3, AffixWeek.ForRun(replaying: true, recorded: 3, DateOnly.Parse(today), 4));
        // A log recorded before the header carried the index (every committed fixture) replays at index 0, the host's
        // value when those logs were written, not at this week's.
        Assert.Equal(0, AffixWeek.ForRun(replaying: true, recorded: null, DateOnly.Parse(today), 4));
    }

    [Fact]
    public void A_live_run_takes_this_weeks_index()
    {
        var today = new DateOnly(2026, 10, 20);
        Assert.Equal(AffixWeek.IndexFor(today, 4), AffixWeek.ForRun(replaying: false, recorded: null, today, 4));
        Assert.Equal(AffixWeek.IndexFor(today.AddDays(7), 4), AffixWeek.ForRun(replaying: false, recorded: null, today.AddDays(7), 4));
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
