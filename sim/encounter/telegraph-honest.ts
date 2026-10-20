/**
 * sim/encounter/telegraph-honest.ts -- when a scripted step fires, and whether its cast will really start
 * (TELEGRAPH-HONEST, twin of game/sim/Keyrun.Sim/Encounter/Scripts.cs FiresAt/ReadyAt/WillCast, STATE.md D28).
 *
 * A telegraph announces a cast `leadTicks` ahead; it is drawn only when combat will accept that cast on its
 * cast tick, judged from the caster's state at announcement: (1) its cooldown clock lets it start (the GCD is
 * over and a charge is back, cooldowns.ts chargesAvailable), and (2) no LATER step of the same script fires a
 * ready cast on that same tick (combat runs the actor's casts in script order and the later one replaces it).
 *
 * Usage: `if (tg && firesAt(step, t + tg.leadTicks) && willCast(actor, script, i, t + tg.leadTicks, abilities))`.
 */
import type { ActorState, Tick } from "./types";
import type { AbilityDef } from "../combat/abilities";
import type { EnemyScript, ScriptStep } from "./scripts";
import { chargesAvailable } from "../combat/cooldowns";

/** True when `step` (a loop or an `at` step; never onDeath) is due on `tick`. */
export function firesAt(step: ScriptStep, tick: Tick): boolean {
  if (step.at === "loop") {
    const every = step.every ?? 0;
    return every > 0 && tick > 0 && tick % every === 0;
  }
  if (step.at === "onDeath") return false; // handled directly in stepEnemyScripts
  const at = step.at;
  if (step.every !== undefined && step.every > 0) {
    return tick >= at && (tick - at) % step.every === 0;
  }
  return tick === at;
}

/** Combat lets `actor` start `cast` on `castTick` as far as its cooldown clock decides; an unknown ability is not judged. */
function readyAt(actor: ActorState, cast: string, castTick: Tick, abilities: Record<string, AbilityDef>): boolean {
  const def = abilities[cast];
  if (def === undefined) return true;
  if (castTick < actor.gcdReadyTick) return false;
  return chargesAvailable(actor, def, castTick) >= 1;
}

/** Step `index` of `script`, due on `castTick`, will really start its cast there, so it may be telegraphed. */
export function willCast(
  actor: ActorState, script: EnemyScript, index: number, castTick: Tick, abilities: Record<string, AbilityDef>,
): boolean {
  if (!readyAt(actor, script[index]!.cast, castTick, abilities)) return false;
  for (let j = index + 1; j < script.length; j++) {
    const later = script[j]!;
    if (later.at !== "onDeath" && firesAt(later, castTick) && readyAt(actor, later.cast, castTick, abilities)) return false;
  }
  return true;
}
