using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Measures = System.Collections.Generic.IReadOnlyList<Keyrun.Bots.Tests.ForkParityTests.RunMeasures>;
using RunMeasures = Keyrun.Bots.Tests.ForkParityTests.RunMeasures;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Per-measure noise tolerance for the fork parity dominance gate (finding f-e3daed76, R-PILE-7). A margin counts as
/// beyond the noise only when it EXCEEDS the tolerance. Derivation and re-measurement: <see cref="ForkParityTests"/>.
/// </summary>
/// <param name="ClearBp">Clear-time tolerance in basis points of the slower option's clear tick in that run.</param>
/// <param name="Deaths">Party deaths tolerance.</param>
/// <param name="Combos">Combos landed tolerance.</param>
internal sealed record NoiseTolerance(int ClearBp, int Deaths, int Combos)
{
    /// <summary>No tolerance: every strict margin counts (the bible's literal rule).</summary>
    public static readonly NoiseTolerance None = new(0, 0, 0);

    /// <summary>
    /// One standard deviation of seed noise per measure, measured on main a7069768 (the noise sweep in the
    /// ForkParity.margins.txt header): the median over the 24 measured sibling pairs of the per-seed margin's standard
    /// deviation at dial 90, seeds 1..12, the larger of keys 2 and 10, floored to the measure's grain. Clear: 132.1
    /// ticks over a 1847-tick median clear at key 10 = 715 bp (key 2: 90.4 / 1469 = 615 bp). Deaths: 0.52, so 0
    /// (one death is already beyond it). Combos: 3.85, so 3 (four combos is beyond it, three is not).
    /// </summary>
    public static readonly NoiseTolerance Measured = new(715, 0, 3);
}

/// <summary>Dominance margins: how far one fork option is ahead of a sibling, per run and per measure.</summary>
internal static class ForkParityMargins
{
    /// <summary>The environment variable that makes the fork parity run rewrite ForkParity.margins.txt.</summary>
    public const string RebaselineVariable = "KEYRUN_FORKPARITY_REBASELINE";

    private const string Separator = " | ";

    /// <summary>game/bots/Keyrun.Bots.Tests/ForkParity.margins.txt, found from the oracle fixture directory.</summary>
    public static string FilePath(string oracleDir) =>
        Path.GetFullPath(Path.Combine(oracleDir, "..", "..", "..", "..", "bots", "Keyrun.Bots.Tests", "ForkParity.margins.txt"));

    /// <summary>
    /// <paramref name="a"/>'s margin over <paramref name="b"/> in one run, positive when a is better: clear b - a
    /// ticks, deaths b - a, combos a - b.
    /// </summary>
    public static (int Clear, int Deaths, int Combos) Margin(RunMeasures a, RunMeasures b) =>
        (b.ClearTick - a.ClearTick, b.Deaths - a.Deaths, a.Combos - b.Combos);

    /// <summary>The clear-time tolerance in ticks for one run: <see cref="NoiseTolerance.ClearBp"/> of the slower clear.</summary>
    private static int ClearTolerance(int clearA, int clearB, NoiseTolerance tolerance) =>
        (int)((long)Math.Max(clearA, clearB) * tolerance.ClearBp / 10000);

    /// <summary>
    /// When <paramref name="a"/> dominates <paramref name="b"/> (<see cref="ForkParityTests.Dominates"/>), the margins
    /// that exceed <paramref name="tolerance"/>, as "run measure margin > tolerance"; empty when a does not dominate b or
    /// dominates it only inside the noise.
    /// </summary>
    public static IReadOnlyList<string> Excess(Measures a, Measures b, NoiseTolerance tolerance, IReadOnlyList<string> runNames)
    {
        var excess = new List<string>();
        if (!ForkParityTests.Dominates(a, b))
        {
            return excess;
        }
        for (var i = 0; i < a.Count; i++)
        {
            var (clear, deaths, combos) = Margin(a[i], b[i]);
            var clearTol = ClearTolerance(a[i].ClearTick, b[i].ClearTick, tolerance);
            if (clear > clearTol)
            {
                excess.Add(string.Create(CultureInfo.InvariantCulture, $"{runNames[i]} clear {clear} > {clearTol}"));
            }
            if (deaths > tolerance.Deaths)
            {
                excess.Add(string.Create(CultureInfo.InvariantCulture, $"{runNames[i]} deaths {deaths} > {tolerance.Deaths}"));
            }
            if (combos > tolerance.Combos)
            {
                excess.Add(string.Create(CultureInfo.InvariantCulture, $"{runNames[i]} combos {combos} > {tolerance.Combos}"));
            }
        }
        return excess;
    }

