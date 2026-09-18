import { describe, expect, it } from "vitest";
import {
  initialSlackTicks,
  spendSlackOnDeath,
  finishRun,
  DEATH_SLACK_COST_TICKS,
  SLACK_BASE_TICKS,
  SLACK_PER_KEY_TICKS,
} from "../../../sim/encounter/timer";
import { defaultEncounterRules } from "../../../sim/encounter/clauses";
import type { EncounterState } from "../../../sim/encounter/telegraphs";
import type { SimEvent } from "../../../sim/encounter/types";

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

describe("Slack", () => {
  it("key 10 starts with the documented bank: base + per-key * 9", () => {
    expect(initialSlackTicks(10)).toBe(SLACK_BASE_TICKS + SLACK_PER_KEY_TICKS * 9);
    expect(initialSlackTicks(10)).toBe(780);
  });

  it("two deaths at key 10 leave exactly the expected bank and do not deplete it", () => {
    let encounter = encounterAtKey(10);
    const emitted: SimEvent[] = [];
    const emit = (e: SimEvent) => emitted.push(e);

    encounter = spendSlackOnDeath(encounter, 1, emit);
    encounter = spendSlackOnDeath(encounter, 2, emit);

    expect(encounter.slackTicks).toBe(780 - 2 * DEATH_SLACK_COST_TICKS);
    expect(encounter.slackTicks).toBe(180);
    expect(encounter.depleted).toBe(false);
    expect(emitted).toHaveLength(0);
  });

  it("a third death depletes the bank, clamps at 0, and emits `depleted` exactly once", () => {
    let encounter = encounterAtKey(10);
    const emitted: SimEvent[] = [];
    const emit = (e: SimEvent) => emitted.push(e);

    encounter = spendSlackOnDeath(encounter, 1, emit);
    encounter = spendSlackOnDeath(encounter, 2, emit);
    encounter = spendSlackOnDeath(encounter, 3, emit);

    expect(encounter.slackTicks).toBe(0); // 180 - 300 clamps to 0, not negative
    expect(encounter.depleted).toBe(true);
    expect(emitted).toEqual([{ tick: 3, type: "depleted" }]);

    // a further death after depletion spends nothing new and does not re-emit `depleted`
    encounter = spendSlackOnDeath(encounter, 4, emit);
    expect(encounter.slackTicks).toBe(0);
    expect(emitted).toHaveLength(1);
  });

  it("finish reports `depleted` while still allowing the run to complete", () => {
    let encounter = encounterAtKey(10);
    const emit = (e: SimEvent) => events.push(e);
    const events: SimEvent[] = [];
    encounter = spendSlackOnDeath(encounter, 1, emit);
    encounter = spendSlackOnDeath(encounter, 2, emit);
    encounter = spendSlackOnDeath(encounter, 3, emit);

    const result = finishRun(encounter, 100, /* completed */ true, emit);
    expect(result).toBe("depleted");
    expect(events.at(-1)).toEqual({ tick: 100, type: "finish", result: "depleted" });
  });

  it("finish reports `abandoned` when the run was not completed, regardless of Slack", () => {
    const encounter = encounterAtKey(10);
    const events: SimEvent[] = [];
    const result = finishRun(encounter, 100, false, (e) => events.push(e));
    expect(result).toBe("abandoned");
    expect(events).toEqual([{ tick: 100, type: "finish", result: "abandoned" }]);
  });

  it("finish reports `timed` on a completed run with Slack remaining", () => {
    const encounter = encounterAtKey(10);
    const result = finishRun(encounter, 100, true, () => {});
    expect(result).toBe("timed");
  });
});
