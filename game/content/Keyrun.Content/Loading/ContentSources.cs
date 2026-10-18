using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Keyrun.Content;

/// <summary>Supplies content files by path relative to content/data, e.g. <c>abilities/warden.json</c>.</summary>
public interface IContentSource
{
    /// <summary>Every file path this source holds, '/'-separated, relative to content/data.</summary>
    IReadOnlyCollection<string> Paths { get; }

    /// <summary>Returns the raw UTF-8 bytes of <paramref name="path"/>.</summary>
    /// <param name="path">A member of <see cref="Paths"/>.</param>
    /// <exception cref="ContentLoadException">The path is not in this source.</exception>
    byte[] Read(string path);
}

/// <summary>
/// The content JSON compiled into Keyrun.Content.dll. Keyrun.Content.csproj embeds every
/// content/data/**/*.json from the repo path (no copy) with logical name
/// <c>content/data/&lt;relative path&gt;</c>.
/// </summary>
public sealed class EmbeddedContentSource : IContentSource
{
    private const string Prefix = "content/data/";

    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _resourceByPath;

    /// <summary>The embedded content of this assembly.</summary>
    public static EmbeddedContentSource Instance { get; } = new();

    private EmbeddedContentSource()
    {
        _assembly = typeof(EmbeddedContentSource).Assembly;
        _resourceByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string resource in _assembly.GetManifestResourceNames())
        {
            string normalized = resource.Replace('\\', '/');
            if (normalized.StartsWith(Prefix, StringComparison.Ordinal))
                _resourceByPath.Add(normalized.Substring(Prefix.Length), resource);
        }
        Paths = _resourceByPath.Keys.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Paths { get; }

    /// <inheritdoc />
    public byte[] Read(string path)
    {
        if (!_resourceByPath.TryGetValue(path, out string? resource))
            throw new ContentLoadException("content: missing embedded file \"" + path + "\"");
        using Stream stream = _assembly.GetManifestResourceStream(resource)
            ?? throw new ContentLoadException("content: cannot open embedded file \"" + path + "\"");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

/// <summary>An in-memory content source (tests, tools, mods): path to bytes.</summary>
public sealed class DictionaryContentSource : IContentSource
{
    private readonly Dictionary<string, byte[]> _files;

    /// <summary>Snapshots <paramref name="files"/>.</summary>
    /// <param name="files">Path ('/'-separated, relative to content/data) to UTF-8 bytes.</param>
    public DictionaryContentSource(IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files.ToDictionary(f => f.Key, f => (byte[])f.Value.Clone(), StringComparer.Ordinal);
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

/// <summary>Content failed to load or validate. The loader never returns partial or defaulted data.</summary>
public sealed class ContentLoadException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What failed, naming the file and location.</param>
    public ContentLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception wrapping a parser or binder failure.</summary>
    /// <param name="message">What failed, naming the file and location.</param>
    /// <param name="inner">The underlying failure.</param>
    public ContentLoadException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