    /// <summary>
    /// True when <paramref name="a"/> dominates <paramref name="b"/> by the bible's rule (at least as good on every
    /// measure of every run, strictly better on one) AND at least one of those margins exceeds the noise tolerance.
    /// </summary>
    public static bool DominatesBeyondNoise(Measures a, Measures b, NoiseTolerance tolerance) =>
        Excess(a, b, tolerance, Enumerable.Range(0, a.Count).Select(i => "run " + i).ToList()).Count > 0;

    /// <summary>
    /// One line per sibling pair (content order, first option before second): "ability a b | run a-measures
    /// b-measures margin | ... | verdict", measures as clear/deaths/combos and the margin as a's over b's.
    /// </summary>
    public static IReadOnlyList<string> Lines(
        IReadOnlyList<(string ClassId, string AbilityId, IReadOnlyList<(string Option, Measures Runs)> Options)> all,
        IReadOnlyList<string> runNames,
        NoiseTolerance tolerance)
    {
        var lines = new List<string>();
        foreach (var (_, abilityId, options) in all)
        {
            for (var i = 0; i < options.Count; i++)
            {
                for (var j = i + 1; j < options.Count; j++)
                {
                    var (a, ra) = options[i];
                    var (b, rb) = options[j];
                    var runs = runNames.Select((name, r) => $"{name} {Triple(ra[r])} {Triple(rb[r])} {Signed(Margin(ra[r], rb[r]))}");
                    lines.Add($"{abilityId} {a} {b}{Separator}{string.Join(Separator, runs)}{Separator}{Verdict(a, ra, b, rb, tolerance)}");
                }
            }
        }
        return lines;
    }

    private static string Verdict(string a, Measures ra, string b, Measures rb, NoiseTolerance tolerance)
    {
        if (ra.SequenceEqual(rb))
        {
            return "identical";
        }
        if (DominatesBeyondNoise(ra, rb, tolerance))
        {
            return $"{a} DOMINATES {b} beyond noise";
        }
        if (DominatesBeyondNoise(rb, ra, tolerance))
        {
            return $"{b} DOMINATES {a} beyond noise";
        }
        if (ForkParityTests.Dominates(ra, rb))
        {
            return $"{a} dominates {b} within noise";
        }
        return ForkParityTests.Dominates(rb, ra) ? $"{b} dominates {a} within noise" : "tradeoff";
    }

    private static string Triple(RunMeasures m) => string.Create(CultureInfo.InvariantCulture, $"{m.ClearTick}/{m.Deaths}/{m.Combos}");

    private static string Signed((int Clear, int Deaths, int Combos) m) => $"{Signed(m.Clear)}/{Signed(m.Deaths)}/{Signed(m.Combos)}";

    private static string Signed(int v) => (v >= 0 ? "+" : "") + v.ToString(CultureInfo.InvariantCulture);

