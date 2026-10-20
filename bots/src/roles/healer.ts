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
 * Also flees a hostile floor patch (shared/floor.ts) before any of the above, keeping the escape
 * point within HEAL_RANGE_M of the party centroid whenever one exists (bots/CLAUDE.md: "stay in
 * range") rather than just fleeing to the nearest safe ground.
 *
 * Usage: `import { healerDecide } from "./healer.js"`
 */
import type { ActorId, ActorState, BotView, Command } from "../types.js";
import { pickHealTarget, pickDispelTarget } from "../shared/priority.js";
import { resolveKit } from "../kits.js";
import { floorEscapeCommand } from "../shared/floor.js";
import { moveToward } from "../shared/command.js";
import { readyAndAffordable, ledgerKickCommand } from "./shared.js";
import { healCommands } from "./heal-pick.js";
import { idleCommands, stanceSwapReady } from "./healer-idle.js";

const DISPELLABLE_PREFIX = "dispellable:";
/** ActorState.pos is already integer millimetres (sim/CLAUDE.md), so this 30m design constant is
 * converted to mm once, here, at definition -- never by re-scaling an already-mm position (that
 * was bots/src/shared/positioning.ts's toMM() bug). */
const HEAL_RANGE_MM = 30 * 1000;
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

  const party = Object.values(view.state.actors).filter((a) => a.kind === "player" && a.alive && a.id !== self.id);
  const escapeOpts =
    party.length > 0
      ? {
          keepNear: {
            anchor: {
              x: Math.round(party.reduce((s, a) => s + a.pos.x, 0) / party.length),
              z: Math.round(party.reduce((s, a) => s + a.pos.z, 0) / party.length),
            },
            maxDistMM: HEAL_RANGE_MM,
          },
        }
      : {};
  const escape = floorEscapeCommand(view, self, escapeOpts);
  if (escape) return [escape];

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
  const dispelId = dispellable.size > 0 ? pickDispelTarget(view.state.actors, dispellable) : null;
  if (dispelId === null) {
    // Heal triage goes through the shape-aware picker: a self-shaped heal cast AT an ally heals
    // only the caster (roles/heal-pick.ts).
    const healId = pickHealTarget(view.state.actors, HEAL_THRESHOLD_PCT);
    const healTarget = healId !== null ? view.state.actors[healId] : undefined;
    if (!healTarget) return idleCommands(view, self, kit, MEND_VERSE_VALUE);
    // BOTS-FIVE-BUTTONS: someone needs healing while the stance sits in Ward, so Refrain back to
    // Mend first (its Mend burst heals on the swap) before the shape-aware heal.
    if (stanceSwapReady(self, kit, tick) && self.resource !== MEND_VERSE_VALUE) {
      return [{ tick, actorId: self.id, kind: "cast", abilityId: kit.stanceSwapAbilityId! }];
    }
    return healCommands(view, self, kit, healTarget);
  }
  const targetId = dispelId;
  const abilityId = kit.dispelAbilityId ?? "dispel";
  const target = view.state.actors[targetId];
  if (!target) return [];
  const dx = target.pos.x - self.pos.x;
  const dz = target.pos.z - self.pos.z;
  const dist = Math.hypot(dx, dz);
  if (dist > HEAL_RANGE_MM) {
    const move = moveToward(tick, self.id, self.pos, target.pos);
    return move ? [move] : [];
  }
  if (readyAndAffordable(self, abilityId, tick, kit)) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId, targetId }];
  }
  return [];
}
