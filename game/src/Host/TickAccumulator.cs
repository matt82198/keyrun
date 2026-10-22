using System;

namespace Keyrun.Host;

/// <summary>
/// Fixed-step accumulator for the 20 Hz sim (STATE.md D9). Godot-free: frame deltas come in as seconds,
/// are rounded to whole nanoseconds, and are spent in whole ticks. Rounding each delta to the nearest
/// nanosecond keeps 1/60 s frames exact enough that three of them always add up to one 50 ms tick
/// (3 x 16666667 ns = 50000001 ns), so 120 frames at 60 fps are exactly 40 ticks.
/// </summary>
public sealed class TickAccumulator
{
    /// <summary>Nanoseconds per second.</summary>
    public const long NanosPerSecond = 1_000_000_000;

    private readonly int _maxStepsPerFrame;
    private long _pendingNanos;

    /// <summary>Builds an accumulator for <paramref name="hz"/> ticks per second.</summary>
    /// <param name="hz">Tick rate (Keyrun.Sim Tick.TickHz).</param>
    /// <param name="maxStepsPerFrame">Cap on ticks run in one frame; a longer stall drops the excess time
    /// instead of spiralling (counted in <see cref="DroppedTicks"/>).</param>
    public TickAccumulator(int hz, int maxStepsPerFrame)
    {
        if (hz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hz));
        }
        if (maxStepsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStepsPerFrame));
        }
        TickNanos = NanosPerSecond / hz;
        _maxStepsPerFrame = maxStepsPerFrame;
    }

    /// <summary>Length of one tick, ns.</summary>
    public long TickNanos { get; }

    /// <summary>Ticks discarded because a single frame owed more than the per-frame cap.</summary>
    public long DroppedTicks { get; private set; }

    /// <summary>Total frame time fed in, ns.</summary>
    public long ElapsedNanos { get; private set; }

    /// <summary>Fraction of the next tick already accumulated, in [0, 1): the render interpolation factor.</summary>
    public double Alpha => (double)_pendingNanos / TickNanos;

    /// <summary>Adds one frame's delta and returns how many ticks are now due (0..maxStepsPerFrame).</summary>
    /// <param name="deltaSeconds">Frame delta, seconds; negative or NaN counts as 0.</param>
    public int Advance(double deltaSeconds)
    {
        var nanos = double.IsNaN(deltaSeconds) || deltaSeconds <= 0 ? 0L : (long)Math.Round(deltaSeconds * NanosPerSecond);
        ElapsedNanos += nanos;
        _pendingNanos += nanos;
        var steps = 0;
        while (_pendingNanos >= TickNanos && steps < _maxStepsPerFrame)
        {
            _pendingNanos -= TickNanos;
            steps++;
        }
        if (_pendingNanos >= TickNanos)
        {
            DroppedTicks += _pendingNanos / TickNanos;
            _pendingNanos %= TickNanos;
        }
        return steps;
    }
}
