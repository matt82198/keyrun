using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>
/// The playtest CLI (run from the repo root):
/// <code>
/// dotnet run --project tools/Keyrun.Playtest -- run --player ID [--key N] --seed S --policy FILE --out DIR
/// dotnet run --project tools/Keyrun.Playtest -- kit
/// dotnet run --project tools/Keyrun.Playtest -- ledger
/// </code>
/// </summary>
public static class Program
{
    /// <summary>The default key ledger path (repo-root relative).</summary>
    public const string DefaultLedger = "game/playtest/ledger.json";

    private const string Usage =
        "usage:\n" +
        "  playtest run [--player ID] [--key N] [--seed S] [--policy FILE] --out DIR [--ledger FILE] [--max-ticks T]\n" +
        "  playtest kit\n" +
        "  playtest ledger [--ledger FILE]\n" +
        "With --player and no --key the run plays the key the ledger holds for that player; the ledger is updated.";

    /// <summary>Entry point.</summary>
    /// <param name="args">Arguments.</param>
    /// <returns>0 on success, 2 on a usage or policy error.</returns>
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            return args.Length == 0 ? Fail(Usage) : args[0] switch
            {
                "run" => Run(Options(args.Skip(1).ToList())),
                "kit" => Kit(),
                "ledger" => Ledger(Options(args.Skip(1).ToList())),
                _ => Fail(Usage),
            };
        }
        catch (PolicyException e)
        {
            return Fail(e.Message);
        }
        catch (ArgumentException e)
        {
            return Fail(e.Message + "\n" + Usage);
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private static Dictionary<string, string> Options(List<string> args)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count)
            {
                throw new ArgumentException($"bad argument \"{args[i]}\"");
            }
            map[args[i][2..]] = args[++i];
        }
        return map;
    }

    private static int Int(Dictionary<string, string> o, string name, int fallback) =>
        o.TryGetValue(name, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    private static int Run(Dictionary<string, string> o)
    {
        foreach (var k in o.Keys)
        {
            if (k is not ("player" or "key" or "seed" or "policy" or "out" or "ledger" or "max-ticks"))
            {
                throw new ArgumentException($"unknown option --{k}");
            }
        }
        if (!o.TryGetValue("out", out var outDir))
        {
            throw new ArgumentException("--out DIR is required");
        }
        var registry = C.ContentLoader.Load();
        o.TryGetValue("player", out var player);
        var ledgerPath = o.TryGetValue("ledger", out var lp) ? lp : DefaultLedger;
        var ledger = player is null ? null : KeyLedger.Load(ledgerPath);
        var key = Int(o, "key", ledger?.KeyOf(player!) ?? Keyrun.Sim.Runs.ScreenState.MinKey);
        var seed = (long)Int(o, "seed", 1);
        o.TryGetValue("policy", out var policyPath);
        var policy = policyPath is null ? null : PolicyReader.Load(policyPath, registry);
        var options = new PlaytestOptions(key, seed, policy) { MaxTicks = o.ContainsKey("max-ticks") ? Int(o, "max-ticks", 0) : null };

        var result = PlaytestRunner.Play(options, registry);
        Directory.CreateDirectory(outDir);
        var logPath = Path.Combine(outDir, "run.log").Replace('\\', '/');
        var summaryPath = Path.Combine(outDir, "summary.json").Replace('\\', '/');
        File.WriteAllText(logPath, result.Log);
        var outcome = Summary.OutcomeOf(result);
        var keyAfter = outcome.NextKey(key);
        int? runNumber = null;
        if (ledger is not null)
        {
            var row = ledger.Record(player!, key, seed, outcome, summaryPath);
            runNumber = row.Run;
            keyAfter = row.KeyAfter;
            ledger.Save(ledgerPath);
        }
        File.WriteAllText(summaryPath, Summary.ToJson(result, options, new SummaryHeader(player, policyPath, logPath, key, keyAfter, runNumber)));
        Console.WriteLine(FormattableString.Invariant(
            $"PLAYTEST {player ?? "-"} key={key} seed={seed} result=\"{outcome.Label}\" clear={result.Run.State.RunTimerTicks / Summary.TicksPerSecond}s par={result.Run.Encounter.ParTicks / Summary.TicksPerSecond}s deaths={result.Stats.Deaths.Count} keyAfter={keyAfter} summary={summaryPath}"));
        return 0;
    }

    private static int Ledger(Dictionary<string, string> o)
    {
        var ledgerPath = o.TryGetValue("ledger", out var lp) ? lp : DefaultLedger;
        Console.Write(File.Exists(ledgerPath) ? File.ReadAllText(ledgerPath) : "{ \"players\": {} }\n");
        return 0;
    }

    // The class kits as the game's own data says them: what a playtester can press, never what to press when.
    private static int Kit()
    {
        var registry = C.ContentLoader.Load();
        foreach (var cls in registry.Classes.Values)
        {
            Console.WriteLine(FormattableString.Invariant($"{cls.Id} ({cls.Name}) resource {cls.Loop.Resource} 0-{cls.Loop.Max}, loop {cls.Loop.Kind}"));
            var slots = new[] { "1", "2", "3", "4", "5", "Q interrupt", "E defensive", "R utility", "F movement" };
            var ids = PolicyReader.KitIds(cls);
            for (var i = 0; i < ids.Count; i++)
            {
                var a = registry.Abilities[ids[i]];
                Console.WriteLine(FormattableString.Invariant(
                    $"  {slots[i],-12} {a.Id,-22} {a.Name,-14} cast {a.CastTicks / 20.0:0.0}s  cooldown {a.CooldownTicks / 20.0:0.0}s  range {a.Range / 1000.0:0.#}m  shape {a.Shape.Kind.ToString().ToLowerInvariant()}  tags [{string.Join(", ", a.Tags)}]"));
            }
            foreach (var fork in cls.Talents)
            {
                Console.WriteLine($"  talent fork {fork.AbilityId}:");
                foreach (var option in fork.Options)
                {
                    Console.WriteLine($"    {option.Id,-12} {option.Name}: {option.Text}");
                }
            }
        }
        return 0;
    }
}
