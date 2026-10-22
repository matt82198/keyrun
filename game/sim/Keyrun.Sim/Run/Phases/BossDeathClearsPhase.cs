using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>Phase 11 of <see cref="RunPipeline"/>: the armed boss's death clears its pull on the same step.</summary>
internal static class BossDeathClearsPhase
{
    /// <summary>Advances the pull when this tick's events hold the armed boss's death.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        // BOSS-DEATH-ENDS-ENCOUNTER (f-77fcfca4, Matt 2026-10-20: "boss death ends the encounter, adds despawn, like
        // WoW"): the step the armed boss dies, its adds have just despawned above, so its pull is cleared on this same
        // step rather than on the next step's pre-combat check, and run-end detection below sees the encounter over.
        // Unarmed runs (every oracle trace) never take this path. The dungeon is passed as the encounter step above gets
        // it: every pull is spawned from tick 0 (PRESPAWN-AGGRO), so no unspawned-pull view is needed.
        if (c.Encounter.Boss is { } dyingBoss
            && c.Events.Any(e => e.Type == "death" && e.Get("actorId") is JsInt id && id.Value == dyingBoss.Config.BossActorId))
        {
            c.Encounter = Pulls.CheckPullAdvance(c.State, run.Dungeon, c.Encounter, run.State.Tick, c.Emit);
        }
    }
}
