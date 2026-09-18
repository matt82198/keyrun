/**
 * bots/src/roles.ts
 *
 * The three role state machines (tank/healer/damage) that bot.ts dispatches to once a decision
 * window opens. Pure functions: (view, self) -> Command[]. Each now plays the real class kit
 * (bots/src/kits.ts, built from content/'s ClassDef) rather than a hand-authored priority list:
 * cooldown- and resource-aware rotation, Setup/Payoff Window play (bots/src/shared/windows.ts),
 * and the kick ledger (bots/src/shared/ledger.ts), all sharing one priority order per role:
 * kick the priority cast > Payoff an open Window > Setup one for a nearby ally > role logic.
 *
 * Contract note: dispel detection uses the local convention that a dispellable effect's effectId
 * is prefixed "dispellable:" -- the shared ActorState/Telegraph contract carries no explicit
 * dispel flag.
 *
 * Usage: `import { tankDecide, healerDecide, damageDecide } from "./roles.js"`
 */
import type { ActorId, ActorState, BotView, Command, Tick } from "./types.js";
import { pickTankTarget, pickHealTarget, pickDispelTarget, pickRotationAbility } from "./shared/priority.js";
import { findClosableWindow, openWindowsOn } from "./shared/windows.js";
import { resolveInterrupt } from "./shared/ledger.js";
import { resolveKit, priorityInterruptAbilityIds, type EffectiveKit } from "./kits.js";

const DISPELLABLE_PREFIX = "dispellable:";
const HEAL_RANGE_M = 30;
const HEAL_THRESHOLD_PCT = 0.7;
const TANK_DEFENSIVE_THRESHOLD_PCT = 0.35;
/** A higher, earlier floor for a self-sustain spender (Warden's Vigil) ahead of the harder defensive (Brace). */
const TANK_SUSTAIN_THRESHOLD_PCT = 0.6;
/** How close a Payoff-capable ally must be for a Setup to be worth opening (design/BIBLE.md Sec 4: "a timing game between two people"). */
const PAYOFF_ALLY_RANGE_M = 30;
/**
 * The party's missing-health ratio (design/BIBLE.md Sec 5 Chanter: "swaps verse when the party's
 * missing health crosses a threshold") that triggers a stance swap into Mend if not already there.
 */
const PARTY_MEND_THRESHOLD_PCT = 0.35;
/**
 * Local convention (sim doesn't implement the verse resource yet -- ClassLoop{resource:"verse",
 * max:1}): resource === 1 is Mend, 0 is Ward. Documented here rather than silently assumed.
 */
const MEND_VERSE_VALUE = 1;

export function abilityReady(self: ActorState, abilityId: string, tick: Tick): boolean {
  return (self.cooldowns[abilityId] ?? 0) <= tick && self.gcdReadyTick <= tick;
}

function affordable(self: ActorState, abilityId: string, kit: EffectiveKit): boolean {
  return self.resource >= (kit.costByAbility[abilityId] ?? 0);
}

function readyAndAffordable(self: ActorState, abilityId: string, tick: Tick, kit: EffectiveKit): boolean {
  return abilityReady(self, abilityId, tick) && affordable(self, abilityId, kit);
}

function aliveEnemies(actors: Record<ActorId, ActorState>): ActorState[] {
  return Object.values(actors).filter((a) => a.kind === "enemy" && a.alive);
}

/** Lowest-actorId alive enemy, for a Setup cast that needs *a* target but has none yet. */
function pickAnyEnemyTarget(actors: Record<ActorId, ActorState>): ActorId | null {
  const enemies = aliveEnemies(actors).sort((a, b) => a.id - b.id);
  return enemies[0]?.id ?? null;
}

/**
 * The kick ledger, shared by all three roles: if it is `self`'s turn to kick the party's
 * priority-cast target, return that cast command and nothing else this tick. Every alive player
 * whose resolved kit uses the same interrupt ability id as `self` is a candidate kicker, matching
 * bots/CLAUDE.md's "the bot with the lowest actorId whose kick is ready takes it" -- content's
 * enemy abilities tagged "healer" (BIBLE.md Sec 9 "The Choir") outrank every other cast.
 */
function ledgerKickCommand(view: BotView, self: ActorState, kit: EffectiveKit): Command | null {
  const tick = view.state.tick;
  const interruptId = kit.interruptAbilityId;
  if (!interruptId || !self.alive) return null;

  const eligible = Object.values(view.state.actors)
    .filter((a) => a.kind === "player" && a.alive)
    .filter((a) => resolveKit(a.classId, view.classKit).interruptAbilityId === interruptId)
    .map((a) => a.id);

  const assignment = resolveInterrupt(view.state.actors, interruptId, eligible, tick, priorityInterruptAbilityIds());
  if (!assignment || assignment.kickerId !== self.id) return null;
  return { tick, actorId: self.id, kind: "cast", abilityId: interruptId, targetId: assignment.targetId };
}

