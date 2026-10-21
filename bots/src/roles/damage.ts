/**
 * bots/src/roles/damage.ts
 *
 * The damage state machine (bots/CLAUDE.md: "rotation priority list from the class definition,
 * kick the priority cast, move out of telegraphs"): kick the priority cast, prefer a Payoff on
 * another class's open Window (bots/src/shared/windows.ts, design/BIBLE.md Sec 4/9), prefer a
 * Setup when no Window is open and a Payoff-capable ally is in range, otherwise cycle the
 * cooldown- and resource-aware base rotation (bots/src/kits.ts). Flees a hostile floor patch
 * (shared/floor.ts) ahead of all of the above; when the rotation itself has nothing ready, falls
 * back to a melee bot re-approaching its target, then to walking into a friendly floor Window this
 * class can close. Pure: (view, self) -> Command[].
 *
 * Usage: `import { damageDecide, pickResourceAwareRotation } from "./damage.js"`
 */
import type { ActorId, ActorState, BotView, Command, Tick } from "../types.js";
import { pickRotationAbility } from "../shared/priority.js";
import { findClosableWindow, openWindowsOn } from "../shared/windows.js";
import { floorEscapeCommand, meleeReapproachCommand, friendlyWindowApproachCommand } from "../shared/floor.js";
import { resolveKit, type EffectiveKit } from "../kits.js";
import { readyAndAffordable, ledgerKickCommand } from "./shared.js";

/** How close a Payoff-capable ally must be for a Setup to be worth opening (design/BIBLE.md Sec 4:
 * "a timing game between two people"). ActorState.pos is integer mm (sim/CLAUDE.md), so this 30m
 * design constant is converted to mm once, here, never by re-scaling an already-mm position. */
export const PAYOFF_ALLY_RANGE_MM = 30 * 1000;

function aliveEnemies(actors: Record<ActorId, ActorState>): ActorState[] {
  return Object.values(actors).filter((a) => a.kind === "enemy" && a.alive);
}

/** Lowest-actorId alive enemy, for a Setup cast that needs *a* target but has none yet. */
function pickAnyEnemyTarget(actors: Record<ActorId, ActorState>): ActorId | null {
  const enemies = aliveEnemies(actors).sort((a, b) => a.id - b.id);
  return enemies[0]?.id ?? null;
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
    return dist <= PAYOFF_ALLY_RANGE_MM;
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
 * Cooldown- and resource-aware pick from the kit's base rotation priority (bots/src/kits.ts). For
 * a "heat-overheat" loop (Cinder), never lets a builder push the resource to/over its cap when a
 * controlled dump (a ready, affordable Payoff that drains the resource, e.g. Cold Snap) is
 * available -- riding into a forced Overheat/Chill is only acceptable when no dump exists.
 */
export function pickResourceAwareRotation(kit: EffectiveKit, self: ActorState, tick: Tick): string | null {
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

  const escape = floorEscapeCommand(view, self);
  if (escape) return [escape];

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

  const reapproach = meleeReapproachCommand(view, self);
  if (reapproach) return [reapproach];
  const approach = friendlyWindowApproachCommand(view, self);
  if (approach) return [approach];
  return [];
}
