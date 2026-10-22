namespace Keyrun.Sim.Runs;

/// <summary>Phase 6 of <see cref="RunPipeline"/>: a control effect that landed this tick ends a cast.</summary>
internal static class ControlStopsPhase
{
    /// <summary>Ends the casts this tick's stuns, freezes and silences stop.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        var content = run.Content;
        // STUN-STOPS-CAST (BIBLE s9.3): a stun, freeze or silence that landed this tick (in combat, or the Break bar's
        // stagger just above) ends an interruptible cast, before the telegraph check below reads the casts. f-c63b43bf: a
        // stun also ends a stunnable actor's uninterruptible cast (kick-immune is not stun-immune; the boss is stun-immune).
        c.State = ControlStops.Apply(
            run.State, c.State, content.StopEffectIds, content.Abilities, run.State.Tick, c.Emit,
            new ControlStops.StunRules(content.StunEffectIds, content.StunImmuneClassIds, c.Pre.Staggered));
    }
}