export function tankDecide(view: BotView, self: ActorState): Command[] {
  const tick = view.state.tick;
  const kit = resolveKit(self.classId, view.classKit);

  const kick = ledgerKickCommand(view, self, kit);
  if (kick) return [kick];

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

function partyMissingHealthRatio(actors: Record<ActorId, ActorState>): number {
  const allies = Object.values(actors).filter((a) => a.kind === "player" && a.alive);
  if (allies.length === 0) return 0;
  const totalMax = allies.reduce((s, a) => s + a.maxHealth, 0);
  if (totalMax <= 0) return 0;
  const totalMissing = allies.reduce((s, a) => s + Math.max(0, a.maxHealth - a.health), 0);
  return totalMissing / totalMax;
}

export function healerDecide(view: BotView, self: ActorState): Command[] {
  const tick = view.state.tick;
  const kit = resolveKit(self.classId, view.classKit);

  const kick = ledgerKickCommand(view, self, kit);
  if (kick) return [kick];

  if (
    kit.loopKind === "stance" &&
    kit.stanceSwapAbilityId &&
    self.resource !== MEND_VERSE_VALUE &&
    partyMissingHealthRatio(view.state.actors) >= PARTY_MEND_THRESHOLD_PCT &&
    readyAndAffordable(self, kit.stanceSwapAbilityId, tick, kit)
  ) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: kit.stanceSwapAbilityId }];
  }

  const dispellable = new Set(
    Object.values(view.state.actors)
      .flatMap((a) => a.effects)
      .map((e) => e.effectId)
      .filter((id) => id.startsWith(DISPELLABLE_PREFIX))
  );
  let targetId = dispellable.size > 0 ? pickDispelTarget(view.state.actors, dispellable) : null;
  let abilityId: string | null = targetId !== null ? kit.dispelAbilityId ?? "dispel" : null;
  if (targetId === null) {
    targetId = pickHealTarget(view.state.actors, HEAL_THRESHOLD_PCT);
    abilityId = kit.healAbilityId;
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
  if (readyAndAffordable(self, abilityId, tick, kit)) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId, targetId }];
  }
  return [];
}

/**
 * A Payoff cast on the best open Window this class can close (never the class's own Window --
 * shared/windows.ts's findClosableWindow() already enforces the Lock). Targets and casts in one
 * go; empty if no Payoff is ready/affordable or no closable Window is open.
 */
function payoffOnWindowCommand(view: BotView, self: ActorState, kit: EffectiveKit): Command[] {
  const tick = view.state.tick;
  if (kit.payoffAbilityIds.length === 0) return [];
  const win = findClosableWindow(view.state.actors, tick, self.classId);
  if (!win) return [];
  const payoffId = kit.payoffAbilityIds.find((id) => readyAndAffordable(self, id, tick, kit));
  if (!payoffId) return [];
  const commands: Command[] = [];
  if (self.targetId !== win.targetId) commands.push({ tick, actorId: self.id, kind: "target", targetId: win.targetId });
  commands.push({ tick, actorId: self.id, kind: "cast", abilityId: payoffId, targetId: win.targetId });
  return commands;
}

/** True if a party member of a *different* class, with a Payoff of its own, is within PAYOFF_ALLY_RANGE_M. */
function payoffAllyInRange(view: BotView, self: ActorState): boolean {
  return Object.values(view.state.actors).some((a) => {
    if (a.kind !== "player" || !a.alive || a.id === self.id || a.classId === self.classId) return false;
    const theirKit = resolveKit(a.classId, view.classKit);
    if (theirKit.payoffAbilityIds.length === 0) return false;
    const dist = Math.hypot(a.pos.x - self.pos.x, a.pos.z - self.pos.z);
    return dist <= PAYOFF_ALLY_RANGE_M;
  });
}

/**
 * A Setup cast to open a fresh Window, only when no Window is currently open at all and a
 * Payoff-capable ally is close enough to plausibly close it (design/BIBLE.md Sec 4).
 */
function setupForAllyCommand(view: BotView, self: ActorState, kit: EffectiveKit): Command[] {
  const tick = view.state.tick;
  if (kit.setupAbilityIds.length === 0) return [];
  if (openWindowsOn(view.state.actors, tick).length > 0) return [];
  if (!payoffAllyInRange(view, self)) return [];
  const setupId = kit.setupAbilityIds.find((id) => readyAndAffordable(self, id, tick, kit));
  if (!setupId) return [];
  const targetId = self.targetId ?? pickAnyEnemyTarget(view.state.actors);
  if (targetId === null) return [];
  const commands: Command[] = [];
  if (self.targetId !== targetId) commands.push({ tick, actorId: self.id, kind: "target", targetId });
  commands.push({ tick, actorId: self.id, kind: "cast", abilityId: setupId, targetId });
  return commands;
}

/**
 * Cooldown- and resource-aware pick from the kit's base rotation priority (kits.ts). For a
 * "heat-overheat" loop (Cinder), never lets a builder push the resource to/over its cap when a
 * controlled dump (a ready, affordable Payoff that drains the resource, e.g. Cold Snap) is
 * available -- riding into a forced Overheat/Chill is only acceptable when no dump exists.
 */
function pickResourceAwareRotation(kit: EffectiveKit, self: ActorState, tick: Tick): string | null {
  const ready = (id: string) => readyAndAffordable(self, id, tick, kit);
  const picked = pickRotationAbility(kit.rotation, ready);
  if (!picked) return null;
  if (kit.loopKind !== "heat-overheat") return picked;

  const gain = kit.resourceGainByAbility[picked] ?? 0;
  const max = kit.resourceMax ?? Number.POSITIVE_INFINITY;
  if (gain <= 0 || self.resource + gain < max) return picked;

  const dump = kit.resourceDumpAbilityIds.find((id) => ready(id));
  return dump ?? picked;
}

export function damageDecide(view: BotView, self: ActorState): Command[] {
  const tick = view.state.tick;
  const kit = resolveKit(self.classId, view.classKit);

  const kick = ledgerKickCommand(view, self, kit);
  if (kick) return [kick];

  const payoff = payoffOnWindowCommand(view, self, kit);
  if (payoff.length > 0) return payoff;

  const setup = setupForAllyCommand(view, self, kit);
  if (setup.length > 0) return setup;

  const abilityId = pickResourceAwareRotation(kit, self, tick);
  if (abilityId) {
    const cmd: Command = { tick, actorId: self.id, kind: "cast", abilityId };
    if (self.targetId !== null) cmd.targetId = self.targetId;
    return [cmd];
  }
  return [];
}

export { pickResourceAwareRotation };
