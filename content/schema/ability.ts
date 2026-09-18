/**
 * content/schema/ability.ts
 * Local copy of the canonical AbilityDef / EffectOp contract shared with the sim-combat lane
 * (sim/combat/abilities.ts defines the same shapes independently; integration is a re-import).
 * Data-shape only. Never import sim/ from here.
 */

export type ShapeKind = "target" | "self" | "cone" | "circle" | "line";

export interface ShapeDef {
  kind: ShapeKind;
  radius?: number;
  angle1024?: number;
  length?: number;
  width?: number;
}

export interface AbilityCost {
  resource: string;
  amount: number;
}

/** Single-slot hard-control tag. ADDED beyond the canonical contract (see effect.ts). */
export type ControlKind = "stun" | "silence" | "root" | "blind";

export type EffectOp =
  | { op: "damage"; amount: number; school: string }
  | { op: "heal"; amount: number }
  | { op: "applyEffect"; effectId: string; durationTicks: number; stacks?: number }
  | { op: "removeEffect"; effectId: string }
  | { op: "interrupt"; lockoutTicks: number }
  | { op: "resource"; resource: string; delta: number }
  | { op: "threat"; amount?: number; multiplierBp?: number }
  | { op: "window"; windowId: string; durationTicks: number; onTarget: boolean }
  | { op: "combo"; comboTable: string };

export interface AbilityDef {
  id: string;
  name: string;
  castTicks: number;
  gcdTicks: number;
  cooldownTicks: number;
  charges: number;
  cost?: AbilityCost;
  /** millimetres */
  range: number;
  shape: ShapeDef;
  interruptible: boolean;
  /** e.g. "setup:sundered", "payoff", "interrupt", "control", "dispel", "movement", "defensive" */
  tags: string[];
  effects: EffectOp[];
  /**
   * ADDED (content lane; not in the canonical contract): per-loop-state effect override for
   * stance classes (Chanter's Mend/Ward), keyed by loop state name ("mend" | "ward" here).
   * When the class's current stance matches a key, sim should use that list instead of `effects`.
   * Absent = `effects` always applies. See content/CLAUDE.md "Schema additions".
   */
  variants?: Record<string, EffectOp[]>;
}
