using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>
/// The playtest CLI (run from the repo root; tools/Keyrun.Playtest/GUIDE.md is the playtester's guide):
/// <code>
/// dotnet run --project tools/Keyrun.Playtest -- guide
/// dotnet run --project tools/Keyrun.Playtest -- kit
/// dotnet run --project tools/Keyrun.Playtest -- run --player ID --policy FILE --seed S --out DIR
/// dotnet run --project tools/Keyrun.Playtest -- memory --player ID
/// dotnet run --project tools/Keyrun.Playtest -- learn --player ID --lesson TEXT [--run N]
/// dotnet run --project tools/Keyrun.Playtest -- verify --player ID
/// dotnet run --project tools/Keyrun.Playtest -- ledger
/// </code>
/// </summary>
public static class Program
{
    /// <summary>The default key ledger path (repo-root relative).</summary>
    public const string DefaultLedger = "game/playtest/ledger.json";

    private const string Usage =
        "usage:\n" +
        "  playtest guide                                   how to play (read this first)\n" +
        "  playtest kit                                     every class button, from the game's data\n" +
        "  playtest run --player ID --policy FILE [--seed S] --out DIR [--ledger FILE] [--memory DIR] [--max-ticks T]\n" +
        "        plays the key the ledger holds for ID, records the ledger and ID's memory\n" +
        "  playtest run [--key N] [--seed S] [--policy FILE] [--class ID] [--dungeon ID] --out DIR [--max-ticks T]\n" +
        "        an off-ledger run (no --player): nothing is recorded; --class is seat 1's class (default warden);\n" +
        "        --dungeon is the dungeon id (default hollow-vault)\n" +
        "  playtest memory --player ID [--memory DIR]       what ID played, saw and learned\n" +
        "  playtest learn --player ID --lesson TEXT [--run N] [--memory DIR]\n" +
        "  playtest verify --player ID [--memory DIR]       re-check every receipt in ID's memory\n" +
        "  playtest ledger [--ledger FILE]";

