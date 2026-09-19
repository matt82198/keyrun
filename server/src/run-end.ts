/**
 * server/src/run-end.ts
 *
 * Pure run-end state machine: decides WHEN a room's run is over. Mirrors client/src/hud/
 * run-end.ts's `stepRunEnd` (read-only reference, not imported -- see server/CLAUDE.md): boss dead
 * -> "timed"/"depleted" (sim/encounter/timer.ts's own distinction, via `run.encounter.depleted`);
 * an explicit abandon request -> "abandoned" immediately; every player dead -> "abandoned" after
 * WIPE_GRACE_TICKS (same value the client uses -- sim has no rez mechanic, so a full wipe would
 * otherwise soft-lock the room forever). `finishRun` (sim/encounter/timer.ts) is the single source
 * of truth for the RunResult value and its "finish" SimEvent -- this file owns no game rule of its
 * own, only the host-level question of when to call it, and threads the wipe-grace window across
 * ticks. Once `result` is set the state is terminal; further calls are no-ops so room.ts can call
 * this unconditionally every tick.
 *
 * Usage: `state = stepRunEndCheck(state, run, bossActorId, abandonRequested, tick, emit);`
 */
import type { Run } from "../../sim/index";
import type { SimEvent } from "../../sim/core/events";
import type { ActorId, Tick } from "../../sim/core/state";
import { finishRun, type RunResult } from "../../sim/encounter/timer";
import { rollRewardChoices, GEAR_SLOTS, type Item } from "../../sim/encounter/rewards";

export const WIPE_GRACE_TICKS = 100; // 5s at 20Hz -- client/src/hud/run-end.ts's WIPE_GRACE_TICKS.

export interface RunEndState {
  result: RunResult | null;
  resultTick: Tick | null;
  wipeStartTick: Tick | null;
}

export function initialRunEndState(): RunEndState {
  return { result: null, resultTick: null, wipeStartTick: null };
}

export function stepRunEndCheck(
  state: RunEndState,
  run: Run,
  bossActorId: ActorId,
  abandonRequested: boolean,
  tick: Tick,
  emit: (e: SimEvent) => void
): RunEndState {
  if (state.result) return state;

  const boss = run.state.actors[bossActorId];
  if (boss !== undefined && !boss.alive) {
    return { result: finishRun(run.encounter, tick, true, emit), resultTick: tick, wipeStartTick: state.wipeStartTick };
  }
  if (abandonRequested) {
    return { result: finishRun(run.encounter, tick, false, emit), resultTick: tick, wipeStartTick: state.wipeStartTick };
  }

  const players = Object.values(run.state.actors).filter((a) => a.kind === "player");
  const allPlayersDead = players.length > 0 && players.every((a) => !a.alive);
  if (allPlayersDead) {
    const wipeStart = state.wipeStartTick ?? tick;
    if (tick - wipeStart >= WIPE_GRACE_TICKS) {
      return { result: finishRun(run.encounter, tick, false, emit), resultTick: tick, wipeStartTick: wipeStart };
    }
    return { result: null, resultTick: null, wipeStartTick: wipeStart };
  }
  return { result: null, resultTick: null, wipeStartTick: null };
}

export interface RunEndPayload {
  result: RunResult;
  keyLevel: number;
  tick: Tick;
  rewardChoices: [Item, Item, Item];
}

/** Builds the `run_end` payload (minus the `{v, type, roomId}` wire envelope, room.ts's job) once
 * `state.result` is set -- null before that. `rewardSeed` is a deterministic mix of the room's
 * fixed replay seed and the ending tick, not a real Ash/account economy -- screen/** owns that and
 * has no server-side store yet (this lane's final report flags it as a follow-up). */
export function buildRunEndPayload(state: RunEndState, seed: number, keyLevel: number): RunEndPayload | null {
  if (!state.result || state.resultTick === null) return null;
  const rewardSeed = seed * 1_000_003 + state.resultTick;
  const rewardChoices = rollRewardChoices(rewardSeed, keyLevel, state.result, GEAR_SLOTS);
  return { result: state.result, keyLevel, tick: state.resultTick, rewardChoices };
}
