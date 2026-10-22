using System;
using System.Globalization;
using System.Text.Json;

namespace Keyrun.Host;

/// <summary>
/// The weekly affix rotation (BIBLE s8 "the rotation index comes with the key from the host"; Matt 2026-10-20, "lean
/// into m+ design heavily while in the dungeon"). As in WoW's Mythic+, the week rolls at the US weekly reset, Tuesday
/// 15:00 UTC: every run started between two resets gets the same rotation index, so the same Fortified/Tyrannical parity
/// and the same key-set Clause, and the index moves on by one at each reset. It lives in the host, never the sim: the sim
/// stays pure and takes the index as an input (<see cref="Keyrun.Sim.Runs.RunOptions.RotationIndex"/>). A run's index
/// is recorded in its input-log header (<see cref="HeaderField"/>), and a replay builds its run from that recorded index,
/// never from the clock. Godot-free so Keyrun.Sim.Tests can drive it.
/// </summary>
public static class AffixWeek
{
    /// <summary>The input-log header field that records a run's index.</summary>
    public const string HeaderField = "rotationIndex";

    /// <summary>The US weekly reset (Tuesday 2025-12-30 15:00 UTC) that opens the week whose index is 0.</summary>
    public static readonly DateTime EpochReset = new(2025, 12, 30, 15, 0, 0, DateTimeKind.Utc);

    private const long TicksPerWeek = TimeSpan.TicksPerDay * 7;

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
    /// The index for a run started at <paramref name="utcNow"/>: whole weekly resets (Tuesday 15:00 UTC) since
    /// <see cref="EpochReset"/>, floored, wrapped into [0, <paramref name="rotationLength"/>). Monday night and Tuesday
    /// 14:59 UTC share the old week; Tuesday 15:00 UTC opens the next one. Integer tick math, no rounding.
    /// </summary>
    /// <param name="utcNow">The run's start instant; must be <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="rotationLength">From <see cref="RotationLength"/>.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentException"><paramref name="utcNow"/> is not a UTC time.</exception>
    public static int IndexFor(DateTime utcNow, int rotationLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rotationLength, 1);
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException($"the affix week is computed in UTC, got a {utcNow.Kind} time", nameof(utcNow));
        }
        var ticks = (utcNow - EpochReset).Ticks;
        var weeks = ticks >= 0 ? ticks / TicksPerWeek : -((-ticks + TicksPerWeek - 1) / TicksPerWeek);
        return (int)(((weeks % rotationLength) + rotationLength) % rotationLength);
    }

    /// <summary>
    /// The <c>--rotation N</c> host override: a non-negative integer that pins a live run's index, so a scene test at
    /// keys 5-9 never depends on the date.
    /// </summary>
    /// <param name="raw">The flag's value.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentException">Not a non-negative integer.</exception>
    public static int ParseOverride(string raw)
    {
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            throw new ArgumentException($"--rotation needs a non-negative integer, got '{raw}'");
        }
        return n;
    }

    /// <summary>
    /// The index a <c>--write-movie</c> launch without <c>--rotation N</c> is built with (finding f-e1ff13e3): the proof
    /// frames are evidence for Matt (D15) and evidence is deterministic, so they never take the week from the clock. At
    /// a key with a rotating affix the week flips Fortified and Tyrannical and the frames' health bars with it.
    /// </summary>
    public const int MovieRotationIndex = 0;

    /// <summary>
    /// The index a run is built with. A live run takes <paramref name="overrideIndex"/> (<c>--rotation N</c>) when given,
    /// else, for a <c>--write-movie</c> launch, <see cref="MovieRotationIndex"/>, else this week's
    /// (<see cref="IndexFor"/>). A replay never reads the clock: the log's recorded index wins, then the override, then 0
    /// (a log written before the header carried an index replays at 0, the index the host used when those logs were
    /// recorded).
    /// </summary>
    /// <param name="replaying">True when the launch replays an input log.</param>
    /// <param name="recorded">The log's recorded index, or null when it has none.</param>
    /// <param name="overrideIndex">The <c>--rotation N</c> value, or null.</param>
    /// <param name="utcNow">The run's start instant (UTC); read only on a live run without an override that does not
    /// write the movie.</param>
    /// <param name="rotationLength">From <see cref="RotationLength"/>.</param>
    /// <param name="writeMovie">True when the launch writes the proof frames (<c>--write-movie DIR</c>).</param>
    /// <returns>The index.</returns>
    public static int ForRun(bool replaying, int? recorded, int? overrideIndex, DateTime utcNow, int rotationLength, bool writeMovie = false) =>
        replaying ? recorded ?? overrideIndex ?? 0 : overrideIndex ?? (writeMovie ? MovieRotationIndex : IndexFor(utcNow, rotationLength));

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