    /// <summary>Entry point.</summary>
    /// <param name="args">Arguments.</param>
    /// <returns>0 on success, 1 when verify finds a broken receipt, 2 on a usage, policy or data error.</returns>
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            var rest = args.Skip(1).ToList();
            return args.Length == 0 ? Fail(Usage) : args[0] switch
            {
                "guide" => Print(Guide.Text),
                "kit" => Kit(),
                "run" => Run(Options(rest)),
                "memory" => Memory(Options(rest)),
                "learn" => Learn(Options(rest)),
                "verify" => Verify(Options(rest)),
                "ledger" => Ledger(Options(rest)),
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
        catch (InvalidOperationException e)
        {
            return Fail(e.Message);
        }
        catch (InvalidDataException e)
        {
            return Fail(e.Message);
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private static int Print(string text)
    {
        Console.Write(text);
        return 0;
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

    private static void Only(Dictionary<string, string> o, params string[] allowed)
    {
        foreach (var k in o.Keys)
        {
            if (!allowed.Contains(k, StringComparer.Ordinal))
            {
                throw new ArgumentException($"unknown option --{k}");
            }
        }
    }

    private static int Int(Dictionary<string, string> o, string name, int fallback) =>
        o.TryGetValue(name, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    private static string Player(Dictionary<string, string> o) =>
        o.TryGetValue("player", out var p) ? p : throw new ArgumentException("--player ID is required");

    private static string MemoryPath(Dictionary<string, string> o, string player) =>
        PlaytesterMemory.PathFor(o.TryGetValue("memory", out var dir) ? dir : PlaytestSession.DefaultMemoryDir, player);

    private static int Run(Dictionary<string, string> o)
    {
        Only(o, "player", "key", "seed", "policy", "out", "ledger", "memory", "max-ticks", "class", "dungeon");
        if (!o.TryGetValue("out", out var outDir))
        {
            throw new ArgumentException("--out DIR is required");
        }
        var registry = C.ContentLoader.Load();
        var seed = (long)Int(o, "seed", 1);
        int? maxTicks = o.ContainsKey("max-ticks") ? Int(o, "max-ticks", 0) : null;
        o.TryGetValue("policy", out var policyPath);

        if (o.TryGetValue("player", out var player))
        {
            if (o.ContainsKey("dungeon"))
            {
                throw new ArgumentException("--dungeon is for off-ledger runs (no --player)");
            }
            var session = new SessionOptions(player, seed, outDir)
            {
                PolicyPath = policyPath,
                Key = o.ContainsKey("key") ? Int(o, "key", 0) : null,
                LedgerPath = o.TryGetValue("ledger", out var lp) ? lp : DefaultLedger,
                MemoryDir = o.TryGetValue("memory", out var md) ? md : PlaytestSession.DefaultMemoryDir,
                MaxTicks = maxTicks,
            };
            var s = PlaytestSession.Play(session, registry);
            Report(player, s.KeyBefore, seed, s.Outcome, s.Play, s.KeyAfter, s.SummaryPath);
            return 0;
        }

        if (o.ContainsKey("ledger") || o.ContainsKey("memory"))
        {
            throw new ArgumentException("--ledger and --memory need --player");
        }
        var classIds = Keyrun.Host.Lobby.ClassIds(registry);
        if (o.TryGetValue("class", out var classOpt) && !classIds.Contains(classOpt, StringComparer.Ordinal))
        {
            throw new ArgumentException($"--class {classOpt} is not a class (one of {string.Join(", ", classIds)})");
        }
        var key =Int(o, "key", Keyrun.Sim.Runs.ScreenState.MinKey);
        var policy = policyPath is null ? null : PolicyReader.Load(policyPath, registry);
        var options = new PlaytestOptions(key, seed, policy) { MaxTicks = maxTicks, HumanClassId = classOpt ?? Keyrun.Host.Lobby.DefaultClassId };
        if (o.TryGetValue("dungeon", out var dungeonId))
        {
            if (!registry.Dungeons.ContainsKey(dungeonId))
            {
                throw new ArgumentException($"--dungeon {dungeonId} is not a dungeon (one of {string.Join(", ", registry.Dungeons.Keys.OrderBy(k => k, StringComparer.Ordinal))})");
            }
            options = options with { DungeonId = dungeonId };
        }
        Directory.CreateDirectory(outDir);
        using var receipts = new Keyrun.Bots.Shared.JsonlBotReceiptSink(Path.Combine(outDir, "bot-receipts.jsonl"));
        var result = PlaytestRunner.Play(options with { Receipts = receipts }, registry);
        var logPath = Path.Combine(outDir, "run.log").Replace('\\', '/');
        var summaryPath = Path.Combine(outDir, "summary.json").Replace('\\', '/');
        File.WriteAllText(logPath, result.Log);
        var outcome = Summary.OutcomeOf(result);
        var keyAfter = outcome.NextKey(key);
        File.WriteAllText(summaryPath, Summary.ToJson(result, options, new SummaryHeader(null, policyPath, logPath, key, keyAfter, null)));
        Report(null, key, seed, outcome, result, keyAfter, summaryPath);
        return 0;
    }

    private static void Report(string? player, int key, long seed, KeyOutcome outcome, PlaytestResult result, int keyAfter, string summaryPath) =>
        Console.WriteLine(FormattableString.Invariant(
            $"PLAYTEST {player ?? "-"} key={key} seed={seed} result=\"{outcome.Label}\" clear={result.Run.State.RunTimerTicks / Summary.TicksPerSecond}s par={result.Run.Encounter.ParTicks / Summary.TicksPerSecond}s deaths={result.Stats.Deaths.Count} keyAfter={keyAfter} summary={summaryPath}"));

    private static int Memory(Dictionary<string, string> o)
    {
        Only(o, "player", "memory");
        var player = Player(o);
        return Print(PlaytesterMemory.Load(MemoryPath(o, player), player).ToText());
    }

    private static int Learn(Dictionary<string, string> o)
    {
        Only(o, "player", "memory", "lesson", "run");
        var player = Player(o);
        if (!o.TryGetValue("lesson", out var lesson))
        {
            throw new ArgumentException("--lesson TEXT is required");
        }
        var path = MemoryPath(o, player);
        var memory = PlaytesterMemory.Load(path, player);
        var entry = memory.AddLesson(o.ContainsKey("run") ? Int(o, "run", 0) : null, lesson);
        memory.Save(path);
        Console.WriteLine(FormattableString.Invariant($"LEARNED {player} run {entry.Run}: {entry.Lessons[^1]}"));
        return 0;
    }

    private static int Verify(Dictionary<string, string> o)
    {
        Only(o, "player", "memory");
        var player = Player(o);
        var memory = PlaytesterMemory.Load(MemoryPath(o, player), player);
        var problems = memory.Verify();
        foreach (var p in problems)
        {
            Console.WriteLine("BROKEN " + p);
        }
        Console.WriteLine(FormattableString.Invariant($"VERIFY {player}: {memory.Entries.Count} run(s), {problems.Count} problem(s)"));
        return problems.Count == 0 ? 0 : 1;
    }

    private static int Ledger(Dictionary<string, string> o)
    {
        Only(o, "ledger");
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
