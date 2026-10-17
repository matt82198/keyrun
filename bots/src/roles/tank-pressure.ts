/**
 * bots/src/roles/tank-pressure.ts
 *
 * The tank's pack-pressure signal and its pressure-aware momentum spend. "Under pressure" means
 * PRESSURE_ATTACKERS or more living enemies are aimed at the tank (the #87 signal that first gated
 * Brace). Hollow Vault's pull 1 (five synced Paired Warriors, tests/sim/integration/
 * bots-pull1.test.ts) lands ~350 damage in one tick on a 400-HP Warden: the tank sat at 310 HP
 * holding 100 momentum, spent it on Reckoning (kit-priority.ts puts cost-paying finishers first),
 * and once Brace's 60 ticks ran out nothing mitigated the next burst.
 *
 * Under pressure the tank therefore:
 *   - casts its self-sustain spender (Warden's Vigil) when the hard defensive's (Brace's) effect
 *     is down or has at most BRACE_EXPIRY_LEAD_TICKS left, and Vigil's own effect is not up;
 *   - holds momentum for Vigil: no rotation ability whose cost would leave less than Vigil's cost
 *     is spent (Reckoning's 100 vs Vigil's 50 on a 100 cap -> Reckoning waits for the pull to thin).
 * With no pressure the rotation is unchanged and Reckoning stays the finisher.
 *
 * Under pressure the tank also keeps its group mitigation (Warden's Bulwark: a circle that applies
 * Braced, -15% damage taken, 160 of every 200 ticks) up whenever its effect is down. Bulwark is a
 * rotation Setup, and kit-priority.ts ranks Payoffs above Setups, so Guard Break (a cd-0 Payoff,
 * ready every GCD) starved it: the tank never cast Bulwark once in pull 0 or pull 1. Without it the
 * pull-1 opener survived on a ~10 HP margin, and sim #94 (dead actors inert) perturbing pull 0 by a
 * few ticks was enough to wipe the party (the tank died at t1295 to the third synced swing).
 *
 * Usage: `import { underPackPressure, pressureSustainCommand, pressureMitigationCommand, pressureRotation } from "./tank-pressure.js"`
 */
import type { ActorState, BotView, Command, Tick } from "../types.js";
import { getContentRegistry, type EffectiveKit } from "../kits.js";
import { readyAndAffordable } from "./shared.js";

/** Enemies aimed at the tank at which it counts as under pack pressure. */
export const PRESSURE_ATTACKERS = 3;
/** Cast Vigil once Brace's effect has this many ticks or fewer left (one GCD: Vigil is on the GCD). */
export const BRACE_EXPIRY_LEAD_TICKS = 30;

export function attackersOf(view: BotView, self: ActorState): number {
  return Object.values(view.state.actors).filter((a) => a.kind === "enemy" && a.alive && a.targetId === self.id).length;
}

export function underPackPressure(view: BotView, self: ActorState): boolean {
  return attackersOf(view, self) >= PRESSURE_ATTACKERS;
}

/** Effect ids an ability applies to its caster/targets (from the injected content registry). */
function appliedEffectIds(abilityId: string): string[] {
  const def = getContentRegistry()?.abilities.get(abilityId);
  if (!def) return [];
  return def.effects.flatMap((e) => (e.op === "applyEffect" ? [e.effectId] : []));
}

/** Ticks left on the longest of `abilityId`'s applied effects on `self` (0 if none is up). */
function effectTicksLeft(self: ActorState, abilityId: string, tick: Tick): number {
  const ids = new Set(appliedEffectIds(abilityId));
  return self.effects.filter((e) => ids.has(e.effectId)).reduce((m, e) => Math.max(m, e.endsTick - tick), 0);
}

/** Under pressure: Vigil when Brace is down or about to expire and Vigil is not already up. */
export function pressureSustainCommand(view: BotView, self: ActorState, kit: EffectiveKit): Command | null {
  const tick = view.state.tick;
  const sustain = kit.selfSustainAbilityId;
  if (!sustain || !underPackPressure(view, self)) return null;
  if (!readyAndAffordable(self, sustain, tick, kit)) return null;
  if (effectTicksLeft(self, sustain, tick) > 0) return null;
  const braceLeft = kit.defensiveAbilityId ? effectTicksLeft(self, kit.defensiveAbilityId, tick) : 0;
  if (braceLeft > BRACE_EXPIRY_LEAD_TICKS) return null;
  return { tick, actorId: self.id, kind: "cast", abilityId: sustain };
}

/**
 * The kit's group mitigation: the first rotational circle-shaped ability that applies an effect
 * with a negative damageTakenBp (Warden's Bulwark -> Braced), read from the injected content
 * registry. Null without a registry or for a class that has none.
 */
export function groupMitigationAbilityId(kit: EffectiveKit): string | null {
  const reg = getContentRegistry();
  if (!reg) return null;
  for (const id of kit.rotation) {
    const def = reg.abilities.get(id);
    if (!def || def.shape.kind !== "circle") continue;
    const mitigates = def.effects.some(
      (e) => e.op === "applyEffect" && (reg.effects.get(e.effectId)?.modifiers?.damageTakenBp ?? 0) < 0,
    );
    if (mitigates) return id;
  }
  return null;
}

/** Under pressure: cast the group mitigation when it is ready, its effect is down on the tank, and
 * the tank is not already mid-cast (Bulwark has a 20-tick cast; never clip our own cast). */
export function pressureMitigationCommand(view: BotView, self: ActorState, kit: EffectiveKit): Command | null {
  const tick = view.state.tick;
  const id = groupMitigationAbilityId(kit);
  if (!id || !underPackPressure(view, self) || self.castId !== null) return null;
  if (!readyAndAffordable(self, id, tick, kit)) return null;
  if (effectTicksLeft(self, id, tick) > 0) return null;
  return { tick, actorId: self.id, kind: "cast", abilityId: id };
}

/**
 * The rotation the tank may spend from: never the self-sustain spender itself, and under pressure
 * no ability whose cost would drop momentum below the self-sustain spender's cost.
 */
export function pressureRotation(view: BotView, self: ActorState, kit: EffectiveKit): string[] {
  const sustain = kit.selfSustainAbilityId;
  const rotation = kit.rotation.filter((id) => id !== sustain);
  if (!sustain || !underPackPressure(view, self)) return rotation;
  const reserve = kit.costByAbility[sustain] ?? 0;
  return rotation.filter((id) => {
    const cost = kit.costByAbility[id] ?? 0;
    return cost === 0 || self.resource - cost >= reserve;
  });
}
