/**
 * tests/sim/integration/module-state-per-run.test.ts — fix/sim-module-state-per-run: verdicts on the
 * three module-level mutable values left after #77 (telegraph ids), each driven through real code:
 *
 *  1. sim/combat/affinity.ts `registeredEffects` -- two runs whose effect tables give the SAME
 *     effect id OPPOSITE polarity, stepped alternately through stepRun, each match their solo replay
 *     (stepRun rebuilds makeRules, which re-registers, every tick). REFUTED on the stepRun path.
 *  2. sim/combat/clause-hooks.ts `comboDepth` -- a combo tick in run A immediately followed by a solo
 *     hit in run B (both Pact) never leaks the combo flag: depth is try/finally-balanced, back to 0
 *     after every stepRun and after a throw. REFUTED.
 *  3. sim/combat/clause-hooks.ts `activeRules` -- CONFIRMED: before this lane, stepRun set it and
 *     never cleared it, so the LAST stepped run's Clause stayed live process-wide; any later damage
 *     resolved outside that stepRun (a direct makeRules + core step, as several suites and bots
 *     drive) silently took the previous room's Pact weighting. Fixed by scoping the channel to the
 *     stepRun call (clause-hooks.ts's `withEncounterRules`, restore-on-exit).
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import type { AbilityDef } from "@sim/combat/abilities";
import type { EffectDef } from "@sim/combat/effects";
import type { DungeonDef } from "@sim/encounter/pulls";
import type { ClauseDef } from "@sim/encounter/clauses";
import { hashState } from "@sim/core/state";
import { step as coreStep } from "@sim/core/tick";
import { makeRules } from "@sim/combat/rules";
import { rulesConfigOf } from "@sim/run-wiring";
import { getActiveEncounterRules, isComboActive, runAsCombo } from "@sim/combat/clause-hooks";

const SEED = 4242;
const ORIGIN = { x: 0, y: 0, z: 0 };
const BOUNDS = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };
const PLAYER = 1;
const PARTNER = 2;
const ENEMY = 100;

function ability(id: string, effects: AbilityDef["effects"], shape: AbilityDef["shape"] = { kind: "target" }): AbilityDef {
  return { id, castTicks: 0, gcdTicks: 0, cooldownTicks: 0, charges: 1, cost: { resource: "energy", amount: 0 }, range: 100000, shape, interruptible: false, effects };
}

const DUNGEON: DungeonDef = {
  id: "one-pull",
  pulls: [{ id: "boss", spawns: [{ actorId: ENEMY, classId: "boss", pos: ORIGIN, yaw: 0, health: 1000000 }] }],
};
const PARTY: PartyMemberSpec[] = [
  { id: PLAYER, classId: "alpha", role: "damage", pos: ORIGIN, maxHealth: 200, maxResource: 100 },
  { id: PARTNER, classId: "beta", role: "damage", pos: ORIGIN, maxHealth: 200, maxResource: 100 },
];

function base(abilities: Record<string, AbilityDef>, effects: Record<string, EffectDef>): RunContent {
  return { abilities, effects, lockoutTicks: 40, baseSpeedMmPerTick: 180, actorRadiusMm: 400, bounds: BOUNDS, enemyScripts: {} };
}

/** Steps `runs` alternately (run 0 tick N, run 1 tick N, ...) for `ticks` ticks each. */
function interleave(runs: Run[], commands: Command[][], ticks: number, after?: (i: number) => void): { runs: Run[]; events: SimEvent[][] } {
  const events: SimEvent[][] = runs.map(() => []);
  const out = [...runs];
  for (let t = 0; t < ticks; t++) {
    out.forEach((run, i) => {
      const stepped = stepRun(run, commands[i]!.filter((c) => c.tick === run.state.tick));
      out[i] = stepped.run;
      events[i]!.push(...stepped.events);
      after?.(i);
    });
  }
  return { runs: out, events };
}

// --- 1. registeredEffects: same effect id, opposite polarity per run -------------------------------
const PULSE = ability("pulse", [{ op: "applyEffect", effectId: "mark", durationTicks: 100, stacks: 1 }], { kind: "circle", radius: 5000 });
const markDef = (damageTakenBp: number): EffectDef => ({ id: "mark", maxStacks: 1, dispellableTag: null, modifiers: { damageTakenBp }, periodic: null });
const BUFF_CONTENT = base({ pulse: PULSE }, { mark: markDef(-1000) }); // beneficial -> friendly circle
const CURSE_CONTENT = base({ pulse: PULSE }, { mark: markDef(1000) }); // hostile -> hostile circle
const PULSE_AT_10: Command[] = [{ tick: 10, actorId: PLAYER, kind: "cast", abilityId: "pulse" }];

const marked = (run: Run): number[] =>
  Object.values(run.state.actors).filter((a) => a.effects.some((e) => e.effectId === "mark")).map((a) => a.id).sort((x, y) => x - y);

