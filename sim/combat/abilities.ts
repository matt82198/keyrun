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
  | { op: "threat"; amount?: number; multiplierBp?: number };

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
  }
  return errs;
}

/** Returns the school of an ability's first damage effect, or "physical" for schoolless abilities. */
export function abilitySchool(def: AbilityDef): string {
  const dmg = def.effects.find((e): e is Extract<EffectOp, { op: "damage" }> => e.op === "damage");
  return dmg ? dmg.school : "physical";
}
