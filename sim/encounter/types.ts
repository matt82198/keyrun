/**
 * sim/encounter/types.ts — re-exports of the shared sim contracts.
 *
 * sim/core has landed on main; sim/encounter/* no longer carries local copies of these shapes.
 * Repointed at the real sim/core/state.ts (and friends) — same shapes, one source.
 */

export type { ActorId, Tick, Vec3, EffectInstance, ActorState, SimState } from "../core/state";
export type { Command } from "../core/commands";
export type { SimEvent } from "../core/events";
