using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// An immutable, insertion-ordered string-keyed map with structural equality: two maps are equal
/// when they hold equal keys and values in the same order. Used for the by-id registries (in the
/// same file order the TS loader uses) and for JSON objects with open keys (ability variants,
/// clause params).
/// </summary>
/// <typeparam name="TValue">Value type.</typeparam>
public sealed class ValueMap<TValue> : IReadOnlyDictionary<string, TValue>, IEquatable<ValueMap<TValue>>, IContentCollection
{
    private readonly KeyValuePair<string, TValue>[] _entries;
    private readonly Dictionary<string, TValue> _index;

    /// <summary>Creates an empty map.</summary>
    public ValueMap()
        : this(Array.Empty<KeyValuePair<string, TValue>>())
    {
    }

    /// <summary>Creates a map from ordered entries.</summary>
    /// <param name="entries">Entries in order; keys must be unique.</param>
    /// <exception cref="ArgumentException">A key appears twice.</exception>
    public ValueMap(IEnumerable<KeyValuePair<string, TValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToArray();
        _index = new Dictionary<string, TValue>(_entries.Length, StringComparer.Ordinal);
        foreach (KeyValuePair<string, TValue> entry in _entries)
        {
            if (!_index.TryAdd(entry.Key, entry.Value))
                throw new ArgumentException("duplicate key \"" + entry.Key + "\"", nameof(entries));
        }
    }

    /// <inheritdoc />
    public int Count => _entries.Length;

    /// <inheritdoc />
    public TValue this[string key] => _index[key];

    /// <summary>Keys in insertion order.</summary>
    public IEnumerable<string> Keys => _entries.Select(e => e.Key);

    /// <summary>Values in insertion order.</summary>
    public IEnumerable<TValue> Values => _entries.Select(e => e.Value);

    /// <inheritdoc />
    public bool ContainsKey(string key) => _index.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value) => _index.TryGetValue(key, out value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, TValue>>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    IEnumerable<KeyValuePair<string, object?>> IContentCollection.Entries =>
        _entries.Select(e => new KeyValuePair<string, object?>("[\"" + e.Key + "\"]", e.Value));

    /// <inheritdoc />
    public bool Equals(ValueMap<TValue>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_entries.Length != other._entries.Length) return false;
        EqualityComparer<TValue> cmp = EqualityComparer<TValue>.Default;
        for (int i = 0; i < _entries.Length; i++)
        {
            if (!string.Equals(_entries[i].Key, other._entries[i].Key, StringComparison.Ordinal)) return false;
            if (!cmp.Equals(_entries[i].Value, other._entries[i].Value)) return false;
        }
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ValueMap<TValue>);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (KeyValuePair<string, TValue> entry in _entries)
        {
            hash.Add(entry.Key, StringComparer.Ordinal);
            hash.Add(entry.Value);
        }
        return hash.ToHashCode();
    }
}

/// <summary>Lets <see cref="NullGuard"/> walk a value collection's items without reflection on indexers.</summary>
internal interface IContentCollection
{
    /// <summary>Each item with a path label such as <c>[0]</c> or <c>["mend"]</c>.</summary>
    IEnumerable<KeyValuePair<string, object?>> Entries { get; }
}
