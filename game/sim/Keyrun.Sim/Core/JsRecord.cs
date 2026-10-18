using System;
using System.Collections;
using System.Collections.Generic;

namespace Keyrun.Sim.Core;

/// <summary>
/// A string-keyed map that iterates exactly like a JS object's own keys (the TS <c>Record&lt;string, T&gt;</c>
/// fields of sim state): canonical array-index keys ("0".."4294967294") first in ascending numeric order, then
/// every other key in insertion order. Overwriting keeps a key's position; delete then re-insert moves it to
/// the end. Iteration order matters wherever the TS walks <c>Object.keys</c>, so ports must use this, never a
/// hash map whose order is unspecified.
/// </summary>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed class JsRecord<TValue> : IEnumerable<KeyValuePair<string, TValue>>
{
    private readonly Dictionary<string, TValue> _values = new(StringComparer.Ordinal);
    private readonly List<string> _stringKeys = new();
    private readonly SortedDictionary<uint, string> _indexKeys = new();

    /// <summary>Number of keys.</summary>
    public int Count => _values.Count;

    /// <summary>Gets or sets a value. Getting a missing key throws <see cref="KeyNotFoundException"/>.</summary>
    /// <param name="key">The key.</param>
    public TValue this[string key]
    {
        get => _values[key];
        set
        {
            if (!_values.ContainsKey(key))
            {
                if (TryArrayIndex(key, out var index))
                {
                    _indexKeys.Add(index, key);
                }
                else
                {
                    _stringKeys.Add(key);
                }
            }
            _values[key] = value;
        }
    }

    /// <summary>Keys in JS own-property order (a snapshot; no compiler iterator, which would read the thread id).</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            var keys = new List<string>(_values.Count);
            foreach (var kv in _indexKeys)
            {
                keys.Add(kv.Value);
            }
            keys.AddRange(_stringKeys);
            return keys;
        }
    }

    /// <summary>True when the key is present (JS <c>key in obj</c> for own data keys).</summary>
    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <summary>Looks a key up without throwing.</summary>
    public bool TryGetValue(string key, out TValue value) => _values.TryGetValue(key, out value!);

    /// <summary>JS <c>delete obj[key]</c>; returns whether the key existed.</summary>
    public bool Remove(string key)
    {
        if (!_values.Remove(key))
        {
            return false;
        }
        if (TryArrayIndex(key, out var index))
        {
            _indexKeys.Remove(index);
        }
        else
        {
            _stringKeys.Remove(key);
        }
        return true;
    }

    /// <summary>A copy with the same key order, each value passed through <paramref name="cloneValue"/>.</summary>
    public JsRecord<TValue> Clone(Func<TValue, TValue> cloneValue)
    {
        var copy = new JsRecord<TValue>();
        foreach (var kv in this)
        {
            copy[kv.Key] = cloneValue(kv.Value);
        }
        return copy;
    }

    /// <summary>Collection-initializer support: same as the indexer setter.</summary>
    public void Add(string key, TValue value)
    {
        if (_values.ContainsKey(key))
        {
            throw new ArgumentException($"duplicate key {key}", nameof(key));
        }
        this[key] = value;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
    {
        var pairs = new List<KeyValuePair<string, TValue>>(_values.Count);
        foreach (var k in Keys)
        {
            pairs.Add(new KeyValuePair<string, TValue>(k, _values[k]));
        }
        return pairs.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>JS array index: canonical decimal (no leading zero, no sign) of an integer in [0, 2^32 - 2].</summary>
    private static bool TryArrayIndex(string key, out uint index)
    {
        index = 0;
        if (key.Length == 0 || key.Length > 10 || (key.Length > 1 && key[0] == '0'))
        {
            return false;
        }
        ulong v = 0;
        foreach (var c in key)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
            v = (v * 10) + (ulong)(c - '0');
        }
        if (v > 4294967294UL)
        {
            return false;
        }
        index = (uint)v;
        return true;
    }
}
