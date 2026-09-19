/**
 * server/src/run-end.ts
 *
 * P2 (policy-inside-stepRun): this file no longer decides WHEN a room's run is over -- sim/index.ts's
 * stepRun does that itself now (sim/run-wiring.ts's checkRunEnd), threading the result through
 * `Run.runEnd`. All that's left here is the wire-payload concern room.ts still needs once a run has
 * ended: rolling the reward choices (sim/encounter/rewards.ts) for the `run_end` message.
 *
 * Usage: `const payload = buildRunEndPayload(room.run.runEnd, SEED, keyLevel);`
 */
import type { RunEndState } from "../../sim/run-wiring";
import type { RunResult } from "../../sim/encounter/timer";
import { rollRewardChoices, GEAR_SLOTS, type Item } from "../../sim/encounter/rewards";

export interface RunEndPayload {
  result: RunResult;
  keyLevel: number;
  tick: number;
  rewardChoices: [Item, Item, Item];
}

/** Builds the `run_end` payload (minus the `{v, type, roomId}` wire envelope, room.ts's job) once
 * `runEnd.result` is set -- null before that. `rewardSeed` is a deterministic mix of the room's
 * fixed replay seed and the ending tick, not a real Ash/account economy -- screen/** owns that and
 * has no server-side store yet (this lane's final report flags it as a follow-up). */
export function buildRunEndPayload(runEnd: RunEndState, seed: number, keyLevel: number): RunEndPayload | null {
  if (!runEnd.result || runEnd.resultTick === null) return null;
  const rewardSeed = seed * 1_000_003 + runEnd.resultTick;
  const rewardChoices = rollRewardChoices(rewardSeed, keyLevel, runEnd.result, GEAR_SLOTS);
  return { result: runEnd.result, keyLevel, tick: runEnd.resultTick, rewardChoices };
}
