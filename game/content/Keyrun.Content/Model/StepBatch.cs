using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// KF1-READABLE: every sim step a render frame ran, as one batch for the view. The host runs up to five steps in a
/// frame when it falls behind (TickAccumulator), and a view that reads only the latest step's events and before-state
/// misses a swing, flinch or kick resolved on an earlier step of that frame (6 enemy swings lost in the 2026-10-20 run
/// log). <see cref="Start"/> is the state before the frame's first step and <see cref="Events"/> every step's events in
/// order. Pure and Godot-free; src/Host/SimDriver.cs feeds it, src/View/ActorView.cs reads it. Render interpolation keeps
/// using the latest step's before-state.
/// </summary>
/// <typeparam name="TState">The sim state type.</typeparam>
/// <typeparam name="TEvent">The sim event type.</typeparam>
public sealed class StepBatch<TState, TEvent>
{
    private readonly List<TEvent> _events = new();
    private int _frame;
    private bool _open;

    /// <summary>A batch starting at <paramref name="start"/> with no events.</summary>
    public StepBatch(TState start)
    {
        Start = start;
    }

    /// <summary>The state before the first step of the latest frame that stepped.</summary>
    public TState Start { get; private set; }

    /// <summary>The events of every step of the latest frame that stepped, in step order.</summary>
    public IReadOnlyList<TEvent> Events => _events;

    /// <summary>
    /// Records one step run in render frame <paramref name="frame"/> from <paramref name="before"/>. The first step of a
    /// new frame starts a new batch; a later step of the same frame appends its events.
    /// </summary>
    public void Step(int frame, TState before, IEnumerable<TEvent> events)
    {
        if (!_open || frame != _frame)
        {
            _open = true;
            _frame = frame;
            Start = before;
            _events.Clear();
        }
        _events.AddRange(events);
    }

    /// <summary>Starts over at <paramref name="state"/> (a new run): the next step opens a fresh batch.</summary>
    public void Reset(TState state)
    {
        _open = false;
        Start = state;
        _events.Clear();
    }
}
