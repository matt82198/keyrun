/**
 * Shared simulation type contracts, re-exported from sim/core now that sim/core has landed on
 * main. This lane no longer carries local copies of these shapes; sim/combat compiles against the
 * single cross-lane source of truth in sim/core/state.ts, sim/core/commands.ts,
 * sim/core/events.ts, and sim/core/tick.ts.
 * Usage: import type { ActorState, Command, StepRules } from "./types";
 */
export type { ActorId, Tick, Bp, Vec3, EffectInstance, ActorState, SimState } from "../core/state";
export type { Command } from "../core/commands";
export type { SimEvent } from "../core/events";
export type { StepRules } from "../core/tick";
