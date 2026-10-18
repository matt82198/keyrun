using System;
using System.IO;
using System.Linq;

namespace Keyrun.Tools.Replay;

/// <summary>
/// <c>Keyrun.Replay &lt;trace.jsonl | directory&gt;...</c>: replays each oracle trace (a directory means every
/// <c>*.trace.jsonl</c> in it, sorted) through the C# run root and prints one summary line per trace, plus the first
/// divergence in full. Exit 0 when every trace matches, 1 when any diverges, 2 on a usage or input error.
/// </summary>
public static class Program
{
    /// <summary>Entry point.</summary>
    /// <param name="args">Trace files or directories.</param>
    /// <returns>0 all match, 1 a divergence, 2 usage or input error.</returns>
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        {
            Console.Error.WriteLine("usage: Keyrun.Replay <trace.jsonl | directory>...");
            return 2;
        }
        try
        {
            var paths = args.SelectMany(a => Directory.Exists(a)
                    ? Directory.GetFiles(a, "*.trace.jsonl").OrderBy(p => p, StringComparer.Ordinal).ToArray()
                    : new[] { a })
                .ToList();
            if (paths.Count == 0)
            {
                Console.Error.WriteLine("no *.trace.jsonl found");
                return 2;
            }
            var allMatch = true;
            foreach (var path in paths)
            {
                var report = TraceReplayer.ReplayFile(path);
                Console.WriteLine(report.Matches ? report.Summary() : report.Describe());
                allMatch &= report.Matches;
            }
            return allMatch ? 0 : 1;
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            Console.Error.WriteLine("Keyrun.Replay: " + e.Message);
            return 2;
        }
    }
}
