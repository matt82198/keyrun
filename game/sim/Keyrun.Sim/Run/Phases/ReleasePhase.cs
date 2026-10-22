namespace Keyrun.Sim.Runs;

/// <summary>Phase 12 of <see cref="RunPipeline"/>: released seats respawn at the entrance.</summary>
internal static class ReleasePhase
{
    /// <summary>Respawns the seats whose release delay is over and records this tick's dead seats' respawn ticks.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        // PATH-TO-BOSS (STATE.md D44): an open (abandoned or unfilled) seat is not in the run, so it never holds a wipe open.
        var openSeats = c.Opts?.OpenSeats;
        // WIPE-IS-RELEASE (R-BACKFILL-2, layout runs): seats whose release delay is over respawn at the entrance before
        // targeting and the wipe check read the party; this tick's dead seats get their respawn tick.
        var (state, releases) = Release.Step(c.Before, c.State, openSeats, c.Emit);
        c.State = state;
        c.Releases = releases;
    }
}
