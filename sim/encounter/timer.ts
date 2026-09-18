/**
 * sim/encounter/timer.ts — run timer vs. `parTicks`, and the Slack bank.
 *
 * Slack is a soft time bank (ticks) that starts full per key level and is spent on player deaths and
 * full-party wipes instead of a hard fail. `spendSlackOnDeath` / `spendSlackOnWipe` clamp at 0 and
 * emit "depleted" exactly once, the tick the bank first hits 0 (further spends after depletion are
 * silent — the run keeps going per the design bible, section 8). `finishRun` always emits "finish";
 * a depleted bank still allows completion, it just downgrades the result from "timed" to "depleted".
 *
 * Usage: `encounter = spendSlackOnDeath(encounter, tick, emit); finishRun(encounter, tick, true, emit);`
 */
import type { SimEvent, Tick } from "./types";
import type { EncounterState } from "./telegraphs";

// TUNE: 30s base slack bank at key 1, +1s per key level above 1 (20Hz ticks).
export const SLACK_BASE_TICKS = 600;
export const SLACK_PER_KEY_TICKS = 20;
// TUNE: a death costs 15s of bank; a full wipe costs 25s.
export const DEATH_SLACK_COST_TICKS = 300;
export const WIPE_SLACK_COST_TICKS = 500;

export function initialSlackTicks(keyLevel: number): number {
  return SLACK_BASE_TICKS + SLACK_PER_KEY_TICKS * Math.max(0, keyLevel - 1);
}

function spendSlack(
  encounter: EncounterState,
  costTicks: number,
  tick: Tick,
  emit: (event: SimEvent) => void
): EncounterState {
  const next = Math.max(0, encounter.slackTicks - costTicks);
  const justDepleted = next === 0 && !encounter.depleted;
  if (justDepleted) emit({ tick, type: "depleted" });
  return { ...encounter, slackTicks: next, depleted: encounter.depleted || next === 0 };
}

export function spendSlackOnDeath(
  encounter: EncounterState,
  tick: Tick,
  emit: (event: SimEvent) => void
): EncounterState {
  return spendSlack(encounter, DEATH_SLACK_COST_TICKS, tick, emit);
}

export function spendSlackOnWipe(
  encounter: EncounterState,
  tick: Tick,
  emit: (event: SimEvent) => void
): EncounterState {
  return spendSlack(encounter, WIPE_SLACK_COST_TICKS, tick, emit);
}

export function isOverPar(runTimerTicks: Tick, encounter: EncounterState): boolean {
  return runTimerTicks > encounter.parTicks;
}

export type RunResult = "timed" | "depleted" | "abandoned";

/** Always emits "finish". A depleted Slack bank still allows completion — result is "depleted", not blocked. */
export function finishRun(
  encounter: EncounterState,
  tick: Tick,
  completed: boolean,
  emit: (event: SimEvent) => void
): RunResult {
  const result: RunResult = !completed ? "abandoned" : encounter.depleted ? "depleted" : "timed";
  emit({ tick, type: "finish", result });
  return result;
}
