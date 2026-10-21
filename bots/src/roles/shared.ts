/**
 * bots/src/roles/shared.ts
 *
 * Helpers shared by every role state machine (tank/healer/damage): cooldown/resource readiness
 * checks, and the kick-ledger command every role attempts first each tick. Split out of the
 * former single-file roles.ts to stay under the repo's <=190 LOC/file cap; no behaviour change.
 *
 * Also the party-wide lull signal (inLull), role-agnostic so any role can read it.
 *
 * Usage: `import { abilityReady, readyAndAffordable, ledgerKickCommand, inLull } from "./shared.js"`
 */
import type { ActorState, BotView, Command, Tick } from "../types.js";
import { resolveInterrupt, isCastLocked } from "../shared/ledger.js";
import { resolveKit, priorityInterruptAbilityIds, type EffectiveKit } from "../kits.js";

/**
 * A lull: at least one enemy is alive and every living enemy is aimed at another enemy (a
 * heal-only caster tending its own pack). Nothing threatens the party. Hollow Vault pull 0 ends
 * on a lone Choir healer, and pull 1's five Paired Warriors spawn the tick pull 0 clears, already
 * aimed at the tank -- so the lull is the party's last chance to prepare (the tank banks its GCD
 * and momentum, roles/tank-pressure.ts; tests/sim/integration/bots-pull1-warden.test.ts). An
 * enemy with no target (not yet engaged) is NOT a lull: the tank must still pull it.
 */
export function inLull(view: BotView): boolean {
  const actors = view.state.actors;
  const living = Object.values(actors).filter((a) => a.kind === "enemy" && a.alive);
  if (living.length === 0) return false;
  return living.every((e) => e.targetId !== null && actors[e.targetId]?.kind === "enemy");
}

/** True when `abilityId`'s own gcdTicks is 0 per the kit (off the GCD, finding f-80b5cf16). */
export function isOffGcd(kit: Pick<EffectiveKit, "offGcdAbilityIds"> | undefined, abilityId: string): boolean {
  return kit?.offGcdAbilityIds?.includes(abilityId) ?? false;
}

/**
 * Not locked out by its own cast (the sim refuses a press then, except in the spell queue window's last ticks, where
 * it holds the press until the cast ends: isCastLocked), off cooldown and off the GCD; an ability in the kit's
 * offGcdAbilityIds only waits for its own cooldown.
 */
export function abilityReady(self: ActorState, abilityId: string, tick: Tick, kit?: Pick<EffectiveKit, "offGcdAbilityIds">): boolean {
  return !isCastLocked(self, tick) && (self.cooldowns[abilityId] ?? 0) <= tick && (isOffGcd(kit, abilityId) || self.gcdReadyTick <= tick);
}

export function affordable(self: ActorState, abilityId: string, kit: EffectiveKit): boolean {
  return self.resource >= (kit.costByAbility[abilityId] ?? 0);
}

export function readyAndAffordable(self: ActorState, abilityId: string, tick: Tick, kit: EffectiveKit): boolean {
  return abilityReady(self, abilityId, tick, kit) && affordable(self, abilityId, kit);
}

/**
 * The kick ledger, shared by all three roles: if it is `self`'s turn to kick the party's
 * priority-cast target, return that cast command and nothing else this tick. Every alive player
 * whose resolved kit uses the same interrupt ability id as `self` is a candidate kicker, matching
 * bots/CLAUDE.md's "the bot with the lowest actorId whose kick is ready takes it" -- content's
 * enemy abilities tagged "healer" (BIBLE.md Sec 9 "The Choir") outrank every other cast.
 */
export function ledgerKickCommand(view: BotView, self: ActorState, kit: EffectiveKit): Command | null {
  const tick = view.state.tick;
  const interruptId = kit.interruptAbilityId;
  if (!interruptId || !self.alive) return null;

  const eligible = Object.values(view.state.actors)
    .filter((a) => a.kind === "player" && a.alive)
    .filter((a) => resolveKit(a.classId, view.classKit).interruptAbilityId === interruptId)
    .map((a) => a.id);

  const assignment = resolveInterrupt(view.state.actors, interruptId, eligible, tick, priorityInterruptAbilityIds(), isOffGcd(kit, interruptId));
  if (!assignment || assignment.kickerId !== self.id) return null;
  return { tick, actorId: self.id, kind: "cast", abilityId: interruptId, targetId: assignment.targetId };
}
