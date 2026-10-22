namespace Keyrun.Sim.Runs;

/// <summary>Phase 5 of <see cref="RunPipeline"/>: stun-immune actors refuse this tick's stuns.</summary>
internal static class RefuseImmuneStunsPhase
{
    /// <summary>Refuses every stun that landed on a stun-immune actor this tick.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        var content = run.Content;
        // STUN-IMMUNE-REFUSES (f-94749699): a stun-immune actor (the boss) takes no stun at all; every stun that landed
        // on one this tick is refused with an "immune" event, before the stops and anything else read the state.
        c.State = ControlStops.RefuseImmuneStuns(
            c.State, new ControlStops.StunRules(content.StunEffectIds, content.StunImmuneClassIds, null), run.State.Tick, c.Emit);
    }
}
