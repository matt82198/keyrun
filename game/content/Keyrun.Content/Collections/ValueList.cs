using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// An immutable list with structural equality: two lists are equal when they hold equal items
/// in the same order. Content records use it instead of arrays or <see cref="List{T}"/> so that
/// record equality is deep, which is what makes "two loads produce equal graphs" checkable.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>, IContentCollection
{
    private readonly T[] _items;

    /// <summary>Creates an empty list.</summary>
    public ValueList()
    {
        _items = Array.Empty<T>();
    }

    /// <summary>Creates a list holding a snapshot of <paramref name="items"/>.</summary>
    /// <param name="items">Items, in order.</param>
    public ValueList(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
    }

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public T this[int index] => _items[index];

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    IEnumerable<KeyValuePair<string, object?>> IContentCollection.Entries =>
        _items.Select((item, i) => new KeyValuePair<string, object?>("[" + i + "]", item));

    /// <inheritdoc />
    public bool Equals(ValueList<T>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _items.SequenceEqual(other._items, EqualityComparer<T>.Default);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (T item in _items) hash.Add(item);
        return hash.ToHashCode();
    }
}
