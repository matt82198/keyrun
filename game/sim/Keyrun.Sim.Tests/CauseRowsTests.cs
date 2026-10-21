using System.Collections.Generic;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The run-end "What got you" list (BIBLE s10 "Death is not a scolding", lane WHAT-GOT-YOU): one row per enemy kind.
/// Two actors of one class share a display name, so per-actor rows read as a duplicate ("Choir Acolyte" twice, seen on
/// main 3e072d6); <see cref="CauseRows.MergeByName"/> folds rows that share a name.
/// </summary>
public sealed class CauseRowsTests
{
    [Fact]
    public void Two_actors_of_one_kind_merge_into_one_row_summing_damage_and_hits()
    {
        var rows = CauseRows.MergeByName(new[]
        {
            new CauseRow(1, 101, "Choir Acolyte", 1183, 22, false),
            new CauseRow(1, 102, "Choir Acolyte", 532, 10, false),
        });

        var row = Assert.Single(rows);
        Assert.Equal("Choir Acolyte", row.SourceName);
        Assert.Equal(1715, row.Damage);
        Assert.Equal(32, row.Hits);
        Assert.False(row.KillingBlow);
        Assert.Equal(101, row.SourceId);
    }

    [Fact]
    public void Different_names_stay_separate_rows_sorted_by_damage()
    {
        var rows = CauseRows.MergeByName(new[]
        {
            new CauseRow(1, 100, "Choir Blade", 280, 4, false),
            new CauseRow(1, 101, "Choir Acolyte", 400, 8, false),
            new CauseRow(1, 102, "Choir Acolyte", 300, 6, false),
            new CauseRow(1, 103, "Hollow Warden", 900, 3, false),
        });

        Assert.Equal(new[] { "Hollow Warden", "Choir Acolyte", "Choir Blade" }, Names(rows));
        Assert.Equal(new long[] { 900, 700, 280 }, Damages(rows));
        Assert.Equal(new[] { 3, 14, 4 }, Hits(rows));
    }

    [Fact]
    public void A_killing_blow_from_either_actor_marks_the_merged_row_and_leads_the_list()
    {
        var rows = CauseRows.MergeByName(new[]
        {
            new CauseRow(1, 103, "Hollow Warden", 900, 3, false),
            new CauseRow(1, 101, "Choir Acolyte", 100, 2, false),
            new CauseRow(1, 102, "Choir Acolyte", 50, 1, true),
        });

        Assert.Equal(new[] { "Choir Acolyte", "Hollow Warden" }, Names(rows));
        Assert.True(rows[0].KillingBlow);
        Assert.Equal(150, rows[0].Damage);
        Assert.False(rows[1].KillingBlow);
    }

    [Fact]
    public void The_damage_number_is_the_sum_it_was_given_never_rescaled()
    {
        // The panel feeds each damage event's healthDamage (Damage.cs ResolveModified: after absorb, capped at health)
        // and labels it "N damage in K hits" under "What got you": the health the seat lost. The fold sums exactly
        // what it is given, so the number on the row is the health lost to that kind.
        var rows = CauseRows.MergeByName(new[]
        {
            new CauseRow(2, 7, "Choir Blade", 0, 1, false),
            new CauseRow(2, 8, "Choir Blade", 37, 2, false),
        });

        var row = Assert.Single(rows);
        Assert.Equal(37, row.Damage);
        Assert.Equal(3, row.Hits);
        Assert.Equal(2, row.Seat);
    }

    [Fact]
    public void Equal_damage_ties_break_by_the_lowest_source_id_and_seats_never_mix()
    {
        var rows = CauseRows.MergeByName(new[]
        {
            new CauseRow(1, 120, "B", 50, 1, false),
            new CauseRow(1, 110, "A", 50, 1, false),
            new CauseRow(3, 105, "A", 999, 9, false),
        });

        Assert.Equal(new[] { "A", "A", "B" }, Names(rows));
        Assert.Equal(new[] { 3, 1, 1 }, Seats(rows));
    }

    [Fact]
    public void A_seat_is_never_listed_as_a_cause_of_its_own_damage()
    {
        // Finding f-1326b59a: the run-end panel printed `HUD runend cause seat=1 source=1 name="Warden"`. Closing a
        // Cinderfield burns the closer (STATE.md D34; Combos.cs BurnCloser emits one damage event whose source and
        // target are both the closer, and the death event it may cause carries the same source), so the private run
        // log holds a (seat 1, source 1) row. "What got you" names what hurt the seat; the seat itself is not one.
        var rows = CauseRows.MergeByName(new[]
        {
            new CauseRow(1, 1, "Warden", 300, 2, true),
            new CauseRow(1, 100, "Choir Blade", 280, 4, false),
            new CauseRow(3, 3, "Cinder", 90, 1, false),
            new CauseRow(3, 1, "Warden", 40, 1, false),
        });

        Assert.DoesNotContain(rows, r => r.SourceId == r.Seat);
        Assert.Equal(new[] { "Choir Blade", "Warden" }, Names(rows));
        Assert.Equal(new[] { 1, 3 }, Seats(rows));
        Assert.False(rows[0].KillingBlow);
        Assert.Equal(280, rows[0].Damage);
        Assert.Equal(40, rows[1].Damage);
    }

    [Fact]
    public void A_seat_whose_only_damage_was_its_own_has_no_cause_rows()
    {
        var rows = CauseRows.MergeByName(new[] { new CauseRow(2, 2, "Chanter", 50, 1, false) });

        Assert.Empty(rows);
    }

    private static string[] Names(IReadOnlyList<CauseRow> rows) => Map(rows, r => r.SourceName);

    private static long[] Damages(IReadOnlyList<CauseRow> rows) => Map(rows, r => r.Damage);

    private static int[] Hits(IReadOnlyList<CauseRow> rows) => Map(rows, r => r.Hits);

    private static int[] Seats(IReadOnlyList<CauseRow> rows) => Map(rows, r => r.Seat);

    private static T[] Map<T>(IReadOnlyList<CauseRow> rows, System.Func<CauseRow, T> f)
    {
        var a = new T[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            a[i] = f(rows[i]);
        }
        return a;
    }
}
