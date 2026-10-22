using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using C = Keyrun.Content;
using RunMeasures = Keyrun.Bots.Tests.ForkParityTests.RunMeasures;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The fork parity seed-noise study (finding f-8ae6250a): the measured source of <see cref="NoiseTolerance.Measured"/>.
/// </summary>
/// <remarks>
/// <para>Study: every fork option and the base kit through the seeded Hollow Vault harness
/// (<see cref="ForkParityTests.Measure"/>) at keys 2 and 10, seeds 1..<see cref="Seeds"/>, bot skill dial 90 (the dial
/// where the seed decides each bot's mistakes, so each seed is a different trajectory through the same content, the
/// same kind of perturbation a behaviour PR makes; at dial 100 the seed is dead, f-313fba9c). Recorded per key: every
/// option's mean and sample sd (n-1) per axis (clear ticks, deaths, combos), and for every measured sibling pair the
/// mean and sample sd of the pair's per-seed margin.</para>
/// <para>Derivation: the dominance gate thresholds a margin between two siblings in the same seeded run, so the noise
/// that matters is the paired margin's sd, not one option's. Per key and axis the study takes the median of that sd
/// over the measured sibling pairs (one typical pair, not the noisiest); per axis the larger key; the tolerance is
/// K = 2 times it, floored to the axis grain (clear in basis points of the key's median base clear). Floored, because a
/// margin counts only when it EXCEEDS the tolerance and margins are integers, so "margin > floor(2 sd)" is exactly
/// "margin > 2 sd". Why two sigma: a true tie (two siblings with equal expected measures) then flags as dominance on a
/// given axis only about 5% of the time under roughly normal noise, while a real gap of 2 sd or more still shows; at
/// one sigma (the old rule) about a third of true ties would flip it, which is what #575 measured.</para>
/// <para>Re-run: <c>KEYRUN_FORKPARITY_NOISESTUDY=1 dotnet test game/bots/Keyrun.Bots.Tests --filter "FullyQualifiedName~ForkParityNoiseStudyTests"</c>
/// rewrites <see cref="FileName"/> with the current HEAD sha; commit it in its own commit with the reason. Never edit
/// the file by hand: <see cref="Load"/> recomputes the medians and the tolerance from the recorded pair sds and refuses
/// a file that disagrees, and refuses a missing file (fail closed).</para>
/// </remarks>
internal static class ForkParityNoiseStudy
{
    /// <summary>The environment variable that makes the study run and rewrite the file.</summary>
    public const string StudyVariable = "KEYRUN_FORKPARITY_NOISESTUDY";

    /// <summary>The study file, next to ForkParity.margins.txt.</summary>
    public const string FileName = "fork-parity-noise.json";

    /// <summary>The sigma multiplier (see the remarks for why two).</summary>
    public const int K = 2;

    /// <summary>Seeds per key (seeds 1..Seeds). At least <see cref="MinSeeds"/>.</summary>
    public const int Seeds = 32;

    /// <summary>The fewest seeds per key a study file may record.</summary>
    public const int MinSeeds = 24;

    /// <summary>The skill dial of the study runs.</summary>
    public const int Dial = 90;

    private static readonly (string Key, string SetupFile)[] Keys =
    {
        ("k2", "hollow-vault-k2.setup.json"),
        ("k10", "hollow-vault-k10.setup.json"),
    };

    private static readonly string[] Axes = { "clear", "deaths", "combos" };

    /// <summary>game/bots/Keyrun.Bots.Tests/fork-parity-noise.json, found from the oracle fixture directory.</summary>
    public static string FilePath(string oracleDir) =>
        Path.GetFullPath(Path.Combine(oracleDir, "..", "..", "..", "..", "bots", "Keyrun.Bots.Tests", FileName));

    /// <summary>The tolerance the checked-in study derives (see <see cref="Load"/>).</summary>
    public static readonly Lazy<NoiseTolerance> Tolerance = new(() => Load(File.Exists(FilePath(RolesOracleTraceTests.OracleDir()))
        ? File.ReadAllText(FilePath(RolesOracleTraceTests.OracleDir()))
        : throw new InvalidOperationException($"fork parity: the noise study {FilePath(RolesOracleTraceTests.OracleDir())} is missing; run the study with {StudyVariable}=1 (fail closed: no study, no tolerance)")));

    /// <summary>The per-key summary a study file records: median base clear and the median paired-margin sd per axis.</summary>
    internal sealed record KeySummary(string Key, double MedianBaseClear, int MeasuredPairs, double Clear, double Deaths, double Combos)
    {
        /// <summary>The clear sd in basis points of the median base clear.</summary>
        public double ClearBp => Clear * 10000 / MedianBaseClear;
    }

