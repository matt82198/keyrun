using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Finds the repo's content JSON from a test's bin or fixture directory. Content lives in two roots that share one
/// logical namespace (Keyrun.Content.csproj): the frozen repo-root <c>content/data/</c> and the C#-only
/// <c>game/content/data/</c>. The repo root is the first ancestor that holds BOTH, never merely the first one with a
/// <c>content/data/&lt;kind&gt;</c> folder (that is <c>game/</c> once game/content/data grows the same kind folder).
/// </summary>
internal static class ContentRoots
{
    /// <summary>The repo root above <paramref name="start"/>: the first ancestor holding content/data and game/content/data.</summary>
    /// <param name="start">A directory inside the repo.</param>
    /// <exception cref="DirectoryNotFoundException">No ancestor holds both roots.</exception>
    public static string RepoRootAbove(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "content", "data"))
                && Directory.Exists(Path.Combine(dir.FullName, "game", "content", "data")))
                return dir.FullName;
        throw new DirectoryNotFoundException("no repo root (content/data beside game/content/data) above " + start);
    }

    /// <summary>
    /// Every *.json in <c>content/data/&lt;kind&gt;/</c> of both roots: the repo root's files first, then the C#-only
    /// root's, each ordinal by path. A root without the folder contributes nothing.
    /// </summary>
    /// <param name="start">A directory inside the repo.</param>
    /// <param name="kind">A content kind folder, e.g. <c>abilities</c>.</param>
    public static IReadOnlyList<string> KindFiles(string start, string kind)
    {
        string root = RepoRootAbove(start);
        var files = new List<string>();
        foreach (string dataRoot in new[] { Path.Combine(root, "content", "data"), Path.Combine(root, "game", "content", "data") })
        {
            string dir = Path.Combine(dataRoot, kind);
            if (Directory.Exists(dir))
                files.AddRange(Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal));
        }
        return files;
    }
}
