/**
 * bots/src/roles/tank-engage.ts
 *
 * The tank's ENGAGE behaviour (bots/CLAUDE.md: "hold threat"): pick up every loose enemy, walk
 * into melee, face the target, and cast the rotation. Before this file the tank bot never cast a
 * single rotation ability and never walked to the pack: it stood at its spawn point facing away
 * from the party, so it never held threat and the enemies chewed through the cinders first
 * (pull-0 repro, tests/sim/integration/bots-pull0.test.ts).
 *
 * Threat model (sim, not bots): sim/combat/casting.ts adds threat ONLY via explicit
 * `{op:"threat"}` ops -- damage generates none -- and sim/encounter/script-target.ts targets the
 * highest-threat living player that has ANY threat entry, else the nearest. So an enemy is
 * "held" by the tank iff the tank has an entry at least as high as every other entry; a "loose"
 * enemy is one the tank does not hold. Taunt (a threat-op utility) grabs one loose enemy at
 * range; the Warden rotation's own threat ops (Guard Break's cone, Reckoning) grab the rest.
 *
 * Usage: `const cmds = tankEngageCommands(view, self, kit);` (null: nothing to engage)
 */
import type { ActorId, ActorState, BotView, Command } from "../types.js";
import type { EffectiveKit } from "../kits.js";
import { getContentRegistry } from "../kits.js";
import { pickTankTarget } from "../shared/priority.js";
import { moveToward, yawToward, yawChanged } from "../shared/command.js";
import { pickResourceAwareRotation } from "./damage.js";
import { abilityReady } from "./shared.js";
import { pressureRotation } from "./tank-pressure.js";
import { arrangeFillers } from "./fillers.js";

/** Fallback melee reach when no registry is injected (Warden's 3000mm swing). */
const DEFAULT_REACH_MM = 3000;
/** Walk this far inside reach so collision push-out cannot nudge the tank back out of range. */
const REACH_MARGIN_MM = 500;

function dist(a: ActorState, b: ActorState): number {
  return Math.hypot(a.pos.x - b.pos.x, a.pos.z - b.pos.z);
}

function rangeOf(abilityId: string | null): number | null {
  if (!abilityId) return null;
  return getContentRegistry()?.abilities.get(abilityId)?.range ?? null;
}

/** Shortest positive range among the rotation's abilities: the distance the tank must close to. */
function reachMm(kit: EffectiveKit): number {
  const ranges = kit.rotation.map(rangeOf).filter((r): r is number => r !== null && r > 0);
  return ranges.length > 0 ? Math.min(...ranges) : DEFAULT_REACH_MM;
}

/** True if `tankId` holds `enemy`: it has a threat entry at least as high as every other one. */
export function holdsThreat(enemy: ActorState, tankId: ActorId): boolean {
  const mine = enemy.threat[tankId];
  if (mine === undefined) return false;
  return Object.values(enemy.threat).every((v) => v <= mine);
}

/** True if `enemy` is currently aimed at a player other than the tank (a heal-only caster aimed at
 * its own ally, or an enemy aimed at the tank, is not peeling anyone). */
function aimedAtOtherPlayer(actors: Record<ActorId, ActorState>, enemy: ActorState, tankId: ActorId): boolean {
  if (enemy.targetId === null || enemy.targetId === tankId) return false;
  return actors[enemy.targetId]?.kind === "player";
}

/** Nearest alive enemy the tank does not hold that is aimed at another player, tie-broken by
 * lowest actorId; null if none. */
export function pickLooseEnemy(actors: Record<ActorId, ActorState>, tank: ActorState): ActorState | null {
  const loose = Object.values(actors)
    .filter((a) => a.kind === "enemy" && a.alive && !holdsThreat(a, tank.id) && aimedAtOtherPlayer(actors, a, tank.id))
    .sort((a, b) => dist(a, tank) - dist(b, tank) || a.id - b.id);
  return loose[0] ?? null;
}

export function tankEngageCommands(view: BotView, self: ActorState, kit: EffectiveKit): Command[] | null {
  const tick = view.state.tick;
  const actors = view.state.actors;
  const loose = pickLooseEnemy(actors, self);
  const current = self.targetId !== null ? actors[self.targetId] : undefined;
  const fallbackId = pickTankTarget(actors, self.id);
  const target = loose ?? (current && current.alive && current.kind === "enemy" ? current : fallbackId !== null ? actors[fallbackId] : undefined);
  if (!target) return null;

  const out: Command[] = [];
  if (self.targetId !== target.id) out.push({ tick, actorId: self.id, kind: "target", targetId: target.id });
  const d = dist(self, target);

  const tauntRange = rangeOf(kit.tauntAbilityId) ?? DEFAULT_REACH_MM;
  if (loose && kit.tauntAbilityId && d <= tauntRange && abilityReady(self, kit.tauntAbilityId, tick)) {
    out.push({ tick, actorId: self.id, kind: "cast", abilityId: kit.tauntAbilityId, targetId: target.id });
    return out;
  }

  if (d > reachMm(kit) - REACH_MARGIN_MM) {
    if (self.castId !== null) return out; // never walk out of our own cast
    const move = moveToward(tick, self.id, self.pos, target.pos);
    if (move) out.push(move);
    return out;
  }

  const desiredYaw = yawToward(self.pos, target.pos); // cones (Guard Break) resolve on current facing
  if (yawChanged(self.yaw, desiredYaw)) out.push({ tick, actorId: self.id, kind: "move", dir: { x: 0, z: 0 }, yaw: desiredYaw });
  // The self-sustain spender (Vigil) is gated by tank.ts, never spent as rotation; under pack
  // pressure the rotation also holds enough momentum for it (tank-pressure.ts).
  const rotationKit = { ...kit, rotation: arrangeFillers(view, self, kit, pressureRotation(view, self, kit), target.id) };
  const abilityId = pickResourceAwareRotation(rotationKit, self, tick);
  if (abilityId) out.push({ tick, actorId: self.id, kind: "cast", abilityId, targetId: target.id });
  return out;
}
