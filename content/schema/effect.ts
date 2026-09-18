/**
 * content/schema/effect.ts
 * Local copy of the canonical EffectDef contract shared with the sim-combat lane.
 * Data-shape only. Never import sim/ from here.
 */

import type { ControlKind, EffectOp } from "./ability";

export interface EffectModifiers {
  damageDoneBp?: number;
  damageTakenBp?: number;
  hasteBp?: number;
  movementBp?: number;
  /**
   * ADDED (content lane): the canonical modifiers set is basis-points-only and has no way to
   * express hard control (stun/silence/root/blind). One slot covers all four rather than four
   * booleans. See content/CLAUDE.md "Schema additions".
   */
  controlKind?: ControlKind;
}

export interface EffectPeriodic {
  everyTicks: number;
  op: EffectOp;
}

export interface EffectDef {
  id: string;
  name: string;
  maxStacks: number;
  durationTicks?: number;
  modifiers?: EffectModifiers;
  periodic?: EffectPeriodic;
  dispellable?: "magic" | "physical" | null;
}
