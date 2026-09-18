/**
 * sim/encounter/breakbar.ts — a shared bar drained by control/interrupt-tagged inputs.
 *
 * `drainBreakBar` applies this tick's tagged inputs (only "control" or "interrupt") in order; the
 * instant current hits 0 it emits exactly one "stagger" event and locks the bar (immune to further
 * drain) until `cooldownReadyTick`. `tickBreakBarReset` is called once per tick per bar to refill it
 * to max the tick the cooldown elapses. Two-function split keeps drain (event-producing, tick-scoped
 * inputs) and reset (time-only) independently testable.
 *
 * Usage: `bar = drainBreakBar(bar, "twins", inputsThisTick, tick, emit); bar = tickBreakBarReset(bar, tick);`
 */
import type { ActorId, SimEvent, Tick } from "./types";

export interface BreakBarState {
  current: number;
  max: number;
  actorIds: ActorId[];
  cooldownReadyTick: Tick;
}

export interface BreakBarInput {
  tick: Tick;
  actorId: ActorId;
  tag: "control" | "interrupt";
  amount: number;
}

// TUNE: 200 ticks = 10s at 20Hz before a drained bar can refill and be drained again.
export const RESET_COOLDOWN_TICKS = 200;

export function createBreakBar(max: number, actorIds: ActorId[]): BreakBarState {
  return { current: max, max, actorIds, cooldownReadyTick: 0 };
}

export function drainBreakBar(
  bar: BreakBarState,
  barId: string,
  inputs: BreakBarInput[],
  tick: Tick,
  emit: (event: SimEvent) => void
): BreakBarState {
  if (bar.current <= 0) return bar; // already staggered, waiting on reset
  let current = bar.current;
  for (const input of inputs) {
    if (input.tick !== tick) continue;
    if (input.tag !== "control" && input.tag !== "interrupt") continue;
    if (current <= 0) break;
    current -= input.amount;
  }
  if (current > 0) return { ...bar, current };
  emit({ tick, type: "stagger", barId, actorIds: bar.actorIds });
  return { current: 0, max: bar.max, actorIds: bar.actorIds, cooldownReadyTick: tick + RESET_COOLDOWN_TICKS };
}

export function tickBreakBarReset(bar: BreakBarState, tick: Tick): BreakBarState {
  const isStaggered = bar.current <= 0 && bar.cooldownReadyTick > 0;
  if (isStaggered && tick >= bar.cooldownReadyTick) {
    return { current: bar.max, max: bar.max, actorIds: bar.actorIds, cooldownReadyTick: 0 };
  }
  return bar;
}