// --- 2/3. Pact (combo +20%, solo -10%) ----------------------------------------------------------------
const PACT: ClauseDef = { id: "pact", name: "Pact", kind: "pact", params: { comboDamageBp: 2000, soloDamageBp: -1000 }, description: "d" };
const PACT_CONTENT: RunContent = {
  ...base(
    {
      open: ability("open", [{ op: "window", windowId: "w", durationTicks: 60, onTarget: true, setupClass: "alpha" }]),
      close: ability("close", [{ op: "payoff" }]),
      solo: ability("solo", [{ op: "damage", amount: 1000, school: "physical" }]),
    },
    {},
  ),
  combos: { alpha: { beta: { id: "combo1", effects: [{ op: "damage", amount: 1000, school: "physical" }] } } },
  clauses: { pact: PACT },
};
const COMBO_AT_20: Command[] = [
  { tick: 10, actorId: PLAYER, kind: "cast", abilityId: "open", targetId: ENEMY },
  { tick: 20, actorId: PARTNER, kind: "cast", abilityId: "close", targetId: ENEMY },
];
const SOLO_AT_20: Command[] = [{ tick: 20, actorId: PLAYER, kind: "cast", abilityId: "solo", targetId: ENEMY }];
const damages = (events: SimEvent[]): number[] => events.filter((e) => e.type === "damage").map((e) => e.healthDamage as number);

describe("module-level sim state stays per-run (two runs in one process)", () => {
  it("registeredEffects: interleaved runs with opposite-polarity 'mark' each match their solo replay", () => {
    const both = interleave([createRun(SEED, PARTY, DUNGEON, BUFF_CONTENT), createRun(SEED, PARTY, DUNGEON, CURSE_CONTENT)], [PULSE_AT_10, PULSE_AT_10], 30);
    const soloBuff = interleave([createRun(SEED, PARTY, DUNGEON, BUFF_CONTENT)], [PULSE_AT_10], 30).runs[0]!;
    const soloCurse = interleave([createRun(SEED, PARTY, DUNGEON, CURSE_CONTENT)], [PULSE_AT_10], 30).runs[0]!;
    expect(marked(both.runs[0]!)).toEqual([PLAYER, PARTNER]); // friendly: party incl. caster
    expect(marked(both.runs[1]!)).toEqual([ENEMY]); // hostile: enemies only
    expect(hashState(both.runs[0]!.state)).toBe(hashState(soloBuff.state));
    expect(hashState(both.runs[1]!.state)).toBe(hashState(soloCurse.state));
  });

  it("comboDepth: run A's combo tick never makes run B's same-tick solo hit count as a combo", () => {
    const seen: boolean[] = [];
    const { events } = interleave(
      [createRun(SEED, PARTY, DUNGEON, PACT_CONTENT, { clauseId: "pact" }), createRun(SEED, PARTY, DUNGEON, PACT_CONTENT, { clauseId: "pact" })],
      [COMBO_AT_20, SOLO_AT_20],
      30,
      () => seen.push(isComboActive()),
    );
    expect(events[0]!.some((e) => e.type === "combo")).toBe(true);
    expect(damages(events[0]!)).toEqual([1200]); // A's combo hit: +20%
    expect(damages(events[1]!)).toEqual([900]); // B's solo hit right after it: -10%, not +20%
    expect(seen.every((v) => v === false)).toBe(true);
    expect(() => runAsCombo(() => { throw new Error("boom"); })).toThrow("boom");
    expect(isComboActive()).toBe(false);
  });

  it("activeRules: a Pact room's Clause does not outlive its stepRun call", () => {
    let pact = createRun(SEED, PARTY, DUNGEON, PACT_CONTENT, { clauseId: "pact" });
    const pactEvents: SimEvent[] = [];
    while (pact.state.tick <= 20) {
      const s = stepRun(pact, SOLO_AT_20.filter((c) => c.tick === pact.state.tick));
      pact = s.run;
      pactEvents.push(...s.events);
    }
    expect(damages(pactEvents)).toEqual([900]); // Pact really applied inside its own stepRun

    // A clause-less run driven through the lower-level makeRules + core step API afterwards.
    const plain = createRun(SEED, PARTY, DUNGEON, base(PACT_CONTENT.abilities, {}));
    let state = plain.state;
    const rules = makeRules(rulesConfigOf(plain.content));
    const plainEvents: SimEvent[] = [];
    while (state.tick <= 20) {
      const s = coreStep(state, SOLO_AT_20.filter((c) => c.tick === state.tick), rules);
      state = s.state;
      plainEvents.push(...s.events);
    }
    expect(damages(plainEvents)).toEqual([1000]); // unweighted, not the previous room's -10%
    expect(getActiveEncounterRules()).toBeUndefined();
  });
});
