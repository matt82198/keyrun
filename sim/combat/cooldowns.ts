/**
 * GCD and charge-based cooldown tracking. Charges use the "chained recharge timestamp"
 * technique: cooldowns[abilityId] stores the tick at which the currently-recharging charge
 * finishes; consuming an additional charge while below full pushes that timestamp forward by
 * cooldownTicks, chaining recharges. This fits the shared ActorState.cooldowns: Record<string,
 * Tick> shape without needing a separate per-ability charge counter.
 * Usage: if (isAbilityReady(actor, def, now)) { consumeCharge(actor, def, now); ... }
 */
import type { ActorState, Tick } from "./types";
import type { AbilityDef } from "./abilities";

export function isGcdReady(actor: ActorState, now: Tick): boolean {
  return now >= actor.gcdReadyTick;
}

export function startGcd(actor: ActorState, now: Tick, gcdTicks: Tick): void {
  if (gcdTicks <= 0) return;
  actor.gcdReadyTick = now + gcdTicks;
}

export function chargesAvailable(actor: ActorState, def: AbilityDef, now: Tick): number {
  if (def.cooldownTicks === 0) return def.charges;
  const nextReady = actor.cooldowns[def.id] ?? 0;
  if (now >= nextReady) return def.charges;
  const ticksRemaining = nextReady - now;
  const chargesOnCooldown = Math.ceil(ticksRemaining / def.cooldownTicks);
  return Math.max(0, def.charges - chargesOnCooldown);
}

export function consumeCharge(actor: ActorState, def: AbilityDef, now: Tick): void {
  if (def.cooldownTicks === 0) return;
  const nextReady = actor.cooldowns[def.id] ?? 0;
  const base = now >= nextReady ? now : nextReady;
  actor.cooldowns[def.id] = base + def.cooldownTicks;
}

/**
 * An ability whose own gcdTicks is 0 is off the GCD (finding f-80b5cf16): it never waits for it, as startGcd never
 * starts one for it, so a gcdTicks-0 kick lands on the tick it is pressed even while a rotation press holds the GCD.
 */
export function isAbilityReady(actor: ActorState, def: AbilityDef, now: Tick): boolean {
  return (def.gcdTicks <= 0 || isGcdReady(actor, now)) && chargesAvailable(actor, def, now) >= 1;
}
