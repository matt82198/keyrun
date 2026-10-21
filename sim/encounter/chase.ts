/**
 * sim/encounter/chase.ts — closes a melee enemy onto its target so its scripted swing can land.
 *
 * Before this file nothing in sim/ ever moved an enemy: scripts.ts only emitted casts, and
 * script-target.ts range-gates (blocks) a target-shaped cast whose target is out of range. A Choir
 * acolyte spawned 3026mm from the party with a 3000mm swing therefore never swung at all
 * (tests/sim/integration/enemy-melee-reach.test.ts is the public-path repro).
 *
 * Rule: a living enemy that is not mid-cast, whose script casts at least one hostile (no "heal"
 * op) target-shaped ability or caster-centred damage area (circle/cone with a "damage" op), has an
 * engage range = the SHORTEST such reach (a target cast's `range`, an area's `shape.radius`). If its
 * target (script-target.ts's default: highest-threat living player, else nearest) is farther than
 * `engageRange - CHASE_MARGIN_MM`, it emits one integer `move` command straight at that target,
 * facing it. The margin keeps a chaser from parking on the exact range boundary, where collision
 * push-out could nudge it back out of range. Enemies whose script has no such ability (e.g. the
 * Choir Cantor, which only heals) never move. With no `abilities` map (legacy callers), nothing
 * is known about range, so nothing chases -- same deliberately-safe default as script-target.ts.
 *
 * Per-enemy tuning (defect (d)/(e)) rides on the ActorState, set from content at spawn: the margin
 * is `actor.chaseMarginMm ?? CHASE_MARGIN_MM`, and the emitted `move` walks at
 * `actor.speedMmPerTick ?? baseSpeedMmPerTick` in combat/rules.ts. Speed is deliberately NOT a
 * field on the command: core/tick.ts re-runs validateCommand on every command (encounter-emitted
 * ones too), and validateCommand rejects a client-supplied speed, so actor state is the only
 * channel a client cannot forge.
 *
 * Usage: `commands.push(...chaseCommands(state, scripts, abilities));`
 */
import type { ActorId, Command, SimState } from "./types";
import type { AbilityDef } from "../combat/abilities";
import type { EnemyScript } from "./scripts";
import { rangeMm } from "../combat/casting";
import { normaliseTo, yawBetween } from "../core/fixed";
import { resolveScriptTarget } from "./script-target";

/** How far inside its engage range a chaser stops, in mm. */
export const CHASE_MARGIN_MM = 500;

/**
 * How close `ability` must be to land on its target, or null when it does not engage: a hostile
 * (no "heal" op) target-shaped cast reaches its `range`; a caster-centred damage area (a circle or
 * cone carrying a "damage" op, e.g. hollowWarden.slam since MECHANICS-PUNISH G1) reaches its
 * `shape.radius`. An area with no damage op (a stack call, a death-bloom Window) does not engage.
 */
function engageReachMm(ability: AbilityDef | undefined): number | null {
  if (ability === undefined || ability.effects.some((op) => op.op === "heal")) return null;
  if (ability.shape.kind === "target") return ability.range;
  if ((ability.shape.kind === "circle" || ability.shape.kind === "cone") && ability.shape.radius !== undefined &&
    ability.effects.some((op) => op.op === "damage")) {
    return ability.shape.radius;
  }
  return null;
}

/** Shortest reach among `script`'s engaging casts (see engageReachMm), or null when it has none. */
export function engageRangeMm(script: EnemyScript, abilities: Record<string, AbilityDef>): number | null {
  let best: number | null = null;
  for (const step of script) {
    if (step.at === "onDeath") continue;
    const reach = engageReachMm(abilities[step.cast]);
    if (reach !== null && (best === null || reach < best)) best = reach;
  }
  return best;
}

export function chaseCommands(
  state: SimState,
  scripts: Record<ActorId, EnemyScript>,
  abilities: Record<string, AbilityDef>,
): Command[] {
  const out: Command[] = [];
  for (const key of Object.keys(scripts)) {
    const actorId = Number(key);
    const actor = state.actors[actorId];
    const script = scripts[actorId];
    if (!actor || !script || !actor.alive || actor.castId !== null) continue;
    const engage = engageRangeMm(script, abilities);
    if (engage === null) continue;
    const { targetId } = resolveScriptTarget({ state, caster: actor });
    const target = targetId !== undefined ? state.actors[targetId] : undefined;
    if (!target || !target.alive) continue;
    if (rangeMm(actor, target) <= Math.max(0, engage - (actor.chaseMarginMm ?? CHASE_MARGIN_MM))) {
      // ENEMY-ENGAGE: in reach, the chaser still turns to face its target (a zero-direction move only turns).
      const face = yawBetween(actor.pos, target.pos);
      if (face !== actor.yaw) out.push({ tick: state.tick, actorId, kind: "move", dir: { x: 0, z: 0 }, yaw: face });
      continue;
    }
    const delta = { x: target.pos.x - actor.pos.x, y: 0, z: target.pos.z - actor.pos.z };
    const dir = normaliseTo(delta, 1000);
    out.push({
      tick: state.tick,
      actorId,
      kind: "move",
      dir: { x: dir.x, z: dir.z },
      yaw: yawBetween(actor.pos, target.pos),
    });
  }
  return out;
}
