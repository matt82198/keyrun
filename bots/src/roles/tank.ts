/**
 * bots/src/roles/tank.ts
 *
 * The tank state machine (bots/CLAUDE.md: "hold threat, face away, use defensives at health
 * thresholds"): kick the priority cast, then ENGAGE (roles/tank-engage.ts: taunt loose enemies,
 * walk into melee, face the target, cast the rotation), and pop a self-sustain spender (Warden's
 * Vigil) under a higher health floor than its harder defensive (Brace). With no enemy to engage
 * it faces away from the party centroid. Also the tank's floor-patch avoidance/re-approach
 * (shared/floor.ts): a hostile floor patch under the tank preempts everything below it here, and
 * once clear, a tank out of melee range of its target walks back in. Pure: (view, self) -> Command[].
 *
 * Pack pressure: the hard defensive also fires, at any health, once PRESSURE_ATTACKERS enemies are
 * aimed at the tank. Hollow Vault's pull 1 (five Paired Warriors, tests/sim/integration/
 * bots-pull1.test.ts) swings in sync: ~350 damage lands in one tick on a 400-HP Warden, so health
 * jumped from above the 35% floor straight to 0 and a floor-only Brace was never cast. Under the
 * same pressure the tank also holds momentum for Vigil and casts it as Brace runs out
 * (roles/tank-pressure.ts), instead of dumping momentum into Reckoning.
 *
 * Usage: `import { tankDecide } from "./tank.js"`
 */
import type { ActorState, BotView, Command } from "../types.js";
import { pickTankTarget } from "../shared/priority.js";
import { resolveKit, type EffectiveKit } from "../kits.js";
import { floorEscapeCommand, meleeReapproachCommand } from "../shared/floor.js";
import { yawToward, yawChanged } from "../shared/command.js";
import { abilityReady, readyAndAffordable, ledgerKickCommand } from "./shared.js";
import { tankEngageCommands } from "./tank-engage.js";
import { underPackPressure, pressureSustainCommand } from "./tank-pressure.js";

const TANK_DEFENSIVE_THRESHOLD_PCT = 0.35;
/** A higher, earlier floor for a self-sustain spender (Warden's Vigil) ahead of the harder defensive (Brace). */
const TANK_SUSTAIN_THRESHOLD_PCT = 0.6;

export { PRESSURE_ATTACKERS } from "./tank-pressure.js";

function defensiveCommands(view: BotView, self: ActorState, kit: EffectiveKit): Command[] {
  const tick = view.state.tick;
  const healthPct = self.health / self.maxHealth;
  const pressured = healthPct < TANK_DEFENSIVE_THRESHOLD_PCT || underPackPressure(view, self);
  if (kit.defensiveAbilityId && pressured && abilityReady(self, kit.defensiveAbilityId, tick)) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: kit.defensiveAbilityId }];
  }
  // Pack pressure: Vigil covers Brace's expiry instead of waiting for the 60% floor (tank-pressure.ts).
  const vigil = pressureSustainCommand(view, self, kit);
  if (vigil) return [vigil];
  if (
    kit.selfSustainAbilityId &&
    healthPct < TANK_SUSTAIN_THRESHOLD_PCT &&
    readyAndAffordable(self, kit.selfSustainAbilityId, tick, kit)
  ) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: kit.selfSustainAbilityId }];
  }
  return [];
}

export function tankDecide(view: BotView, self: ActorState): Command[] {
  const tick = view.state.tick;
  const kit = resolveKit(self.classId, view.classKit);

  const kick = ledgerKickCommand(view, self, kit);
  if (kick) return [kick];

  const escape = floorEscapeCommand(view, self);
  if (escape) return [escape];
  const reapproach = meleeReapproachCommand(view, self);
  if (reapproach) return [reapproach];

  // Defensives first: sim gates even a gcd-0 cast on the GCD, so it must precede a rotation cast.
  const defensive = defensiveCommands(view, self, kit);
  const engage = tankEngageCommands(view, self, kit);
  if (engage) return [...defensive, ...engage];

  const out: Command[] = [];
  const targetId = pickTankTarget(view.state.actors, self.id);
  if (targetId !== null && self.targetId !== targetId) {
    out.push({ tick, actorId: self.id, kind: "target", targetId });
  }
  const party = Object.values(view.state.actors).filter((a) => a.kind === "player" && a.alive && a.id !== self.id);
  if (party.length > 0) {
    // Centroid rounded to an integer mm point (sim/CLAUDE.md: integer positions) -- an average of
    // integers is not itself guaranteed integer.
    const cx = Math.round(party.reduce((s, a) => s + a.pos.x, 0) / party.length);
    const cz = Math.round(party.reduce((s, a) => s + a.pos.z, 0) / party.length);
    if (self.pos.x !== cx || self.pos.z !== cz) {
      const desiredYaw = yawToward({ x: cx, z: cz }, self.pos); // faces away from the party centroid
      if (yawChanged(self.yaw, desiredYaw)) {
        out.push({ tick, actorId: self.id, kind: "move", dir: { x: 0, z: 0 }, yaw: desiredYaw });
      }
    }
  }
  return [...out, ...defensive];
}
