/**
 * bots/src/roles/healer-idle.ts
 *
 * BOTS-FIVE-BUTTONS (design/BIBLE.md Sec 0 test 1: "Classes with real decisions on five buttons"):
 * what the healer does in combat with nobody to heal or dispel. Before this the healer stood idle,
 * so Chanter's Dissonance (its damaging Payoff) and Refrain (the verse swap, needed only from Ward)
 * were never cast. In order: close another class's open Window with a damaging Payoff (the Combo);
 * the damaging Payoff on an engaged enemy (HEALER-KEEPS-PRESSING: with nobody to heal the first GCD
 * goes to a non-heal button, R-BOTS-14); else, in Mend, Refrain into Ward (its Ward burst shields on
 * the swap, and the healer pre-mitigates until someone drops, when healer.ts Refrains back to Mend).
 * R-BOTS-17: in Mend the Payoff takes the fight's first idle GCD only; after that the Refrain into
 * Ward comes before any further press. Out of combat (no living enemy aimed at a player) the healer does nothing, so it never
 * pulls. Mirrors game/bots/Keyrun.Bots/Roles/HealerIdle.cs.
 *
 * Usage: `if (healTarget === undefined) return idleCommands(view, self, kit, MEND_VERSE_VALUE);`
 */
import type { AbilityDef } from "@content/schema/ability";
import type { ActorState, BotView, Command, Tick } from "../types.js";
import type { EffectiveKit } from "../kits.js";
import { getContentRegistry } from "../kits.js";
import { findClosableWindow } from "../shared/windows.js";
import { readyAndAffordable } from "./shared.js";

/** True when the kit has a stance swap that is ready and affordable now. */
export function stanceSwapReady(self: ActorState, kit: EffectiveKit, tick: Tick): boolean {
  return kit.loopKind === "stance" && !!kit.stanceSwapAbilityId && readyAndAffordable(self, kit.stanceSwapAbilityId, tick, kit);
}

function damages(def: AbilityDef): boolean {
  if (def.effects.some((op) => op.op === "damage")) return true;
  const variants = def.variants as Record<string, { op: string }[]> | undefined;
  return variants !== undefined && Object.values(variants).some((ops) => ops.some((op) => op.op === "damage"));
}

export function idleCommands(view: BotView, self: ActorState, kit: EffectiveKit, mendVerseValue: number): Command[] {
  const tick = view.state.tick;
  const actors = view.state.actors;
  const engaged = Object.values(actors).filter((a) => {
    if (a.kind !== "enemy" || !a.alive || a.targetId === null) return false;
    return actors[a.targetId]?.kind === "player";
  });
  const reg = getContentRegistry();
  if (engaged.length === 0 || !reg) return [];

  const damagingPayoffs = kit.payoffAbilityIds.filter((id) => {
    const def = reg.abilities.get(id);
    return def !== undefined && damages(def);
  });
  const castOn = (target: ActorState): Command[] | null => {
    const dist = Math.hypot(self.pos.x - target.pos.x, self.pos.z - target.pos.z);
    const abilityId = damagingPayoffs.find(
      (id) => readyAndAffordable(self, id, tick, kit) && dist <= reg.abilities.get(id)!.range
    );
    if (abilityId === undefined) return null;
    const commands: Command[] = [];
    if (self.targetId !== target.id) commands.push({ tick, actorId: self.id, kind: "target", targetId: target.id });
    commands.push({ tick, actorId: self.id, kind: "cast", abilityId, targetId: target.id });
    return commands;
  };

  const win = findClosableWindow(actors, tick, self.classId);
  const windowTarget = win ? actors[win.targetId] : undefined;
  if (windowTarget) {
    const payoff = castOn(windowTarget);
    if (payoff) return payoff;
  }
  // R-BOTS-17: in Mend the damaging Payoff takes the fight's FIRST idle GCD only; after that the Refrain into Ward
  // comes before the press again (the pre-R-BOTS-14 priority resumes).
  const refrainIntoWard = stanceSwapReady(self, kit, tick) && self.resource === mendVerseValue;
  if (refrainIntoWard && payoffHadFirstIdleGcd(self, damagingPayoffs, tick)) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: kit.stanceSwapAbilityId! }];
  }
  const target = engaged.find((e) => e.id === self.targetId) ?? engaged[0]!;
  const press = castOn(target);
  if (press) return press;
  if (refrainIntoWard) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: kit.stanceSwapAbilityId! }];
  }
  return [];
}

/**
 * R-BOTS-17: true when this idle GCD is NOT the fight's first, so the Payoff has already had its turn and the
 * Refrain into Ward comes first. A fight's first idle GCD follows a lull: the healer's GCD has been free for more
 * than a whole GCD (out of combat it presses nothing), or a damaging Payoff (it has a cooldown, so
 * sim/combat/cooldowns.ts consumeCharge leaves an entry) has never been pressed at all. Mid-fight the healer presses
 * every GCD, so its GCD came off within the last one. Mirrors HealerIdle.cs PayoffHadFirstIdleGcd.
 */
function payoffHadFirstIdleGcd(self: ActorState, damagingPayoffs: string[], tick: Tick): boolean {
  if (damagingPayoffs.length === 0 || !damagingPayoffs.some((id) => self.cooldowns[id] !== undefined)) return false;
  const reg = getContentRegistry()!;
  const gcd = Math.max(...damagingPayoffs.map((id) => reg.abilities.get(id)!.gcdTicks));
  return tick - self.gcdReadyTick <= gcd;
}
