using System;
using System.Collections.Generic;

namespace Keyrun.Host;

/// <summary>
/// The <c>--replay-input</c> due cursor (lane 3E-b, made Godot-free for finding f-6a00609e): hands back a log's items,
/// in order, once the replay clock (<see cref="ReplayClock"/>) reaches their tick. <c>InputReplayer</c> holds one for the
/// key events and one for the camera headings; Keyrun.Sim.Tests drives it directly.
/// </summary>
/// <typeparam name="T">The item (a key event or a camera heading).</typeparam>
public sealed class ReplayFeed<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly Func<T, int> _tickOf;
    private int _next;

    /// <summary>Builds a feed.</summary>
    /// <param name="items">The items, ticks never decreasing (the parse refuses a log whose ticks go down).</param>
    /// <param name="tickOf">An item's tick.</param>
    public ReplayFeed(IReadOnlyList<T> items, Func<T, int> tickOf)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _tickOf = tickOf ?? throw new ArgumentNullException(nameof(tickOf));
    }

    /// <summary>Items handed out so far.</summary>
    public int Applied => _next;

    /// <summary>Every item handed out.</summary>
    public bool Done => _next >= _items.Count;

    /// <summary>The tick of the last item handed out, or -1.</summary>
    public int LastTick => _next == 0 ? -1 : _tickOf(_items[_next - 1]);

    /// <summary>The items now due (tick at or before <paramref name="now"/>) that have not been handed out.</summary>
    /// <param name="now">The replay clock (<see cref="ReplayClock.Now"/>).</param>
    /// <returns>The due items, in log order.</returns>
    public IReadOnlyList<T> Due(int now)
    {
        var due = new List<T>();
        while (_next < _items.Count && _tickOf(_items[_next]) <= now)
        {
            due.Add(_items[_next]);
            _next++;
        }
        return due;
    }
}
