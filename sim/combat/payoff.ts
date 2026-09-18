/**
 * Payoff resolution for Windows and Combos (design/BIBLE.md §4, §5) and the Lock (§9): given a
 * Payoff actor, finds the Window it can close (on a target/self actor, or on the floor-patch it is
 * standing in), looks up the matching ComboDef via combos.ts's ComboTable, applies it, and enforces
 * the two denial rules — same-class self-combo, and Lock alternation (a class may not close two
 * Locks in a row). See window-state.ts for the shared Window/Lock state this reads and mutates.
 *
 * Usage (see sim/combat/windows.ts's applyWindowsAndCombos, the sole caller): try
 * `tryClosePayoff(state, target, payoffActor, table, effectDefs, emit)` on the ability's resolved
 * target first; if it reports "no_window", fall back to `tryClosePayoffOnFloor`.
 */
import type { ActorState, SimEvent, SimState } from "./types";
import type { EffectDef } from "./effects";
import { removeEffectInstanceWhere } from "./effects";
import { inCircle } from "./targeting";
import { applyComboEffects, lookupCombo, type ComboTable } from "./combos";
import {
  FLOOR_WINDOW_RADIUS_MM,
  findOpenWindow,
  lastSetupClassOf,
  parseWindowEffectId,
  resetLockFailCount,
  setLastSetupClass,
  type FloorWindowRegistry,
} from "./window-state";

export type PayoffOutcome = "closed" | "denied" | "no_window";

/** Attempts to close a target/self (including Lock) Window carried by `target` via a Payoff from
 * `payoffActor`. Same-class Payoffs (or, for a Lock/alternate Window, a Payoff from whichever
 * class closed this actor's last alternate Window) are denied and emit "combo_denied" without
 * consuming the Window — any other class may still close it before it expires. */
export function tryClosePayoff(
  state: SimState,
  target: ActorState,
  payoffActor: ActorState,
  table: ComboTable,
  effectDefs: Record<string, EffectDef>,
  emit: (e: SimEvent) => void,
): PayoffOutcome {
  const now = state.tick;
  const inst = findOpenWindow(target, now);
  if (!inst) return "no_window";
  const parsed = parseWindowEffectId(inst.effectId);
  if (!parsed) return "no_window";

  const deniedClass = parsed.alternate ? lastSetupClassOf(target) : parsed.setupClass;
  if (deniedClass !== null && payoffActor.classId === deniedClass) {
    emit({
      tick: now,
      type: "combo_denied",
      windowId: parsed.windowId,
      targetId: target.id,
      setupActorId: inst.sourceId,
      payoffActorId: payoffActor.id,
      reason: parsed.alternate ? "lock_alternate" : "self_combo",
    });
    return "denied";
  }

  const combo = lookupCombo(table, parsed.setupClass, payoffActor.classId);
  if (!combo) return "no_window";

  removeEffectInstanceWhere(target, (e) => e === inst);
  if (parsed.alternate) {
    setLastSetupClass(target, payoffActor.classId);
    resetLockFailCount(target);
  }
  applyComboEffects(state, payoffActor, target, combo.effects, effectDefs, emit);
  emit({
    tick: now,
    type: "combo",
    comboId: combo.id,
    windowId: parsed.windowId,
    setupActorId: inst.sourceId,
    setupClass: parsed.setupClass,
    payoffActorId: payoffActor.id,
    payoffClass: payoffActor.classId,
    targetId: target.id,
  });
  return "closed";
}

/** Attempts to close any floor-patch Window `payoffActor` is currently standing inside, using the
 * "field" (smaller) ComboTable. Floor Windows accept any other class's Payoff; there is no
 * per-window setup-actor exclusion beyond the universal self-combo rule. */
export function tryClosePayoffOnFloor(
  state: SimState,
  payoffActor: ActorState,
  floor: FloorWindowRegistry,
  fieldTable: ComboTable,
  effectDefs: Record<string, EffectDef>,
  emit: (e: SimEvent) => void,
): PayoffOutcome {
  const now = state.tick;
  for (const patch of Object.values(floor.patches)) {
    if (patch.endsTick <= now) continue;
    const inRange = inCircle(patch.pos, FLOOR_WINDOW_RADIUS_MM, [payoffActor]).length > 0;
    if (!inRange) continue;
    if (payoffActor.classId === patch.setupClass) {
      emit({
        tick: now,
        type: "combo_denied",
        windowId: patch.windowId,
        setupActorId: patch.setupActorId,
        payoffActorId: payoffActor.id,
        reason: "self_combo",
      });
      return "denied";
    }
    const combo = lookupCombo(fieldTable, patch.setupClass, payoffActor.classId);
    if (!combo) continue;
    delete floor.patches[patch.windowId];
    applyComboEffects(state, payoffActor, payoffActor, combo.effects, effectDefs, emit);
    emit({
      tick: now,
      type: "combo",
      comboId: combo.id,
      windowId: patch.windowId,
      setupActorId: patch.setupActorId,
      setupClass: patch.setupClass,
      payoffActorId: payoffActor.id,
      payoffClass: payoffActor.classId,
      field: true,
    });
    return "closed";
  }
  return "no_window";
}
