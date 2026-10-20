/**
 * bots/src/roles/healer-idle.ts
 *
 * BOTS-FIVE-BUTTONS (design/BIBLE.md Sec 0 test 1: "Classes with real decisions on five buttons"):
 * what the healer does in combat with nobody to heal or dispel. Before this the healer stood idle,
 * so Chanter's Dissonance (its damaging Payoff) and Refrain (the verse swap, needed only from Ward)
 * were never cast. In order: close another class's open Window with a damaging Payoff (the Combo);
 * in Mend, Refrain into Ward (its Ward burst shields on the swap, and the healer pre-mitigates until
 * someone drops, when healer.ts Refrains back to Mend); else the damaging Payoff on an engaged
 * enemy. Out of combat (no living enemy aimed at a player) the healer does nothing, so it never
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
  if (stanceSwapReady(self, kit, tick) && self.resource === mendVerseValue) {
    return [{ tick, actorId: self.id, kind: "cast", abilityId: kit.stanceSwapAbilityId! }];
  }
  const target = engaged.find((e) => e.id === self.targetId) ?? engaged[0]!;
  return castOn(target) ?? [];
}
