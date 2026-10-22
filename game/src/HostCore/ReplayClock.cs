namespace Keyrun.Host;

/// <summary>
/// The input-log clock: the one clock a <c>--record-input</c> log is stamped on and a <c>--replay-input</c> log's ticks
/// are read against (findings f-6a00609e and f-6b922005, ruling R-CLOCK-1). Godot-free so Keyrun.Sim.Tests can drive
/// it; SimDriver holds one whenever it records or replays (one clock for both when it does both).
/// <para>
/// The rule (R-CLOCK-1): until the launch's first run exists (the interactive launch opens on the between-runs Screen
/// with no run, <c>SimDriver.StartsOnScreen</c>), the clock is <see cref="ScreenFrames"/>, the count of host frames the
/// first Screen has been up (0 is the frame it is hooked, the first frame a replay can act); once the run exists it is
/// <see cref="ScreenFrames"/> (frozen at the run start: it only counts frames with no run, and a run never goes back to
/// none, so it is the screen frames counted before the run started) plus <c>SimDriver.TicksRun</c>. So the clock never
/// goes down, and a recording that stamps it is monotonic by construction: Enter on Screen frame S starts the run, and a
/// key in the run's first ticks is stamped S + TicksRun, never a value below S. A log with Enter at 200 and W at 250
/// presses W on sim tick 50. Every committed fixture and every recording whose Enter is on frame 0 has an offset of 0
/// and replays on exactly the ticks it always did; the hand-written D15 talent capture (T at 0, picks at 40/80/120, T at
/// 160, Enter at 200, no run keys) still starts the run at frame 200. Later between-runs screens (a run exists) stay on
/// the run side of the clock.
/// </para>
/// </summary>
public sealed class ReplayClock
{
    /// <summary>Host frames the first Screen has been up with no run (counted at the end of each such frame). Once the
    /// run exists it no longer moves: it is the screen frames counted before the run started.</summary>
    public int ScreenFrames { get; private set; }

    /// <summary>The clock value (R-CLOCK-1): <see cref="ScreenFrames"/> before the first run exists, else
    /// <see cref="ScreenFrames"/> (frozen at the run start) plus <paramref name="ticksRun"/>. A replay pass reads it.</summary>
    /// <param name="runExists">The host has a run (<c>SimDriver.Run</c> is not null; it never goes back to null).</param>
    /// <param name="ticksRun"><c>SimDriver.TicksRun</c> (0 while no run exists).</param>
    /// <returns>The clock value.</returns>
    public int Now(bool runExists, int ticksRun) => runExists ? ScreenFrames + ticksRun : ScreenFrames;

    /// <summary>The tick a recording stamps on a key event or camera heading (finding f-6b922005): the same
    /// <see cref="Now"/> a replay of the log reads, so a live recording round-trips.</summary>
    /// <param name="runExists">The host has a run.</param>
    /// <param name="ticksRun"><c>SimDriver.TicksRun</c>.</param>
    /// <returns>The stamp.</returns>
    public int Stamp(bool runExists, int ticksRun) => Now(runExists, ticksRun);

    /// <summary>Ends one host frame: counts it as a screen frame when no run exists and the Screen is up (hooked).</summary>
    /// <param name="runExists">The host has a run.</param>
    /// <param name="screenUp">The Screen is hooked (before that the replay waits and no frame is counted).</param>
    public void EndFrame(bool runExists, bool screenUp)
    {
        if (!runExists && screenUp)
        {
            ScreenFrames++;
        }
    }
}
