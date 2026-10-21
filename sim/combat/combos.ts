/**
 * ComboDef/ComboTable schema for Windows and Combos content data (design/BIBLE.md §4, §5's six
 * combos table), plus the effect-application helper that fires a combo's effects through the
 * same integer damage/heal/effect primitives casting.ts's completeCast uses (re-implemented here,
 * scoped down to the op kinds a combo effects list may contain, because casting.ts's own
 * applyEffects is a private, unexported function and casting.ts is out of this lane's edit scope).
 *
 * Usage:
 *   const combo = lookupCombo(table, "warden", "cinder"); // -> Shatter
 *   if (combo) applyComboEffects(state, payoffActor, target, combo.effects, effectDefs, emit);
 */
import type { ActorState, SimEvent, SimState } from "./types";
import type { EffectOp } from "./abilities";
import type { EffectDef } from "./effects";
import { applyEffectInstance, removeEffectInstances } from "./effects";
import { removeDispellableEffects } from "./effects-remove";
import { applyHeal } from "./damage";
import { creditResource, dealDamage } from "./mitigation";
import { addThreat } from "./threat";
import { inCircle } from "./targeting";

/** Combos.cs HealPulseRadiusMm: the Chanter's Hymn patch radius (content/data/abilities/chanter.json). */
export const HEAL_PULSE_RADIUS_MM = 2200;

export interface ComboDef {
  id: string;
  effects: EffectOp[];
}

/**
 * [setupClass][payoffClass] -> ComboDef. Content supplies one table keyed this way for the full
 * ("burst") combos fired when a Window opened on a target/self is closed, and a second table of
 * the same shape for the smaller "field" combos fired when a floor Window (Bulwark/Cinderfield/
 * Hymn) is closed (design/BIBLE.md §5's Floor Windows paragraph). The Lock (§9) reuses the first
 * table shape too: its Window op's `setupClass` is a boss pseudo-class (e.g. "boss") that content
 * adds as an extra row alongside the three player classes, one entry per class that may pay it off.
 */
export type ComboTable = Record<string, Record<string, ComboDef>>;

/** R-ANTHEM-1: an effect whose modifiers carry positive haste (Anthem's `hasted`) is a party buff in a combo. */
export function isPartyHaste(def: EffectDef): boolean {
  return (def.modifiers.hasteBp ?? 0) > 0;
}

export function lookupCombo(table: ComboTable, setupClass: string, payoffClass: string): ComboDef | undefined {
  return table[setupClass]?.[payoffClass];
}

/**
 * Applies a combo's effects op list from `source` (the actor whose Payoff closed the Window) onto
 * `target` (the actor that carried the Window; for a floor or self/Lock Window this is the same
 * actor windows.ts already resolved as the Window's owner). Ops this increment does not define combo
 * semantics for — "interrupt", "window", "payoff" — are intentionally no-ops here: combos do not
 * chain new Windows or interrupts in this increment.
 */
export function applyComboEffects(
  state: SimState,
  source: ActorState,
  target: ActorState,
  ops: EffectOp[],
  effectDefs: Record<string, EffectDef>,
  emit: (e: SimEvent) => void,
): void {
  for (const op of ops) {
    if (op.op === "damage") {
      dealDamage(state, source, target, op.amount, effectDefs, emit, source.id);
    } else if (op.op === "heal") {
      // HEAL-NEVER-HOSTILE (twin of Combos.cs ApplyComboEffects, #390): a combo heal lands only on the payoff caster's faction.
      if (target.kind === source.kind) applyHeal(state, target, op.amount, source.id, emit);
    } else if (op.op === "applyEffect") {
      const def = effectDefs[op.effectId];
      if (!def) continue;
      if (isPartyHaste(def)) {
        // R-ANTHEM-1 (f-7d5df7f9; BIBLE Sec 5 Anthem "Party gains 15% haste"): combo haste is party haste. It goes
        // to every living actor of the Payoff actor's faction, never the carrier (an enemy, on an enemy Window).
        // Twin of Combos.cs ApplyComboEffects.
        for (const member of Object.values(state.actors)) {
          if (member.alive && member.kind === source.kind) {
            applyEffectInstance(member, def, source.id, op.durationTicks, op.stacks, state.tick);
          }
        }
      } else if (def.periodic?.op.op === "heal" && target.kind !== source.kind) {
        // BOTS-FIVE-BUTTONS (D28 recording; mirrors Combos.cs ApplyHealPulseToAllies, BIBLE section 4 Consecrate):
        // a heal-over-time closed on a carrier of another faction goes to the Payoff actor's living allies within
        // HEAL_PULSE_RADIUS_MM of the carrier, never to the carrier.
        const allies = Object.values(state.actors).filter((a) => a.alive && a.kind === source.kind);
        for (const id of inCircle(target.pos, HEAL_PULSE_RADIUS_MM, allies)) {
          applyEffectInstance(state.actors[id]!, def, source.id, op.durationTicks, op.stacks, state.tick);
        }
      } else {
        applyEffectInstance(target, def, source.id, op.durationTicks, op.stacks, state.tick);
      }
    } else if (op.op === "removeEffect") {
      if (op.effectId === "*") removeDispellableEffects(source, target, effectDefs);
      else removeEffectInstances(target, op.effectId);
    } else if (op.op === "resource") {
      // The payoff actor earns the combo's resource (e.g. Forge's +40 momentum), never the
      // Window carrier: the op has no target field (abilities.ts). See mitigation.ts.
      creditResource(source, op.delta);
    } else if (op.op === "threat") {
      addThreat(target, source.id, op.amount ?? 0, op.multiplierBp);
    }
  }
}
