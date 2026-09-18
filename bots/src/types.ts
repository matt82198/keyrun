/**
 * bots/src/types.ts
 *
 * Sim-facing types the bots package consumes, re-exported from the real sim/core package now
 * that it has landed on main (bots/ no longer carries a local copy). `Telegraph` and `BotView`
 * are lane-specific extras with no sim/core equivalent, so they stay defined here.
 *
 * Usage: `import type { BotView, Command, ActorState } from "./types.js"`
 */

import type { SimState } from "../../sim/core/state.js";

export type { ActorId, Tick, Vec3, EffectInstance, ActorState, SimState } from "../../sim/core/state.js";
export type { Command } from "../../sim/core/commands.js";
export type { SimEvent } from "../../sim/core/events.js";

/** A boss/enemy AoE warning. Shapes are evaluated in the xz plane. */
export interface Telegraph {
  id: string;
  shape: "circle" | "cone" | "line";
  center: { x: number; z: number };
  radius?: number;
  angle1024?: number;
  yaw?: number;
  length?: number;
  width?: number;
  resolvesTick: number;
  danger: boolean;
}

/** What a bot's decide() call is handed each tick. */
export interface BotView {
  state: SimState;
  telegraphs: Telegraph[];
  classKit: {
    rotation: string[];
    interruptAbilityId: string | null;
    defensiveAbilityId: string | null;
    healAbilityId: string | null;
  };
}
