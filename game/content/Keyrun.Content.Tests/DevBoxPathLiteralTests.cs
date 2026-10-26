using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Hygiene guard (S1 no-devbox-paths): a hardcoded developer-box path such as <c>C:/Users/Mattt/...</c> works on the
/// developer's machine and fails on the CI runner (keyrun PR 724: <c>DirectoryNotFoundException</c> from
/// tools/Keyrun.Playtest.Tests/SeedIdleLeaderTests.cs). Every *.cs file under the game and tools roots is scanned for the
/// dev-box user prefixes (C:/Users/Mattt, case-insensitive); a hit outside a comment line fails the test and lists file:line. Comment lines are allowed so
/// documentation can name a dev-box path.
/// </summary>
public sealed class DevBoxPathLiteralTests
{
    // Built from fragments so this file never contains the literal it forbids. Only the developer's own box user is
    // forbidden; other C:/Users/... strings are fixture input data and are not dev-box paths.
    private static readonly string[] ForbiddenPrefixes =
    {
        "C:" + "/" + "Users" + "/" + "Mattt",
        "C:" + "\\" + "Users" + "\\" + "Mattt",
    };

    private static readonly string[] ScanRoots =
    {
        Path.Combine("game", "sim"),
        Path.Combine("game", "bots"),
        Path.Combine("game", "content"),
        Path.Combine("game", "src"),
        "tools",
    };

    [Fact]
    public void NoDevBoxPathLiteralsOutsideComments()
    {
        string root = RepoRoot();
        var hits = new List<string>();

        foreach (string scanRoot in ScanRoots)
        {
            string dir = Path.Combine(root, scanRoot);
            if (!Directory.Exists(dir)) continue;

            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(root, file)) continue;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (IsCommentLine(lines[i])) continue;
                    foreach (string prefix in ForbiddenPrefixes)
                    {
                        if (lines[i].Contains(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{i + 1}: {lines[i].Trim()}");
                            break;
                        }
                    }
                }
            }
        }

        Assert.True(hits.Count == 0,
            "Hardcoded dev-box path literal(s) outside comment lines (use a repo-relative path or an environment variable):\n  "
            + string.Join("\n  ", hits));
    }

    private static bool IsCommentLine(string line)
    {
        string t = line.TrimStart();
        return t.StartsWith("//", StringComparison.Ordinal)
            || t.StartsWith("/*", StringComparison.Ordinal)
            || t.StartsWith("*", StringComparison.Ordinal);
    }

    private static bool IsBuildOutput(string root, string file)
    {
        string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        return rel.Split('/').Any(seg => seg.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                      || seg.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "design")) && Directory.Exists(Path.Combine(d.FullName, "game"))) return d.FullName;
        }
        throw new DirectoryNotFoundException("repo root (design/ and game/) not found above " + AppContext.BaseDirectory);
    }
}
