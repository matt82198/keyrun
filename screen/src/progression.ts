/**
 * Usage: import { ASH_TIMED, ASH_DEPLETED, ASH_ABANDONED, ashForResult, completeRun, pickReward,
 *                  computeRunDelta } from "./progression.js";
 *
 * Run economy: Ash accrual by result, the end-of-run choice-of-three reward roll and pick, and
 * the "since last run" delta line. Split out of rules.ts to keep every screen file under the
 * repo's 190-line cap. Nothing here touches the DOM or localStorage -- panels call these and then
 * `saveStore`.
 */
import { rollRewardChoices } from "../../sim/encounter/rewards.js";
import { applyGearPick, normalizeGearItem, recordRun } from "./rules.js";
import { hashString } from "./hash.js";
import { GEAR_SLOT_IDS, type Character, type Item, type RunRecord, type RunResult } from "./types.js";

// TUNE: Ash awarded per run by result, win or lose (design/BIBLE.md section 7): timed > depleted > abandoned.
export const ASH_TIMED = 50;
export const ASH_DEPLETED = 30;
export const ASH_ABANDONED = 10;

export function ashForResult(result: RunResult): number {
  if (result === "timed") return ASH_TIMED;
  if (result === "depleted") return ASH_DEPLETED;
  return ASH_ABANDONED;
}

function deriveRewardSeed(
  character: Pick<Character, "classId" | "level">,
  params: { result: RunResult; keyLevel: number },
  now: number,
): number {
  const basis = `${character.classId}:${character.level}:${params.keyLevel}:${params.result}:${now}`;
  return hashString(basis);
}

export interface CompleteRunParams {
  result: RunResult;
  keyLevel: number;
  durationSec: number;
}

export interface CompleteRunOutcome {
  character: Character;
  rewardChoices: Item[];
}

/**
 * Ends a run: banks Ash by result, rolls the deterministic choice-of-three (sim/encounter/rewards.ts),
 * and appends a history entry (reward left null until pickReward resolves it).
 */
export function completeRun(character: Character, params: CompleteRunParams, now: number = Date.now()): CompleteRunOutcome {
  const ash = ashForResult(params.result);
  const seed = deriveRewardSeed(character, params, now);
  const rewardChoices: Item[] = rollRewardChoices(seed, params.keyLevel, params.result, GEAR_SLOT_IDS).map(
    normalizeGearItem,
  );
  const record: RunRecord = {
    id: `run-${now}`,
    dungeon: "Hollow Vault",
    keyLevel: params.keyLevel,
    result: params.result,
    durationSec: params.durationSec,
    completedAt: now,
    rewardPicked: null,
    ashGained: ash,
  };
  const character2: Character = {
    ...character,
    ash: character.ash + ash,
    runHistory: recordRun(character.runHistory, record),
    rewardChoices,
  };
  return { character: character2, rewardChoices };
}

/** Pick button: equips (or parks as compare) the chosen reward and attaches it to the latest run record. */
export function pickReward(character: Character, itemId: string): Character {
  const choices = character.rewardChoices ?? [];
  const picked = choices.find((item) => item.id === itemId);
  if (!picked) return character;

  const withGear = applyGearPick(character, picked);
  const [latest, ...rest] = withGear.runHistory;
  const runHistory = latest ? [{ ...latest, rewardPicked: picked }, ...rest] : withGear.runHistory;

  return { ...withGear, runHistory, rewardChoices: null };
}

export interface RunDelta {
  newBestTime: boolean;
  newTier: boolean;
  ashGained: number;
  text: string;
}

/** "Since last run": did this run beat the prior best time, land a new tier, and how much Ash it banked. */
export function computeRunDelta(character: Pick<Character, "runHistory">): RunDelta | null {
  const [latest, ...rest] = character.runHistory;
  if (!latest) return null;

  const priorBest = rest.length > 0 ? Math.min(...rest.map((run) => run.durationSec)) : Number.POSITIVE_INFINITY;
  const newBestTime = latest.durationSec < priorBest;
  const newTier = latest.rewardPicked !== null;

  const parts: string[] = [];
  if (newBestTime) parts.push("new best time");
  if (newTier) parts.push(`new tier ${latest.rewardPicked!.tier}`);
  parts.push(`+${latest.ashGained} Ash`);

  return { newBestTime, newTier, ashGained: latest.ashGained, text: parts.join(" · ") };
}
