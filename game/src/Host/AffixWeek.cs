using System;
using System.Globalization;
using System.Text.Json;

namespace Keyrun.Host;

/// <summary>
/// The weekly affix rotation (BIBLE s8 "the rotation index comes with the key from the host (the ISO week)"; Matt
/// 2026-10-20, "lean into m+ design heavily while in the dungeon"). Like WoW's Mythic+ weeks, every run started in the
/// same ISO week gets the same rotation index, so the same Fortified/Tyrannical parity and the same key-set Clause, and
/// the index moves on by one each week. It lives in the host, never the sim: the sim stays pure and takes the index as
/// an input (<see cref="Keyrun.Sim.Runs.RunOptions.RotationIndex"/>). A run's index is recorded in its input-log header
/// (<see cref="HeaderField"/>), and a replay builds its run from that recorded index, never from the replay's own date.
/// Godot-free so Keyrun.Sim.Tests can drive it.
/// </summary>
public static class AffixWeek
{
    /// <summary>The input-log header field that records a run's index.</summary>
    public const string HeaderField = "rotationIndex";

    /// <summary>Monday of ISO 2026-W01 (2025-12-29), the week whose index is 0.</summary>
    public static readonly DateOnly EpochMonday = DateOnly.FromDateTime(ISOWeek.ToDateTime(2026, 1, DayOfWeek.Monday));

    /// <summary>
    /// The rotation's length in weeks: the least common multiple of the affix parity (2, <see
    /// cref="Keyrun.Sim.Encounter.Affixes.AffixesFor"/>) and the Clause pool (<see
    /// cref="Keyrun.Sim.Runs.ClauseRotation.ForKey"/>), so wrapping the index there changes neither the affix nor the
    /// Clause any week carries; it only keeps the recorded number small.
    /// </summary>
    /// <param name="clauseCount">How many Clauses content defines (0 or 1 means the parity alone).</param>
    /// <returns>The length, at least 2.</returns>
    public static int RotationLength(int clauseCount)
    {
        var n = Math.Max(clauseCount, 1);
        var a = n;
        var b = 2;
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return n / a * 2;
    }

    /// <summary>
    /// The index for a run started on <paramref name="date"/>: whole ISO weeks since <see cref="EpochMonday"/>, wrapped
    /// into [0, <paramref name="rotationLength"/>). Every day of one ISO week (Monday to Sunday, including a week that
    /// straddles New Year) gets the same index; the next week gets the next one.
    /// </summary>
    /// <param name="date">The wall-clock date at run start.</param>
    /// <param name="rotationLength">From <see cref="RotationLength"/>.</param>
    /// <returns>The index.</returns>
    public static int IndexFor(DateOnly date, int rotationLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rotationLength, 1);
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(ISOWeek.GetYear(dt), ISOWeek.GetWeekOfYear(dt), DayOfWeek.Monday));
        var weeks = (monday.DayNumber - EpochMonday.DayNumber) / 7; // exact: both are Mondays
        return ((weeks % rotationLength) + rotationLength) % rotationLength;
    }

    /// <summary>
    /// The index a run is built with. A live run takes <paramref name="today"/>'s week. A replay takes the index its log
    /// recorded and never reads <paramref name="today"/>; a log written before the header carried one replays at 0, the
    /// index the host used when those logs were recorded.
    /// </summary>
    /// <param name="replaying">True when the launch replays an input log.</param>
    /// <param name="recorded">The log's recorded index, or null when it has none.</param>
    /// <param name="today">The wall-clock date at run start.</param>
    /// <param name="rotationLength">From <see cref="RotationLength"/>.</param>
    /// <returns>The index.</returns>
    public static int ForRun(bool replaying, int? recorded, DateOnly today, int rotationLength) =>
        replaying ? recorded ?? 0 : IndexFor(today, rotationLength);

    /// <summary>The header line the input log writes (two-space indent, trailing comma, LF).</summary>
    /// <param name="index">The run's index.</param>
    /// <returns>The line.</returns>
    public static string HeaderLine(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"  \"{HeaderField}\": {index},\n");

    /// <summary>
    /// The index an input log's header recorded, or null when the header has none. Fail-closed: a present value that
    /// is not a non-negative 32-bit integer is refused.
    /// </summary>
    /// <param name="root">The log's root object.</param>
    /// <param name="source">Where it came from (error messages).</param>
    /// <returns>The index, or null.</returns>
    /// <exception cref="ArgumentException">The field is present but not a non-negative integer.</exception>
    public static int? ReadRecorded(JsonElement root, string source)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(HeaderField, out var el))
        {
            return null;
        }
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var index) || index < 0)
        {
            throw new ArgumentException($"{source}: \"{HeaderField}\" must be a non-negative integer, got {el.GetRawText()}");
        }
        return index;
    }
}
