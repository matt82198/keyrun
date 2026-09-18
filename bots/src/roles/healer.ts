/**
 * bots/src/roles/healer.ts
 *
 * The healer state machine (bots/CLAUDE.md: "triage by missing health, dispel priority, stay in
 * range"): kick the priority cast, swap Chanter's verse into Mend when the party's missing health
 * crosses a threshold, dispel before healing, then triage the lowest-health ally under the
 * threshold. Pure: (view, self) -> Command[].
 *
 * Contract note: dispel detection uses the local convention that a dispellable effect's effectId
 * is prefixed "dispellable:" -- the shared ActorState/Telegraph contract carries no explicit
 * dispel flag.
 *
 * Usage: `import { healerDecide } from "./healer.js"`
 */
import type { ActorId, ActorState, BotView, Command } from "../types.js";
import { pickHealTarget, pickDispelTarget } from "../shared/priority.js";
import { resolveKit } from "../kits.js";
import { readyAndAffordable, ledgerKickCommand } from "./shared.js";

const DISPELLABLE_PREFIX = "dispellable:";
const HEAL_RANGE_M = 30;
const HEAL_THRESHOLD_PCT = 0.7;
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
