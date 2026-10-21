/**
 * sim/combat/holder.ts -- HOLDER-LOCK (STATE.md D30), the frozen TS mirror of
 * game/sim/Keyrun.Sim/Combat/Holder.cs so the oracle (ORACLE_WRITE=1) records the C# rule.
 *
 * Each engaged enemy has one holder (`ActorState.holderId`); its scripted casts and chase go at that
 * holder only (sim/encounter/script-target.ts). Engage is the body pull: an enemy with no living
 * holder locks the nearest living player (range, then lowest id). After that only a taunt moves it;
 * the holder's death hands it to the nearest living player; a wipe clears it. Damage, healing and
 * threat never move it. The threat table still fills but no longer picks the target.
 */
import type { ActorState, SimState } from "./types";
import type { AbilityDef } from "./abilities";
import { isqrt } from "./movement";

/** The tag a taunt carries (warden.taunt is the class's "utility" ability). */
export const TAUNT_TAG = "utility";

function rangeMmOf(a: ActorState, b: ActorState): number {
  const dx = a.pos.x - b.pos.x;
  const dz = a.pos.z - b.pos.z;
  return isqrt(dx * dx + dz * dz);
}

/** A taunt: a target-shaped "utility" ability carrying a threat op (warden.taunt). */
export function isTaunt(def: AbilityDef): boolean {
  return def.shape.kind === "target" && (def.tags ?? []).includes(TAUNT_TAG) && def.effects.some((op) => op.op === "threat");
}

/** A completed taunt by a living player on a living enemy makes the taunter its holder. */
export function applyTaunt(caster: ActorState, target: ActorState): void {
  if (caster.kind === "player" && caster.alive && target.kind === "enemy" && target.alive) {
    target.holderId = caster.id;
  }
}

/** Every living enemy whose holder is missing or no longer a living player locks the nearest living
 * player; with no living player it holds no one. A living holder is never replaced here. Mutates
 * `state.actors` in place; run by fixupTargeting (run-wiring.ts) at creation and every tick. */
export function lockHolders(state: SimState): void {
  const actors = Object.values(state.actors);
  const players = actors.filter((a) => a.kind === "player" && a.alive);
  for (const enemy of actors) {
    if (enemy.kind !== "enemy" || !enemy.alive) continue;
    const current = enemy.holderId !== undefined ? state.actors[enemy.holderId] : undefined;
    if (current !== undefined && current.alive && current.kind === "player") continue;
    if (players.length === 0) {
      delete enemy.holderId;
      continue;
    }
    let best = players[0]!;
    let bestRange = rangeMmOf(enemy, best);
    for (const p of players) {
      const r = rangeMmOf(enemy, p);
      if (r < bestRange || (r === bestRange && p.id < best.id)) {
        best = p;
        bestRange = r;
      }
    }
    enemy.holderId = best.id;
  }
}
