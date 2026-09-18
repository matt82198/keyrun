/**
 * Tests for sim/encounter/clauses.ts: the four Clauses (design/BIBLE.md §8) as run modifiers.
 * Clause params mirror content/data/clauses.json exactly (Echo addMaxHealth:600/spawnDelayTicks:40,
 * Tide driftMmPerTick:50, Hush intervalTicks:900/silenceDurationTicks:40, Pact comboDamageBp:2000/
 * soloDamageBp:-1000) so a drift in that data would be caught here.
 */
import { describe, expect, it } from "vitest";
import {
  applyClause,
  applyPactDamageBp,
  ClauseAlreadyActiveError,
  defaultEncounterRules,
  driftTelegraphOrigin,
  driftTelegraphs,
  hashEncounterRules,
  pendingEchoSpawns,
  shouldSilence,
  type ClauseDef,
} from "../../../sim/encounter/clauses";
import { pointInCircle } from "../../../sim/encounter/telegraphs";
import type { Telegraph } from "../../../sim/encounter/telegraphs";
import type { SimEvent } from "../../../sim/encounter/types";

const ECHO: ClauseDef = {
  id: "echo",
  name: "Echo",
  kind: "echo",
  params: { addMaxHealth: 600, spawnDelayTicks: 40 },
  description: "Every interrupted cast spawns a weak add.",
};
const TIDE: ClauseDef = {
  id: "tide",
  name: "Tide",
  kind: "tide",
  params: { driftMmPerTick: 50 },
  description: "Floor patches drift slowly across the arena.",
};
const HUSH: ClauseDef = {
  id: "hush",
  name: "Hush",
  kind: "hush",
  params: { intervalTicks: 900, silenceDurationTicks: 40 },
  description: "A silence pulse hits the party every 45 seconds.",
};
const PACT: ClauseDef = {
  id: "pact",
  name: "Pact",
  kind: "pact",
  params: { comboDamageBp: 2000, soloDamageBp: -1000 },
  description: "Combos deal more; uncombo'd hits deal less.",
};

const circleTelegraph: Telegraph = {
  id: 1,
  sourceId: 99,
  abilityId: "wave",
  shape: "circle",
  origin: { x: 0, y: 0, z: 0 },
  yaw: 0,
  radius: 1000,
  resolvesTick: 100,
};
const standingActor = { x: 1200, y: 0, z: 0 };

describe("Clauses: no-clause baseline", () => {
  it("defaultEncounterRules carries no active clause and zeroed clause knobs", () => {
    const rules = defaultEncounterRules(5);
    expect(rules.activeClauseId).toBeNull();
    expect(rules.activeClauseKind).toBeNull();
    expect(rules.driftMmPerTick).toBe(0);
    expect(rules.silenceIntervalTicks).toBe(0);
    expect(rules.comboDamageBp).toBe(0);
    expect(rules.echoAddMaxHealth).toBe(0);
  });

  it("no-op clause effects leave stepped-pull data unchanged", () => {
    const rules = defaultEncounterRules(1);
    expect(driftTelegraphOrigin(circleTelegraph, rules)).toEqual(circleTelegraph);
    expect(shouldSilence(900, rules)).toBe(false);
    expect(applyPactDamageBp(1000, true, rules)).toBe(1000);
    expect(pendingEchoSpawns([{ tick: 100, type: "interrupt", sourceId: 1, targetId: 2 }], rules)).toEqual([]);
  });
});

describe("Clauses: exactly one enforced", () => {
  it("applyClause sets activeClauseId and activeClauseKind", () => {
    const rules = applyClause(defaultEncounterRules(1), ECHO);
    expect(rules.activeClauseId).toBe("echo");
    expect(rules.activeClauseKind).toBe("echo");
  });

  it("applying a second clause throws ClauseAlreadyActiveError", () => {
    const withEcho = applyClause(defaultEncounterRules(1), ECHO);
    expect(() => applyClause(withEcho, TIDE)).toThrow(ClauseAlreadyActiveError);
    expect(() => applyClause(withEcho, TIDE)).toThrow(/echo.*tide/);
  });

  it("is pure: the input rules object is never mutated", () => {
    const base = defaultEncounterRules(1);
    applyClause(base, PACT);
    expect(base.activeClauseId).toBeNull();
    expect(base.comboDamageBp).toBe(0);
  });
});

