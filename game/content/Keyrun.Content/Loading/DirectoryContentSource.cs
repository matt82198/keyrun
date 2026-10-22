using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// A content source over a directory on disk (tests, tools, mods): every <c>**/*.json</c> under the root, keyed by its
/// '/'-separated path relative to the root, the same shape <see cref="EmbeddedContentSource"/> gives game/content/data.
/// <see cref="Paths"/> is in ordinal order, so a loader that walks it (or <see cref="ContentDirectory.Under"/>) sees the
/// same files in the same order on every machine and every run (ruling R-LOADER-1: C#-only content loads in ordinal path order).
/// </summary>
public sealed class DirectoryContentSource : IContentSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>Snapshots every JSON file under <paramref name="root"/>.</summary>
    /// <param name="root">The directory that plays the role of content/data.</param>
    /// <exception cref="ContentLoadException">The directory does not exist.</exception>
    public DirectoryContentSource(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        string full = Path.GetFullPath(root);
        if (!Directory.Exists(full)) throw new ContentLoadException("content: missing directory \"" + root + "\"");
        foreach (string file in Directory.EnumerateFiles(full, "*.json", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(full, file).Replace('\\', '/');
            _files.Add(relative, File.ReadAllBytes(file));
        }
        Paths = _files.Keys.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Paths { get; }

    /// <inheritdoc />
    public byte[] Read(string path)
    {
        if (!_files.TryGetValue(path, out byte[]? bytes))
            throw new ContentLoadException("content: missing file \"" + path + "\"");
        return (byte[])bytes.Clone();
    }
}

/// <summary>
/// Directory views over any <see cref="IContentSource"/>: the per-kind content directories (layout/, forces/) are read by
/// walking every JSON file under a prefix in ordinal path order, so adding a pack, a room or a forces value is adding a
/// file, never editing a shared list.
/// </summary>
public static class ContentDirectory
{
    /// <summary>
    /// Every JSON path in <paramref name="source"/> under <paramref name="directory"/> (recursive), in ordinal order.
    /// </summary>
    /// <param name="source">The content source.</param>
    /// <param name="directory">A '/'-terminated prefix relative to content/data, e.g. <c>layout/hollow-vault/rooms/</c>.</param>
    /// <returns>The matching paths, ordinal order.</returns>
    public static IReadOnlyList<string> Under(IContentSource source, string directory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!directory.EndsWith('/')) throw new ArgumentException("directory must end with '/': " + directory, nameof(directory));
        return source.Paths
            .Where(p => p.StartsWith(directory, StringComparison.Ordinal) && p.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }
}
