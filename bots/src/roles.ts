/**
 * bots/src/roles.ts
 *
 * The three role state machines (tank/healer/damage) that bot.ts dispatches
 * to once a decision window opens. Pure functions: (view, self) -> Command[].
 *
 * Contract note: dispel detection uses the local convention that a
 * dispellable effect's effectId is prefixed "dispellable:" -- the shared
 * ActorState/Telegraph contract carries no explicit dispel flag.
 *
 * Usage: `import { tankDecide, healerDecide, damageDecide } from "./roles.js"`
 */
import type { ActorState, BotView, Command, Tick } from "./types.js";
import {
  pickTankTarget,
  pickHealTarget,
  pickDispelTarget,
  pickRotationAbility,
  isInterruptResponsible,
} from "./shared/priority.js";

const DISPELLABLE_PREFIX = "dispellable:";
const HEAL_RANGE_M = 30;
const HEAL_THRESHOLD_PCT = 0.7;
const TANK_DEFENSIVE_THRESHOLD_PCT = 0.35;

export function abilityReady(self: ActorState, abilityId: string, tick: Tick): boolean {
  return (self.cooldowns[abilityId] ?? 0) <= tick && self.gcdReadyTick <= tick;
}

export function tankDecide(view: BotView, self: ActorState): Command[] {
  const tick = view.state.tick;
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
  const def = view.classKit.defensiveAbilityId;
  if (def && self.health / self.maxHealth < TANK_DEFENSIVE_THRESHOLD_PCT && abilityReady(self, def, tick)) {
    out.push({ tick, actorId: self.id, kind: "cast", abilityId: def });
  }
  return out;
}

export function healerDecide(view: BotView, self: ActorState): Command[] {
  const tick = view.state.tick;
  const dispellable = new Set(
    Object.values(view.state.actors)
      .flatMap((a) => a.effects)
      .map((e) => e.effectId)
      .filter((id) => id.startsWith(DISPELLABLE_PREFIX))
  );
  let targetId = dispellable.size > 0 ? pickDispelTarget(view.state.actors, dispellable) : null;
  let abilityId: string | null = targetId !== null ? "dispel" : null;
  if (targetId === null) {
    targetId = pickHealTarget(view.state.actors, HEAL_THRESHOLD_PCT);
    abilityId = view.classKit.healAbilityId;
  }
  if (targetId === null || !abilityId) return [];
  const target = view.state.actors[targetId];
  if (!target) return [];
  const dx = target.pos.x - self.pos.x;
  const dz = target.pos.z - self.pos.z;
  const dist = Math.hypot(dx, dz);
  if (dist > HEAL_RANGE_M) {
    const len = dist || 1;
    return [{ tick, actorId: self.id, kind: "move", dir: { x: dx / len, z: dz / len }, yaw: Math.atan2(dz, dx) }];
  }
  if (abilityReady(self, abilityId, tick)) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId, targetId }];
  }
  return [];
}

export function damageDecide(view: BotView, self: ActorState): Command[] {
  const tick = view.state.tick;
  const interruptId = view.classKit.interruptAbilityId;
  if (interruptId) {
    const casting = Object.values(view.state.actors).find((a) => a.kind === "enemy" && a.alive && a.castId !== null);
    if (casting) {
      const eligible = Object.values(view.state.actors)
        .filter((a) => a.kind === "player" && a.alive && a.role === "damage")
        .filter((a) => (a.cooldowns[interruptId] ?? 0) <= tick && a.gcdReadyTick <= tick)
        .map((a) => a.id);
      if (abilityReady(self, interruptId, tick) && isInterruptResponsible(self.id, eligible)) {
        return [{ tick, actorId: self.id, kind: "cast", abilityId: interruptId, targetId: casting.id }];
      }
    }
  }
  const abilityId = pickRotationAbility(view.classKit.rotation, (id) => abilityReady(self, id, tick));
  if (abilityId) {
    const cmd: Command = { tick, actorId: self.id, kind: "cast", abilityId };
    if (self.targetId !== null) cmd.targetId = self.targetId;
    return [cmd];
  }
  return [];
}
