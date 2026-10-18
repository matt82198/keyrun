using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;

namespace Keyrun.Content.Tests;

/// <summary>
/// Copies every embedded content file into memory so a test can mutate one file (as JSON
/// nodes or raw text) and load the result through the real loader.
/// </summary>
internal sealed class MutableSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public MutableSource()
    {
        foreach (string path in EmbeddedContentSource.Instance.Paths)
            _files[path] = EmbeddedContentSource.Instance.Read(path);
    }

    public MutableSource Edit(string path, Action<JsonNode> mutate)
    {
        JsonNode node = JsonNode.Parse(_files[path]) ?? throw new InvalidOperationException(path);
        mutate(node);
        _files[path] = Encoding.UTF8.GetBytes(node.ToJsonString());
        return this;
    }

    public MutableSource EditText(string path, Func<string, string> mutate)
    {
        string before = Encoding.UTF8.GetString(_files[path]);
        string after = mutate(before);
        if (after == before) throw new InvalidOperationException("text mutation of " + path + " changed nothing");
        _files[path] = Encoding.UTF8.GetBytes(after);
        return this;
    }

    public MutableSource Add(string path, string text)
    {
        _files[path] = Encoding.UTF8.GetBytes(text);
        return this;
    }

    public MutableSource Remove(string path)
    {
        if (!_files.Remove(path)) throw new InvalidOperationException(path);
        return this;
    }

    public DictionaryContentSource Build() => new(_files);
}