    /// <summary>
    /// Parses a study file and returns its tolerance. Throws when a field is missing (main sha, run parameters, keys,
    /// pairs), when fewer than <see cref="MinSeeds"/> seeds were run, when K is not <see cref="K"/>, or when the
    /// recorded medians or tolerance disagree with the recorded pair sds.
    /// </summary>
    public static NoiseTolerance Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sha = Str(root, "mainSha");
        if (sha.Length != 40 || !sha.All(Uri.IsHexDigit))
        {
            throw Bad($"mainSha \"{sha}\" is not a 40-hex commit sha");
        }
        var p = Obj(root, "params");
        var first = Int(p, "seedFirst");
        var last = Int(p, "seedLast");
        if (last - first + 1 < MinSeeds)
        {
            throw Bad($"seeds {first}..{last} are fewer than {MinSeeds} per key");
        }
        if (Int(p, "k") != K)
        {
            throw Bad($"k is {Int(p, "k")}, the rule is {K}");
        }
        Int(p, "dial");
        Int(p, "maxTicks");
        var summaries = Keys.Select(k => Summary(Obj(Obj(root, "keys"), k.Key), k.Key)).ToList();
        var derived = Derive(summaries);
        var t = Obj(root, "tolerance");
        var recorded = new NoiseTolerance(Int(t, "clearBp"), Int(t, "deaths"), Int(t, "combos"));
        if (recorded != derived)
        {
            throw Bad($"recorded tolerance {recorded} is not {K} x the recorded sds ({derived}); re-run the study, never hand-edit");
        }
        return derived;
    }

    /// <summary>K times the larger key's median paired-margin sd per axis, floored to the axis grain.</summary>
    public static NoiseTolerance Derive(IReadOnlyList<KeySummary> keys) => new(
        (int)Math.Floor(K * keys.Max(k => k.ClearBp)),
        (int)Math.Floor(K * keys.Max(k => k.Deaths)),
        (int)Math.Floor(K * keys.Max(k => k.Combos)));

    /// <summary>Reads one key's block and checks its median sds against its recorded pairs.</summary>
    private static KeySummary Summary(JsonElement key, string name)
    {
        var medianBase = Num(key, "medianBaseClear");
        if (medianBase <= 0)
        {
            throw Bad($"{name}.medianBaseClear must be positive");
        }
        Obj(key, "options");
        var pairs = Obj(key, "pairs").EnumerateObject().ToList();
        if (pairs.Count == 0)
        {
            throw Bad($"{name} records no measured sibling pairs");
        }
        var sds = Axes.Select(a => Median(pairs.Select(pr => Num(Obj(pr.Value, a), "sd")))).ToArray();
        var m = Obj(key, "medianPairedMarginSd");
        for (var i = 0; i < Axes.Length; i++)
        {
            if (Num(m, Axes[i]) != sds[i])
            {
                throw Bad($"{name}.medianPairedMarginSd.{Axes[i]} {Num(m, Axes[i])} is not the median of its pairs' sds ({sds[i]})");
            }
        }
        if (Int(key, "measuredPairs") != pairs.Count)
        {
            throw Bad($"{name}.measuredPairs disagrees with its pair count {pairs.Count}");
        }
        return new KeySummary(name, medianBase, pairs.Count, sds[0], sds[1], sds[2]);
    }

    /// <summary>The per-key summaries of a valid study file, for the margins header and the tests.</summary>
    public static IReadOnlyList<KeySummary> Summaries(string json)
    {
        Load(json);
        using var doc = JsonDocument.Parse(json);
        return Keys.Select(k => Summary(Obj(Obj(doc.RootElement, "keys"), k.Key), k.Key)).ToList();
    }

    /// <summary>Runs the study over seeds 1..<paramref name="seeds"/> and renders the study file.</summary>
    public static string Run(int seeds, string mainSha)
    {
        var registry = ForkParityTests.Registry.Value;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("finding", "f-8ae6250a");
            w.WriteString("mainSha", mainSha);
            w.WriteString("rule", $"tolerance per axis = floor({K} x max over keys of the median over measured sibling pairs of the sample sd (n-1) of the per-seed margin of each pair); clear in bp of the median base-kit clear of the key. See ForkParityNoiseStudy.cs.");
            w.WriteStartObject("params");
            w.WriteNumber("seedFirst", 1);
            w.WriteNumber("seedLast", seeds);
            w.WriteNumber("dial", Dial);
            w.WriteNumber("maxTicks", 24000);
            w.WriteNumber("k", K);
            w.WriteString("sd", "sample (n-1)");
            w.WriteEndObject();
            w.WriteStartObject("keys");
            var summaries = new List<KeySummary>();
            foreach (var (key, file) in Keys)
            {
                var sweep = Enumerable.Range(1, seeds).Select(seed => ForkParityTests.LoadScenario(("noise", file, seed, Dial))).ToList();
                var baseRuns = sweep.Select(s => ForkParityTests.Measure(s, null, null)).ToList();
                var medianBase = Median(baseRuns.Select(r => (double)r.ClearTick));
                w.WriteStartObject(key);
                w.WriteString("setup", file);
                w.WriteNumber("medianBaseClear", medianBase);
                w.WriteStartObject("options");
                WriteStats(w, "base", baseRuns);
                var pairSds = new List<(string Name, double[] Mean, double[] Sd)>();
                foreach (var cls in registry.Classes.Values)
                {
                    foreach (var fork in cls.Talents)
                    {
                        var baseDef = registry.Abilities[fork.AbilityId];
                        var options = fork.Options.Select(o => (o.Id, Runs: sweep.Select(s => ForkParityTests.Measure(s, cls.Id, ForkParityTests.ToSim(C.TalentPicks.ApplyPatch(baseDef, o.Patch)))).ToList())).ToList();
                        foreach (var (id, runs) in options)
                        {
                            WriteStats(w, fork.AbilityId + ":" + id, runs);
                        }
                        for (var i = 0; i < options.Count; i++)
                        {
                            for (var j = i + 1; j < options.Count; j++)
                            {
                                var m = options[i].Runs.Zip(options[j].Runs, ForkParityMargins.Margin).ToList();
                                if (m.All(x => x == (0, 0, 0)))
                                {
                                    continue; // an unmeasured fork: both options run identically on every seed
                                }
                                var axes = new[] { m.Select(x => (double)x.Clear).ToList(), m.Select(x => (double)x.Deaths).ToList(), m.Select(x => (double)x.Combos).ToList() };
                                pairSds.Add(($"{fork.AbilityId} {options[i].Id} {options[j].Id}", axes.Select(a => R(a.Average())).ToArray(), axes.Select(a => R(SampleSd(a))).ToArray()));
                            }
                        }
                    }
                }
                w.WriteEndObject();
                w.WriteStartObject("pairs");
                foreach (var (name, mean, sd) in pairSds)
                {
                    w.WriteStartObject(name);
                    for (var a = 0; a < Axes.Length; a++)
                    {
                        w.WriteStartObject(Axes[a]);
                        w.WriteNumber("meanMargin", mean[a]);
                        w.WriteNumber("sd", sd[a]);
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
                var med = Axes.Select((_, a) => Median(pairSds.Select(x => x.Sd[a]))).ToArray();
                w.WriteNumber("measuredPairs", pairSds.Count);
                w.WriteStartObject("medianPairedMarginSd");
                for (var a = 0; a < Axes.Length; a++)
                {
                    w.WriteNumber(Axes[a], med[a]);
                }
                w.WriteEndObject();
                w.WriteEndObject();
                summaries.Add(new KeySummary(key, medianBase, pairSds.Count, med[0], med[1], med[2]));
            }
            w.WriteEndObject();
            var tol = Derive(summaries);
            w.WriteStartObject("tolerance");
            w.WriteNumber("clearBp", tol.ClearBp);
            w.WriteNumber("deaths", tol.Deaths);
            w.WriteNumber("combos", tol.Combos);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>The checked-out HEAD sha (the study records the tree it measured).</summary>
    public static string HeadSha()
    {
        var psi = new ProcessStartInfo("git", "rev-parse HEAD")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = RolesOracleTraceTests.OracleDir(),
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("fork parity noise study: git did not start");
        var sha = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit();
        return proc.ExitCode == 0 ? sha : throw new InvalidOperationException("fork parity noise study: git rev-parse HEAD failed");
    }

    /// <summary>One header line per key for ForkParity.margins.txt.</summary>
    public static IReadOnlyList<string> HeaderLines(string json) =>
        Summaries(json).Select(k => string.Create(CultureInfo.InvariantCulture,
            $"{k.Key} median paired-margin sd over {k.MeasuredPairs} sibling pairs ({FileName}): clear {k.Clear:0.0} ticks ({k.ClearBp:0} bp of median base clear {k.MedianBaseClear:0}), deaths {k.Deaths:0.00}, combos {k.Combos:0.00}")).ToList();

    private static void WriteStats(Utf8JsonWriter w, string name, IReadOnlyList<RunMeasures> runs)
    {
        w.WriteStartObject(name);
        var axes = new[] { runs.Select(r => (double)r.ClearTick).ToList(), runs.Select(r => (double)r.Deaths).ToList(), runs.Select(r => (double)r.Combos).ToList() };
        for (var a = 0; a < Axes.Length; a++)
        {
            w.WriteStartObject(Axes[a]);
            w.WriteNumber("mean", R(axes[a].Average()));
            w.WriteNumber("sd", R(SampleSd(axes[a])));
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    private static double R(double x) => Math.Round(x, 3, MidpointRounding.AwayFromZero);

    internal static double SampleSd(IReadOnlyList<double> v)
    {
        var mean = v.Average();
        return Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1));
    }

    internal static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    private static InvalidOperationException Bad(string why) => new($"fork parity: {FileName} is invalid (fail closed): {why}");

    private static JsonElement Obj(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : throw Bad($"missing object \"{name}\"");

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw Bad($"missing string \"{name}\"");

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : throw Bad($"missing number \"{name}\"");

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : throw Bad($"missing integer \"{name}\"");
}
