/**
 * tests/sim/balance/seeds.ts
 *
 * The seed set and fixed conditions for the key-2 survivability metric (orchestrator-set target,
 * 2026-10-18; Matt's play overrules). Each seed drives BOTH the sim run seed (createRun) and the
 * bot PRNG seed (seedPrng), so one seed is one fully reproducible run.
 *
 * HZ is the client's real-time tick pacing (?hz=60, boot-params.ts parseHz). The sim itself is
 * tick-authored and hz-agnostic, so the only place hz enters a headless run is the scripted
 * human's reaction time: HUMAN_REACTION_MS of wall time is HUMAN_REACTION_MS * HZ / 1000 ticks.
 *
 * BOT_SKILL matches the playable client (client/src/sim-host/sim-runtime.ts defaultMemory(80)).
 *
 * Usage: `import { BALANCE_SEEDS, BALANCE_KEY_LEVEL, HZ } from "./seeds.js";`
 */
export const BALANCE_SEEDS: readonly number[] = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
export const BALANCE_KEY_LEVEL = 2;
export const BALANCE_DUNGEON = "hollow-vault";
export const HZ = 60;
export const HUMAN_REACTION_MS = 250;
export const BOT_SKILL = 80;
/** Hard stop for a run that neither clears nor wipes (sim ticks; ~16 min of 20 Hz content). */
export const MAX_TICKS = 20_000;

/** The scripted human's reaction delay in sim ticks at HZ (250 ms at 60 Hz = 15 ticks). */
export function humanReactionTicks(hz: number = HZ, ms: number = HUMAN_REACTION_MS): number {
  return Math.max(1, Math.round((ms * hz) / 1000));
}
