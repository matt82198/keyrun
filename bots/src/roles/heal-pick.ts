/**
 * bots/src/roles/heal-pick.ts
 *
 * Shape-aware heal choice for the healer bot (bots/src/kit-heals.ts explains the shapes). Given
 * the triage target, returns the cast that actually REACHES it, or a move into range:
 *   1. target is the healer itself -> self heal (Refrain/Mend);
 *   2. target within the group heal's radius around the healer -> group heal (Chorus/Mend);
 *   3. a second group heal (Hymn's heal-over-time pool) ready -> cast it if the target stands in
 *      its radius, else walk toward the target (never out of our own cast);
 *   4. target within the ally-targeted heal's range -> targeted heal (Cadence/Mend's HoT);
 *   5. otherwise walk toward the target.
 * The old single-`healAbilityId` path cast Chanter's self-shaped Refrain AT a dying ally, which
 * sim resolves to the caster only (sim/combat/targeting.ts's resolveTargets): the tank never got
 * a single heal in the pull-0 repro (tests/sim/integration/bots-pull0.test.ts).
 *
 * Usage: `const cmds = healCommands(view, self, kit, target);`
 */
import type { ActorState, BotView, Command, Tick } from "../types.js";
import { getContentRegistry, type EffectiveKit } from "../kits.js";
import { moveToward } from "../shared/command.js";
import { readyAndAffordable } from "./shared.js";
import { swapsVerseWithoutHealing } from "../kit-heals.js";

/** Fallback reach for an ally-targeted heal when no registry is injected (or no range is known). */
export const HEAL_RANGE_MM = 30 * 1000;
/** Stand this far inside a heal-over-time pool's radius so the target's own drift keeps it in. */
const HOT_POOL_MARGIN_MM = 500;

/** R-PILE-7d: under this health fraction the heal target is an emergency, the trigger that spends a banked charge. */
export const CHARGE_EMERGENCY_PCT = 0.35;

/**
 * CHARGE-SPEND (ruling R-PILE-7d, finding f-f44791bc; the WoW Mythic+ rule for a charged ability): whether the healer
 * spends a charge of `abilityId` now. A one-charge ability always does (unchanged). A multi-charge ability (the kit's
 * chargedAbilities) does when every charge is full (sim chargesAvailable is full once cooldowns[id] <= tick), so the
 * recharge is never wasted; otherwise the spare is banked and spent only on a real trigger: the target under
 * CHARGE_EMERGENCY_PCT, or no other heal that reaches the target ready this GCD. Never back to back on a target that
 * was just healed. C# twin: game/bots/Keyrun.Bots/Roles/HealPick.cs SpendsCharge.
 */
export function spendsCharge(
  self: ActorState,
  abilityId: string,
  tick: Tick,
  kit: Pick<EffectiveKit, "chargedAbilities">,
  target: ActorState,
  otherHealReady: () => boolean,
): boolean {
  const c = kit.chargedAbilities?.[abilityId];
  if (!c || c.cooldownTicks <= 0 || (self.cooldowns[abilityId] ?? 0) <= tick) return true;
  return target.health / target.maxHealth < CHARGE_EMERGENCY_PCT || !otherHealReady();
}

function rangeOf(abilityId: string): number {
  return getContentRegistry()?.abilities.get(abilityId)?.range ?? HEAL_RANGE_MM;
}

function dist(a: ActorState, b: ActorState): number {
  return Math.hypot(a.pos.x - b.pos.x, a.pos.z - b.pos.z);
}

export function healCommands(view: BotView, self: ActorState, kit: EffectiveKit, target: ActorState): Command[] {
  const tick = view.state.tick;
  const reg = getContentRegistry();
  const leavesVerse = (id: string): boolean => {
    const def = reg?.abilities.get(id);
    return def !== undefined && swapsVerseWithoutHealing(def, self.resource >= 1 ? "mend" : "ward", reg?.effects);
  };
  const ready = (id: string | null): id is string => id !== null && readyAndAffordable(self, id, tick, kit) && !leavesVerse(id);
  const { selfHealAbilityId, groupHealAbilityId, groupHealRadiusMm, targetHealAbilityId } = kit.heals;
  const d = dist(self, target);
  const second = kit.heals.secondGroupHeal;
  const reaches = (id: string | null): boolean =>
    id !== null &&
    ((id === selfHealAbilityId && target.id === self.id) ||
      (id === groupHealAbilityId && d <= groupHealRadiusMm) ||
      (!!second && id === second.abilityId && d <= second.radiusMm - HOT_POOL_MARGIN_MM) ||
      (id === targetHealAbilityId && d <= rangeOf(id)));
  const healIds = [selfHealAbilityId, groupHealAbilityId, second?.abilityId ?? null, targetHealAbilityId];
  const press = (id: string | null): id is string =>
    ready(id) && spendsCharge(self, id, tick, kit, target, () => healIds.some((o) => o !== null && o !== id && reaches(o) && ready(o)));

  if (target.id === self.id && press(selfHealAbilityId)) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: selfHealAbilityId }];
  }
  if (d <= groupHealRadiusMm && press(groupHealAbilityId)) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: groupHealAbilityId }];
  }
  if (second && ready(second.abilityId)) {
    if (d <= second.radiusMm - HOT_POOL_MARGIN_MM && press(second.abilityId)) return [{ tick, actorId: self.id, kind: "cast", abilityId: second.abilityId }];
    // Walk into the pool's radius first: it heals far more than one targeted cast.
    if (self.castId === null && target.id !== self.id) {
      const move = moveToward(tick, self.id, self.pos, target.pos);
      if (move) return [move];
    }
  }
  if (targetHealAbilityId !== null && d <= rangeOf(targetHealAbilityId)) {
    return press(targetHealAbilityId)
      ? [{ tick, actorId: self.id, kind: "cast", abilityId: targetHealAbilityId, targetId: target.id }]
      : [];
  }
  if (target.id === self.id) return [];
  const move = moveToward(tick, self.id, self.pos, target.pos);
  return move ? [move] : [];
}
