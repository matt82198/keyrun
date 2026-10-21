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
 * A lull: at least one enemy is alive and every living enemy is idle. Mirrors the C# rule in
 * game/bots/Keyrun.Bots/Roles/RoleShared.cs InLull (R-SEED-1a, f-244d102b): an engaged enemy is alive and not
 * idle, whatever it is aimed at, so a heal-only caster tending its own pack is engaged and the tank fights it.
 * Idle is the C#-only prespawn-formation flag (game/sim/Keyrun.Sim/Core/State.cs, BIBLE 9.1). The TS sim has no
 * idle state and never idles an enemy, so no living enemy is idle here and this returns false in every TS state.
 * The tank-pressure lull hold (roles/tank-pressure.ts) therefore never fires on the TS side.
 */
export function inLull(view: BotView): boolean {
  const living = Object.values(view.state.actors).filter((a) => a.kind === "enemy" && a.alive);
  if (living.length === 0) return false;
  return living.every((e) => isIdleEnemy(e));
}

/** The C# Idle flag has no TS twin: the TS sim never idles an enemy. */
function isIdleEnemy(_enemy: ActorState): boolean {
  return false;
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
export function abilityReady(
  self: ActorState,
  abilityId: string,
  tick: Tick,
  kit?: Pick<EffectiveKit, "offGcdAbilityIds" | "chargedAbilities">,
): boolean {
  return !isCastLocked(self, tick) && hasCharge(self, abilityId, tick, kit) && (isOffGcd(kit, abilityId) || self.gcdReadyTick <= tick);
}

/**
 * BANKED-CHARGE (R-PILE-7c, f-f44791bc): at least one charge ready, exactly the sim's chargesAvailable: cooldowns[id] is
 * the tick the recharging charge finishes, so a multi-charge ability in the kit's chargedAbilities has
 * `charges - ceil((cooldowns[id] - tick) / cooldownTicks)` charges banked before it. Any other ability waits for it.
 */
export function hasCharge(self: ActorState, abilityId: string, tick: Tick, kit?: Pick<EffectiveKit, "chargedAbilities">): boolean {
  const nextReady = self.cooldowns[abilityId] ?? 0;
  if (nextReady <= tick) return true;
  const c = kit?.chargedAbilities?.[abilityId];
  if (!c) return false;
  if (c.cooldownTicks <= 0) return true;
  return c.charges - Math.ceil((nextReady - tick) / c.cooldownTicks) >= 1;
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
