using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>RED STUB (KF1-READABLE): keeps only the latest step, the defect the tests pin.</summary>
public sealed class StepBatch<TState, TEvent>
{
    private readonly List<TEvent> _events = new();

    /// <summary>A batch starting at <paramref name="start"/> with no events.</summary>
    public StepBatch(TState start)
    {
        Start = start;
    }

    /// <summary>The state before the frame's first step.</summary>
    public TState Start { get; private set; }

    /// <summary>The frame's events.</summary>
    public IReadOnlyList<TEvent> Events => _events;

    /// <summary>Records one step.</summary>
    public void Step(int frame, TState before, IEnumerable<TEvent> events)
    {
        Start = before;
        _events.Clear();
        _events.AddRange(events);
    }

    /// <summary>Starts over at <paramref name="state"/>.</summary>
    public void Reset(TState state)
    {
        Start = state;
        _events.Clear();
    }
}
