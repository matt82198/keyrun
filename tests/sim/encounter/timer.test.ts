import { describe, expect, it } from "vitest";
import {
  initialSlackTicks,
  chargeDeath,
  chargeWipe,
  clockTicks,
  finishRun,
  DEATH_SLACK_COST_TICKS,
  WIPE_SLACK_COST_TICKS,
  SLACK_BASE_TICKS,
  SLACK_PER_KEY_TICKS,
} from "../../../sim/encounter/timer";
import { applyDeathsAndWipe } from "../../../sim/run-wiring";
import { defaultEncounterRules } from "../../../sim/encounter/clauses";
import type { EncounterState } from "../../../sim/encounter/telegraphs";
import type { SimEvent } from "../../../sim/encounter/types";
import type { SimState } from "../../../sim/core/state";

function encounterAtKey(keyLevel: number): EncounterState {
  return {
    telegraphs: [],
    breakBars: {},
    pullIndex: 0,
    slackTicks: initialSlackTicks(keyLevel),
    parTicks: 10000,
    keyLevel,
    depleted: false,
    rules: defaultEncounterRules(keyLevel),
  };
}

// Only `actors[id].kind`/`.alive` are read by applyDeathsAndWipe.
function party(dead: number[]): SimState {
  const actors: Record<number, { id: number; kind: string; alive: boolean }> = {};
  for (const id of [1, 2, 3, 4]) actors[id] = { id, kind: "player", alive: !dead.includes(id) };
  actors[99] = { id: 99, kind: "enemy", alive: true };
  return { actors } as unknown as SimState;
}

const death = (tick: number, actorId: number): SimEvent => ({ tick, type: "death", actorId, sourceId: 99 }) as SimEvent;

function threeDeaths(encounter: EncounterState, emitted: SimEvent[]): EncounterState {
  const emit = (e: SimEvent) => emitted.push(e);
  encounter = applyDeathsAndWipe(party([1]), encounter, [death(5, 1)], 5, emit);
  encounter = applyDeathsAndWipe(party([1, 2]), encounter, [death(9, 2)], 9, emit);
  return applyDeathsAndWipe(party([1, 2, 3]), encounter, [death(12, 3)], 12, emit);
}

describe("run clock (STATE.md D35, Mythic+: deaths only add time)", () => {
  it("key 10 still documents the bank constants: base + per-key * 9", () => {
    expect(initialSlackTicks(10)).toBe(SLACK_BASE_TICKS + SLACK_PER_KEY_TICKS * 9);
    expect(initialSlackTicks(10)).toBe(780);
  });

  it("two deaths at key 10 add their cost to the clock and leave the bank untouched", () => {
    let encounter = encounterAtKey(10);
    encounter = chargeDeath(encounter);
    encounter = chargeDeath(encounter);

    expect(encounter.penaltyTicks).toBe(2 * DEATH_SLACK_COST_TICKS);
    expect(encounter.slackTicks).toBe(780);
    expect(encounter.depleted).toBe(false);
  });

  it("a third death past the bank size still only adds time and never depletes", () => {
    let encounter = encounterAtKey(10);
    encounter = chargeDeath(encounter);
    encounter = chargeDeath(encounter);
    encounter = chargeDeath(encounter);

    expect(encounter.penaltyTicks).toBe(900);
    expect(encounter.deaths).toBe(3);
    expect(encounter.slackTicks).toBe(780);
    expect(encounter.depleted).toBe(false);
  });

  it("a wipe adds the documented cost to the clock", () => {
    const encounter = chargeWipe(encounterAtKey(10));
    expect(encounter.penaltyTicks).toBe(WIPE_SLACK_COST_TICKS);
    expect(encounter.penaltyTicks).toBe(500);
  });

  it("finish reports `depleted` once the clock with death time passes par", () => {
    let encounter = encounterAtKey(10);
    const events: SimEvent[] = [];
    const emit = (e: SimEvent) => events.push(e);
    encounter = chargeDeath(encounter);
    encounter = chargeDeath(encounter);
    encounter = chargeDeath(encounter);

    expect(finishRun(encounter, 100, true, emit)).toBe("timed");
    expect(finishRun(encounter, 10000 - 900 + 1, true, emit)).toBe("depleted");
    expect(events.at(-1)).toEqual({ tick: 9101, type: "finish", result: "depleted" });
  });

  it("finish reports `abandoned` when the run was not completed, regardless of the clock", () => {
    const encounter = encounterAtKey(10);
    const events: SimEvent[] = [];
    const result = finishRun(encounter, 100, false, (e) => events.push(e));
    expect(result).toBe("abandoned");
    expect(events).toEqual([{ tick: 100, type: "finish", result: "abandoned" }]);
  });

  it("a clean run is timed at par and depleted one tick past it", () => {
    const encounter = encounterAtKey(10);
    expect(finishRun(encounter, 10000, true, () => {})).toBe("timed");
    expect(finishRun(encounter, 10001, true, () => {})).toBe("depleted");
  });

  it("two players dying on one tick add two deaths; a repeated event and an enemy death add none", () => {
    const encounter = applyDeathsAndWipe(
      party([1, 2]),
      encounterAtKey(10),
      [death(5, 99), death(5, 1), death(5, 2), death(5, 2)],
      5,
      () => {}
    );
    expect(encounter.deaths).toBe(2);
    expect(encounter.penaltyTicks).toBe(2 * DEATH_SLACK_COST_TICKS);
  });

  it("three deaths under par at key 10 stay timed when elapsed + 45s is within par, and never emit `depleted`", () => {
    const emitted: SimEvent[] = [];
    const encounter = threeDeaths(encounterAtKey(10), emitted);

    expect(encounter.deaths).toBe(3);
    expect(encounter.depleted).toBe(false);
    expect(emitted.filter((e) => e.type === "depleted")).toHaveLength(0);
    expect(clockTicks(encounter, 7100)).toBe(8000);
    expect(finishRun(encounter, 10000 - 900, true, () => {})).toBe("timed");
    expect(finishRun(encounter, 10000 - 900 + 1, true, () => {})).toBe("depleted");
  });

  it("a Clause death cost is what each death adds to the clock", () => {
    const base = encounterAtKey(10);
    const encounter = threeDeaths({ ...base, rules: { ...base.rules, deathSlackCostTicks: 100 } }, []);

    expect(encounter.penaltyTicks).toBe(300);
    expect(finishRun(encounter, 10000 - 300, true, () => {})).toBe("timed");
    expect(finishRun(encounter, 10000 - 300 + 1, true, () => {})).toBe("depleted");
  });

  it("a full wipe adds the wipe cost on top of its deaths", () => {
    const encounter = applyDeathsAndWipe(party([1, 2, 3, 4]), encounterAtKey(10), [death(5, 4)], 5, () => {});
    expect(encounter.penaltyTicks).toBe(DEATH_SLACK_COST_TICKS + WIPE_SLACK_COST_TICKS);
  });
});
