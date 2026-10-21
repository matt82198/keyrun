namespace Keyrun.Host;

/// <summary>
/// The clock a <c>--replay-input</c> log's ticks are read against (finding f-6a00609e). Godot-free so Keyrun.Sim.Tests
/// can drive it; SimDriver holds one per replay.
/// <para>
/// The rule: until the launch's first run exists (the interactive launch opens on the between-runs Screen with no run,
/// <c>SimDriver.StartsOnScreen</c>), the clock is <see cref="ScreenFrames"/>, the count of host frames the first Screen
/// has been up (0 is the frame it is hooked, the first frame a replay can act); from the first run on it is
/// <c>SimDriver.TicksRun</c>. Nothing resets at the run start: an event whose tick the screen count had not reached
/// when the run started fires when <c>TicksRun</c> reaches that tick (so a log with Enter at 200 and W at 250 presses W
/// on sim tick 250, not 50). A log recorded by <c>--record-input</c> stamps every first-Screen key at 0 (TicksRun does
/// not advance without a run), and its Enter starts the run in that same frame, so every recorded log and committed
/// fixture replays on exactly the ticks it did before this rule; only a hand-written log with first-Screen events past 0
/// (e.g. the D15 talent capture: T at 0, picks at 40/80/120, T at 160, Enter at 200) uses the screen frames. Later
/// between-runs screens (a run exists) stay on <c>TicksRun</c>.
/// </para>
/// </summary>
public sealed class ReplayClock
{
    /// <summary>Host frames the first Screen has been up with no run (counted at the end of each such frame).</summary>
    public int ScreenFrames { get; private set; }

    /// <summary>The clock value a replay pass reads: <see cref="ScreenFrames"/> before the first run exists, else
    /// <paramref name="ticksRun"/>.</summary>
    /// <param name="runExists">The host has a run (<c>SimDriver.Run</c> is not null; it never goes back to null).</param>
    /// <param name="ticksRun"><c>SimDriver.TicksRun</c>.</param>
    /// <returns>The clock value.</returns>
    public int Now(bool runExists, int ticksRun) => runExists ? ticksRun : ScreenFrames;

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
