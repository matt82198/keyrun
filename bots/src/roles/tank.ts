/**
 * bots/src/roles/tank.ts
 *
 * The tank state machine (bots/CLAUDE.md: "hold threat, face away, use defensives at health
 * thresholds"): kick the priority cast, acquire the highest-threat-deficit enemy, face away from
 * the party centroid, and pop a self-sustain spender (Warden's Vigil) under a higher health floor
 * than its harder defensive (Brace). Also the tank's floor-patch avoidance/re-approach
 * (shared/floor.ts): a hostile floor patch under the tank preempts everything below it here, and
 * once clear, a tank out of melee range of its target walks back in before resuming its usual
 * facing/defensive logic. Pure: (view, self) -> Command[].
 *
 * Usage: `import { tankDecide } from "./tank.js"`
 */
import type { ActorState, BotView, Command } from "../types.js";
import { pickTankTarget } from "../shared/priority.js";
import { resolveKit } from "../kits.js";
import { floorEscapeCommand, meleeReapproachCommand } from "../shared/floor.js";
import { abilityReady, readyAndAffordable, ledgerKickCommand } from "./shared.js";

const TANK_DEFENSIVE_THRESHOLD_PCT = 0.35;
/** A higher, earlier floor for a self-sustain spender (Warden's Vigil) ahead of the harder defensive (Brace). */
const TANK_SUSTAIN_THRESHOLD_PCT = 0.6;

export function tankDecide(view: BotView, self: ActorState): Command[] {
  const tick = view.state.tick;
  const kit = resolveKit(self.classId, view.classKit);

  const kick = ledgerKickCommand(view, self, kit);
  if (kick) return [kick];

  const escape = floorEscapeCommand(view, self);
  if (escape) return [escape];
  const reapproach = meleeReapproachCommand(view, self);
  if (reapproach) return [reapproach];

  const out: Command[] = [];
  const targetId = pickTankTarget(view.state.actors, self.id);
  if (targetId !== null && self.targetId !== targetId) {
    out.push({ tick, actorId: self.id, kind: "target", targetId });
  }
  const party = Object.values(view.state.actors).filter((a) => a.kind === "player" && a.alive && a.id !== self.id);
  if (party.length > 0) {
    const cx = party.reduce((s, a) => s + a.pos.x, 0) / party.length;
    const cz = party.reduce((s, a) => s + a.pos.z, 0) / party.length;
    const dx = self.pos.x - cx;
    const dz = self.pos.z - cz;
    if (dx !== 0 || dz !== 0) {
      const yaw = Math.atan2(dz, dx);
      if (Math.abs(yaw - self.yaw) > 0.01) {
        out.push({ tick, actorId: self.id, kind: "move", dir: { x: 0, z: 0 }, yaw });
      }
    }
  }

  const healthPct = self.health / self.maxHealth;
  if (kit.defensiveAbilityId && healthPct < TANK_DEFENSIVE_THRESHOLD_PCT && abilityReady(self, kit.defensiveAbilityId, tick)) {
    out.push({ tick, actorId: self.id, kind: "cast", abilityId: kit.defensiveAbilityId });
  } else if (
    kit.selfSustainAbilityId &&
    healthPct < TANK_SUSTAIN_THRESHOLD_PCT &&
    readyAndAffordable(self, kit.selfSustainAbilityId, tick, kit)
  ) {
    out.push({ tick, actorId: self.id, kind: "cast", abilityId: kit.selfSustainAbilityId });
  }
  return out;
}
