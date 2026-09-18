/**
 * AbilityDef schema and validator. Data-driven ability definitions consumed by casting.ts.
 * Usage: const errs = validateAbilityDef(def); if (errs.length) throw new Error(errs.join("; "));
 */
import type { Tick } from "./types";

export type EffectOp =
  | { op: "damage"; amount: number; school: string }
  | { op: "heal"; amount: number }
  | { op: "applyEffect"; effectId: string; durationTicks: Tick; stacks: number }
  | { op: "removeEffect"; effectId: string }
  | { op: "interrupt" }
  | { op: "resource"; delta: number }
  | { op: "threat"; amount?: number; multiplierBp?: number }
  /**
   * Opens a Window (design/BIBLE.md §4): a timed tag that a Payoff ability from another class
   * can close for a Combo. `onTarget: true` puts the Window on the resolved target (or the
   * caster itself when `self: true`, e.g. a boss's own Window); `onTarget: false` plants a
   * floor-patch Window instead (Bulwark/Cinderfield/Hymn), tracked by windows.ts, not on any
   * actor. `setupClass` is the key this Window's Combo is looked up under in the content-supplied
   * ComboTable (combos.ts) — normally the opening actor's own classId, or a boss pseudo-class
   * (e.g. "boss") for a self Window. `alternate: true` is the Lock (§9): instead of forbidding a
   * Payoff from `setupClass` (which a boss's own class never matches anyway), it forbids a
   * Payoff from whichever class most recently *closed* this actor's last alternate Window —
   * windows.ts persists that as `lastSetupClass` on the actor — forcing the party to alternate
   * who closes it between consecutive Locks. `radiusMm` (floor Windows only, `onTarget: false`)
   * overrides windows.ts's `FLOOR_WINDOW_RADIUS_MM` default so content can size a floor Window
   * per ability (design/BIBLE.md §4/§5: Bulwark, Cinderfield and Hymn are different sizes);
   * omitted, the default applies.
   */
  | { op: "window"; windowId: string; durationTicks: Tick; onTarget: boolean; setupClass: string; self?: boolean; alternate?: boolean; radiusMm?: number }
  /**
   * Marks this ability as a Payoff: on cast completion, windows.ts attempts to close an open
   * Window on the resolved target (falling back to any floor-patch Window in range when the
   * target carries none), firing the looked-up Combo through combos.ts's effect pipeline.
   */
  | { op: "payoff" };

export interface AbilityShape {
  kind: "target" | "self" | "cone" | "circle" | "line";
  radius?: number;
  angle1024?: number;
  length?: number;
}

export interface AbilityDef {
  id: string;
  castTicks: Tick;
  gcdTicks: Tick;
  cooldownTicks: Tick;
  charges: number;
  cost: { resource: string; amount: number };
  range: number;
  shape: AbilityShape;
  interruptible: boolean;
  effects: EffectOp[];
  /** Free-form classification tags (e.g. "setup", "payoff", "interrupt"); optional so existing
   * content and fixtures that predate Windows and Combos keep compiling untouched. */
  tags?: string[];
}

export function validateAbilityDef(def: AbilityDef): string[] {
  const errs: string[] = [];
  if (!def.id) errs.push("id is required");
  if (!Number.isInteger(def.castTicks) || def.castTicks < 0) errs.push("castTicks must be an integer >= 0");
  if (!Number.isInteger(def.gcdTicks) || def.gcdTicks < 0) errs.push("gcdTicks must be an integer >= 0");
  if (!Number.isInteger(def.cooldownTicks) || def.cooldownTicks < 0) errs.push("cooldownTicks must be an integer >= 0");
  if (!Number.isInteger(def.charges) || def.charges < 1) errs.push("charges must be an integer >= 1");
  if (!Number.isInteger(def.cost.amount) || def.cost.amount < 0) errs.push("cost.amount must be an integer >= 0");
  if (!Number.isInteger(def.range) || def.range < 0) errs.push("range must be an integer mm >= 0");
  if (def.shape.kind === "cone" && (def.shape.angle1024 === undefined || def.shape.radius === undefined)) {
    errs.push("cone shape requires radius and angle1024");
  }
  if (def.shape.kind === "circle" && def.shape.radius === undefined) {
    errs.push("circle shape requires radius");
  }
  if (def.shape.kind === "line" && (def.shape.length === undefined || def.shape.radius === undefined)) {
    errs.push("line shape requires length and radius (half-width)");
  }
  if (def.effects.length === 0) errs.push("effects must not be empty");
  for (const op of def.effects) {
    if (op.op === "applyEffect" && op.durationTicks < 0) errs.push(`applyEffect ${op.effectId} durationTicks must be >= 0`);
    if (op.op === "applyEffect" && (!Number.isInteger(op.stacks) || op.stacks < 1)) {
      errs.push(`applyEffect ${op.effectId} stacks must be an integer >= 1`);
    }
    if (op.op === "damage" && (!Number.isInteger(op.amount) || op.amount < 0)) errs.push("damage amount must be an integer >= 0");
    if (op.op === "heal" && (!Number.isInteger(op.amount) || op.amount < 0)) errs.push("heal amount must be an integer >= 0");
    if (op.op === "window") {
      if (!op.windowId) errs.push("window op requires windowId");
      if (!Number.isInteger(op.durationTicks) || op.durationTicks < 0) errs.push(`window ${op.windowId} durationTicks must be an integer >= 0`);
      if (!op.setupClass) errs.push(`window ${op.windowId} requires setupClass`);
      if (op.radiusMm !== undefined && (!Number.isInteger(op.radiusMm) || op.radiusMm <= 0)) {
        errs.push(`window ${op.windowId} radiusMm must be a positive integer`);
      }
    }
  }
  return errs;
}

/** Returns the school of an ability's first damage effect, or "physical" for schoolless abilities. */
export function abilitySchool(def: AbilityDef): string {
  const dmg = def.effects.find((e): e is Extract<EffectOp, { op: "damage" }> => e.op === "damage");
  return dmg ? dmg.school : "physical";
}