    /// <summary>The margins file: a generated header (format, tolerance, the noise sweep when re-measured), then the lines.</summary>
    public static string Render(IReadOnlyList<string> lines, NoiseTolerance tolerance, IReadOnlyList<string>? noise)
    {
        var header = new List<string>
        {
            $"# ForkParity.margins.txt: written by ForkParityTests with {RebaselineVariable}=1. Never hand-edit; re-baseline instead.",
            "# Finding f-e3daed76 (R-PILE-7). One line per sibling pair \"ability a b\". Per run: a's and b's clear/deaths/combos,",
            "# then a's margin over b (+ = a better: clear b-a ticks, deaths b-a, combos a-b). Last field: the verdict.",
            string.Create(CultureInfo.InvariantCulture,
                $"# Tolerance (NoiseTolerance.Measured): clear {tolerance.ClearBp} bp of the slower clear, deaths {tolerance.Deaths}, combos {tolerance.Combos}. Only a margin that exceeds it counts; only \"DOMINATES ... beyond noise\" gates."),
        };
        header.Add(noise is null ? "# Noise sweep: not re-measured in this render." : "# Noise sweep (re-measured at this re-baseline):");
        header.AddRange((noise ?? Array.Empty<string>()).Select(n => "#   " + n));
        return string.Join("\n", header.Concat(lines)) + "\n";
    }

    /// <summary>The margins file's lines keyed "ability a b" (comment and blank lines skipped).</summary>
    public static SortedDictionary<string, string> Parse(string text)
    {
        var lines = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            var cut = line.IndexOf(Separator, StringComparison.Ordinal);
            if (cut <= 0)
            {
                throw new FormatException("ForkParity.margins.txt: unparseable line: " + line);
            }
            lines[line[..cut]] = line;
        }
        return lines;
    }

    private static string Key(string line) => line[..line.IndexOf(Separator, StringComparison.Ordinal)];

    /// <summary>Pairs measured now but not recorded, then pairs recorded but no longer measured.</summary>
    public static IReadOnlyList<string> PairSetChanges(IReadOnlyDictionary<string, string> recorded, IReadOnlyList<string> current)
    {
        var keys = current.Select(Key).ToList();
        var changes = keys.Where(k => !recorded.ContainsKey(k)).Select(k => "unrecorded pair: " + k).ToList();
        changes.AddRange(recorded.Keys.Where(k => !keys.Contains(k)).Select(k => "recorded pair no longer measured: " + k));
        return changes;
    }

    /// <summary>
    /// For every pair in both, the margins that moved by more than <paramref name="tolerance"/> since the recording and
    /// any verdict change, as "ability a b: run measure margin old -> new; verdict 'old' -> 'new'". Informational.
    /// </summary>
    public static IReadOnlyList<string> Drift(IReadOnlyDictionary<string, string> recorded, IReadOnlyList<string> current, NoiseTolerance tolerance)
    {
        var drift = new List<string>();
        foreach (var line in current)
        {
            var key = Key(line);
            if (!recorded.TryGetValue(key, out var old))
            {
                continue;
            }
            var now = line.Split(Separator);
            var was = old.Split(Separator);
            var moved = new List<string>();
            for (var r = 1; r < Math.Min(now.Length, was.Length) - 1; r++)
            {
                var n = now[r].Split(' ');
                var w = was[r].Split(' ');
                var nm = n[3].Split('/').Select(Int).ToArray();
                var wm = w[3].Split('/').Select(Int).ToArray();
                var clearTol = ClearTolerance(Int(n[1].Split('/')[0]), Int(n[2].Split('/')[0]), tolerance);
                var tols = new[] { clearTol, tolerance.Deaths, tolerance.Combos };
                var names = new[] { "clear", "deaths", "combos" };
                for (var k = 0; k < 3; k++)
                {
                    if (Math.Abs(nm[k] - wm[k]) > tols[k])
                    {
                        moved.Add(string.Create(CultureInfo.InvariantCulture, $"{n[0]} {names[k]} margin {wm[k]} -> {nm[k]}"));
                    }
                }
            }
            if (now[^1] != was[^1])
            {
                moved.Add($"verdict '{was[^1]}' -> '{now[^1]}'");
            }
            if (moved.Count > 0)
            {
                drift.Add(key + ": " + string.Join("; ", moved));
            }
        }
        return drift;
    }

    private static int Int(string s) => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
}
