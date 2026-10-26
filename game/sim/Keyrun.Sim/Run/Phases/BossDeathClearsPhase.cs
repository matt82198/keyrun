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
        // PACK-CLEAR (R-WOW-2, seq 857): a pack's last member dying emits pack_clear, in or out of pull order.
        Pulls.EmitPackClears(c.State, run.Dungeon, c.Events, run.State.Tick, c.Encounter.Boss?.Config.BossActorId, c.Emit);
        // BOSS-DEATH-ENDS-ENCOUNTER (f-77fcfca4, Matt 2026-10-20: "boss death ends the encounter, adds despawn, like
        // WoW"): the step the armed boss dies, its adds have just despawned above, so its pull is cleared on this same
        // step rather than on the next step's pre-combat check, and run-end detection below sees the encounter over.
        // Unarmed runs (every oracle trace) never take this path. The dungeon is passed as the encounter step above gets
        // it: every pull is spawned from tick 0 (PRESPAWN-AGGRO), so no unspawned-pull view is needed.
        if (c.Encounter.Boss is not { } dyingBoss ||
            c.Events.FirstOrDefault(e => e.Type == "death" && e.Get("actorId") is JsInt id && id.Value == dyingBoss.Config.BossActorId) is not { } death)
        {
            return;
        }
        // R-MULTIBOSS-1 (event seq 118): with several bosses armed, every boss's death emits boss_death, and only the FINAL
        // boss's death ends the run (its pull is the dungeon's last, so clearing it moves the pull index past the end). An
        // earlier boss's pull clears and the next boss's rules are armed in its place; the run goes on into the next segment.
        // A one-boss run (BossesArmed 0, the Hollow Vault) emits nothing here and arms nothing, exactly as before.
        if (c.Encounter.BossesArmed > 1)
        {
            c.Emit(new SimEvent(death.Tick, "boss_death",
                ("actorId", JsValue.Int(dyingBoss.Config.BossActorId)),
                ("bossIndex", JsValue.Int(c.Encounter.BossIndex)),
                ("final", JsValue.Bool(c.Encounter.NextBosses.Count == 0))));
        }
        c.Encounter = Pulls.CheckPullAdvance(c.State, run.Dungeon, c.Encounter, run.State.Tick, c.Emit);
        c.Encounter = BossRules.Advance(c.Encounter);
    }
}
