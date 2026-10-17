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
import type { ActorState, BotView, Command } from "../types.js";
import { getContentRegistry, type EffectiveKit } from "../kits.js";
import { moveToward } from "../shared/command.js";
import { readyAndAffordable } from "./shared.js";

/** Fallback reach for an ally-targeted heal when no registry is injected (or no range is known). */
export const HEAL_RANGE_MM = 30 * 1000;
/** Stand this far inside a heal-over-time pool's radius so the target's own drift keeps it in. */
const HOT_POOL_MARGIN_MM = 500;

function rangeOf(abilityId: string): number {
  return getContentRegistry()?.abilities.get(abilityId)?.range ?? HEAL_RANGE_MM;
}

function dist(a: ActorState, b: ActorState): number {
  return Math.hypot(a.pos.x - b.pos.x, a.pos.z - b.pos.z);
}

export function healCommands(view: BotView, self: ActorState, kit: EffectiveKit, target: ActorState): Command[] {
  const tick = view.state.tick;
  const ready = (id: string | null): id is string => id !== null && readyAndAffordable(self, id, tick, kit);
  const { selfHealAbilityId, groupHealAbilityId, groupHealRadiusMm, targetHealAbilityId } = kit.heals;
  const d = dist(self, target);

  if (target.id === self.id && ready(selfHealAbilityId)) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: selfHealAbilityId }];
  }
  if (d <= groupHealRadiusMm && ready(groupHealAbilityId)) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: groupHealAbilityId }];
  }
  const second = kit.heals.secondGroupHeal;
  if (second && ready(second.abilityId)) {
    if (d <= second.radiusMm - HOT_POOL_MARGIN_MM) return [{ tick, actorId: self.id, kind: "cast", abilityId: second.abilityId }];
    // Walk into the pool's radius first: it heals far more than one targeted cast.
    if (self.castId === null && target.id !== self.id) {
      const move = moveToward(tick, self.id, self.pos, target.pos);
      if (move) return [move];
    }
  }
  if (targetHealAbilityId !== null && d <= rangeOf(targetHealAbilityId)) {
    return ready(targetHealAbilityId)
      ? [{ tick, actorId: self.id, kind: "cast", abilityId: targetHealAbilityId, targetId: target.id }]
      : [];
  }
  if (target.id === self.id) return [];
  const move = moveToward(tick, self.id, self.pos, target.pos);
  return move ? [move] : [];
}