describe("Echo: interrupted cast spawns a weak add", () => {
  it("emits one delayed spawn request per interrupt event", () => {
    const rules = applyClause(defaultEncounterRules(1), ECHO);
    const events: SimEvent[] = [
      { tick: 100, type: "interrupt", sourceId: 1, targetId: 2, abilityId: "cast-a", school: 0 },
      { tick: 100, type: "damage", sourceId: 1, targetId: 2, amount: 50 },
    ];
    const spawns = pendingEchoSpawns(events, rules);
    expect(spawns).toEqual([{ spawnAtTick: 140, sourceActorId: 2, maxHealth: 600 }]);
  });

  it("a non-echo active clause produces no spawns even on an interrupt event", () => {
    const rules = applyClause(defaultEncounterRules(1), TIDE);
    const events: SimEvent[] = [{ tick: 100, type: "interrupt", sourceId: 1, targetId: 2 }];
    expect(pendingEchoSpawns(events, rules)).toEqual([]);
  });
});

describe("Tide: floor patches drift", () => {
  it("drifts a telegraph's origin by driftMmPerTick per call, changing a stepped pull's hit-test", () => {
    const rules = applyClause(defaultEncounterRules(1), TIDE);
    expect(pointInCircle(circleTelegraph.origin, circleTelegraph.radius ?? 0, standingActor)).toBe(false);

    let drifted = circleTelegraph;
    for (let i = 0; i < 4; i++) drifted = driftTelegraphOrigin(drifted, rules);
    expect(drifted.origin).toEqual({ x: 200, y: 0, z: 0 });
    expect(pointInCircle(drifted.origin, drifted.radius ?? 0, standingActor)).toBe(true);
  });

  it("driftTelegraphs maps a whole registry and leaves shape/radius untouched", () => {
    const rules = applyClause(defaultEncounterRules(1), TIDE);
    const [drifted] = driftTelegraphs([circleTelegraph], rules);
    expect(drifted).toBeDefined();
    expect(drifted?.origin.x).toBe(50);
    expect(drifted?.radius).toBe(1000);
  });
});

describe("Hush: silence pulses every 45s (900 ticks), 40 ticks long", () => {
  it("is silent from tick 900 through 939, not before or after", () => {
    const rules = applyClause(defaultEncounterRules(1), HUSH);
    expect(shouldSilence(899, rules)).toBe(false);
    expect(shouldSilence(900, rules)).toBe(true);
    expect(shouldSilence(939, rules)).toBe(true);
    expect(shouldSilence(940, rules)).toBe(false);
  });

  it("never fires on tick 0 and repeats on the second pulse at tick 1800", () => {
    const rules = applyClause(defaultEncounterRules(1), HUSH);
    expect(shouldSilence(0, rules)).toBe(false);
    expect(shouldSilence(1799, rules)).toBe(false);
    expect(shouldSilence(1800, rules)).toBe(true);
    expect(shouldSilence(1839, rules)).toBe(true);
    expect(shouldSilence(1840, rules)).toBe(false);
  });
});

describe("Pact: Combos deal more, uncombo'd hits deal less", () => {
  it("scales a combo hit up 20% and a solo hit down 10%, integer truncated", () => {
    const rules = applyClause(defaultEncounterRules(1), PACT);
    expect(applyPactDamageBp(1000, true, rules)).toBe(1200);
    expect(applyPactDamageBp(1000, false, rules)).toBe(900);
    expect(applyPactDamageBp(333, true, rules)).toBe(399);
  });
});

describe("Clauses: state hash changes with the active clause", () => {
  it("is deterministic (same rules -> same hash, repeated calls)", () => {
    const rules = applyClause(defaultEncounterRules(3), PACT);
    const h1 = hashEncounterRules(rules);
    const h2 = hashEncounterRules(rules);
    const h3 = hashEncounterRules(applyClause(defaultEncounterRules(3), PACT));
    expect(h1).toBe(h2);
    expect(h1).toBe(h3);
  });

  it("differs across no-clause and each of the four clauses", () => {
    const base = defaultEncounterRules(1);
    const hashes = new Set([
      hashEncounterRules(base),
      hashEncounterRules(applyClause(base, ECHO)),
      hashEncounterRules(applyClause(base, TIDE)),
      hashEncounterRules(applyClause(base, HUSH)),
      hashEncounterRules(applyClause(base, PACT)),
    ]);
    expect(hashes.size).toBe(5);
  });
});
