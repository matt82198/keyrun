/**
 * sim/encounter/timer.ts — the run clock vs. `parTicks` (Mythic+ rules, STATE.md D35).
 *
 * Matt 2026-10-20 ("lean into m+ design heavily", "almost exact to wow"): a player death adds its
 * death cost to the run clock and a full wipe adds the wipe cost on top. Nothing drains on its
 * own: a key is depleted only when the clock (elapsed plus that added time) finishes past par, or
 * when an unrecovered full wipe forces `depleted` (run-wiring.ts's checkRunEnd). The Clause's
 * `rules.deathSlackCostTicks`/`wipeSlackCostTicks` replace the constants when set (above 0). The
 * Slack bank constants stay as tunables but no longer decide a result. Mirrors
 * game/sim/Keyrun.Sim/Encounter/Timer.cs (the C# sim is the source of truth, D28).
 *
 * Usage: `encounter = chargeDeath(encounter); finishRun(encounter, tick, true, emit);`
 */
import type { SimEvent, Tick } from "./types";
import type { EncounterState } from "./telegraphs";
import type { EncounterRules } from "./clauses";

// TUNE: 30s base slack bank at key 1, +1s per key level above 1 (20Hz ticks). No longer decides a result (D35).
export const SLACK_BASE_TICKS = 600;
export const SLACK_PER_KEY_TICKS = 20;
// TUNE: a death adds 15s to the run clock; a full wipe adds 25s on top.
export const DEATH_SLACK_COST_TICKS = 300;
export const WIPE_SLACK_COST_TICKS = 500;

export function initialSlackTicks(keyLevel: number): number {
  return SLACK_BASE_TICKS + SLACK_PER_KEY_TICKS * Math.max(0, keyLevel - 1);
}

/** The time one death adds: the Clause's cost when set (above 0), else the constant. */
export function deathCostTicks(rules: EncounterRules | undefined): number {
  return rules && rules.deathSlackCostTicks > 0 ? rules.deathSlackCostTicks : DEATH_SLACK_COST_TICKS;
}

/** The time one full wipe adds: the Clause's cost when set (above 0), else the constant. */
export function wipeCostTicks(rules: EncounterRules | undefined): number {
  return rules && rules.wipeSlackCostTicks > 0 ? rules.wipeSlackCostTicks : WIPE_SLACK_COST_TICKS;
}

/** One more death counted, its cost added to the run clock. */
export function chargeDeath(encounter: EncounterState): EncounterState {
  return {
    ...encounter,
    deaths: (encounter.deaths ?? 0) + 1,
    penaltyTicks: (encounter.penaltyTicks ?? 0) + deathCostTicks(encounter.rules),
  };
}

/** The wipe cost added to the run clock, on top of the wipe's own deaths. */
export function chargeWipe(encounter: EncounterState): EncounterState {
  return { ...encounter, penaltyTicks: (encounter.penaltyTicks ?? 0) + wipeCostTicks(encounter.rules) };
}

/** The run clock: elapsed ticks plus every death and wipe cost so far. */
export function clockTicks(encounter: EncounterState, elapsedTicks: Tick): number {
  return elapsedTicks + Math.max(0, encounter.penaltyTicks ?? 0);
}

export function isOverPar(runTimerTicks: Tick, encounter: EncounterState): boolean {
  return runTimerTicks > encounter.parTicks;
}

export type RunResult = "timed" | "depleted" | "abandoned";

/** Always emits "finish". Timed when completed, not wipe-depleted, and the clock is within par. */
export function finishRun(
  encounter: EncounterState,
  tick: Tick,
  completed: boolean,
  emit: (event: SimEvent) => void
): RunResult {
  const timed = !encounter.depleted && clockTicks(encounter, tick) <= encounter.parTicks;
  const result: RunResult = !completed ? "abandoned" : timed ? "timed" : "depleted";
  emit({ tick, type: "finish", result });
  return result;
}
