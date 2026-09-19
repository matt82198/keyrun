/**
 * tests/sim/integration/interleaved-runs.test.ts — modstate-repro: proof (or refutation) that
 * sim/combat/clause-hooks.ts's `activeRules` and sim/combat/affinity.ts's `registeredEffects`
 * module-scoped registries stay correct when TWO createRun runs with DIFFERENT active Clauses are
 * stepped ALTERNATELY (A's tick N, B's tick N, A's tick N+1, ...) through the public composition
 * root only -- the same interleaving pattern server/src/lobby.ts produces when it steps several
 * rooms in one Node process (server/src/room.ts's Room.tick calls sim/index.ts's stepRun once per
 * room per timer tick).
 *
 * Run A uses Pact (combo hits +20%, solo hits -10%, exact integers -- reusing the scenario from
 * tests/sim/integration/clauses-live.test.ts's "Pact" describe block). Run B uses Hush (a cast
 * inside the recurring silence window is refused). Both are asserted against a SOLO replay: the
 * same seed/content/clauseId/commands, stepped alone with nothing else interleaved. If either
 * module-scoped registry were only set once (e.g. at makeRules-build time with no per-tick
 * refresh) rather than reset on every stepRun call, interleaving would let one run observe the
 * other's rules or effects table and this test would fail.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import type { DungeonDef } from "@sim/encounter/pulls";
import type { AbilityDef } from "@sim/combat/abilities";
import { hashState } from "@sim/core/state";
import type { ClauseDef } from "@sim/encounter/clauses";

const SEED = 90210;
const TICKS = 200;

// --- Run A: Pact -- combo hit +20%, solo hit -10% -----------------------------------------------
const A_SETUP_ID = 1;
const A_PAYOFF_ID = 2;
const A_ENEMY_ID = 100;

const OPEN: AbilityDef = {
  id: "open",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 100000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "window", windowId: "w", durationTicks: 60, onTarget: true, setupClass: "alpha" }],
};
const CLOSE: AbilityDef = {
  id: "close",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 100000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "payoff" }],
};
const SOLO_ABILITY: AbilityDef = {
  id: "solo",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 100000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "damage", amount: 1000, school: "physical" }],
};

const A_DUNGEON: DungeonDef = {
  id: "pact-pull",
  pulls: [{ id: "boss", spawns: [{ actorId: A_ENEMY_ID, classId: "boss", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 1000000 }] }],
};
const A_PARTY: PartyMemberSpec[] = [
  { id: A_SETUP_ID, classId: "alpha", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 200, maxResource: 100 },
  { id: A_PAYOFF_ID, classId: "beta", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 200, maxResource: 100 },
];
const PACT: ClauseDef = { id: "pact", name: "Pact", kind: "pact", params: { comboDamageBp: 2000, soloDamageBp: -1000 }, description: "d" };
const A_CONTENT: RunContent = {
  abilities: { open: OPEN, close: CLOSE, solo: SOLO_ABILITY },
  effects: {},
  lockoutTicks: 40,
  baseSpeedMmPerTick: 180,
  actorRadiusMm: 400,
  bounds: { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 },
  enemyScripts: {},
  combos: { alpha: { beta: { id: "combo1", effects: [{ op: "damage", amount: 1000, school: "physical" }] } } },
  clauses: { pact: PACT },
};
const A_COMMANDS: Command[] = [
  { tick: 10, actorId: A_SETUP_ID, kind: "cast", abilityId: "open", targetId: A_ENEMY_ID },
  { tick: 20, actorId: A_PAYOFF_ID, kind: "cast", abilityId: "close", targetId: A_ENEMY_ID },
  { tick: 30, actorId: A_SETUP_ID, kind: "cast", abilityId: "solo", targetId: A_ENEMY_ID },
];

// --- Run B: Hush -- a cast inside the silence window is refused, the same cast outside it lands -
const B_PLAYER_ID = 1;
const B_ENEMY_ID = 100;
const ZAP: AbilityDef = {
  id: "zap",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 100000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "damage", amount: 50, school: "physical" }],
};
const B_DUNGEON: DungeonDef = {
  id: "hush-pull",
  pulls: [{ id: "boss", spawns: [{ actorId: B_ENEMY_ID, classId: "boss", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 100000 }] }],
};
const B_PARTY: PartyMemberSpec[] = [{ id: B_PLAYER_ID, classId: "warrior", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 200, maxResource: 100 }];
const HUSH: ClauseDef = { id: "hush", name: "Hush", kind: "hush", params: { intervalTicks: 100, silenceDurationTicks: 40 }, description: "d" };
const B_CONTENT: RunContent = {
  abilities: { zap: ZAP },
  effects: {},
  lockoutTicks: 40,
  baseSpeedMmPerTick: 180,
  actorRadiusMm: 400,
  bounds: { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 },
  enemyScripts: {},
  clauses: { hush: HUSH },
};
const B_COMMANDS: Command[] = [
  { tick: 110, actorId: B_PLAYER_ID, kind: "cast", abilityId: "zap", targetId: B_ENEMY_ID },
  { tick: 150, actorId: B_PLAYER_ID, kind: "cast", abilityId: "zap", targetId: B_ENEMY_ID },
];

function soloReplay(
  party: PartyMemberSpec[],
  dungeon: DungeonDef,
  content: RunContent,
  clauseId: string,
  commands: Command[],
  ticks: number,
): { run: Run; events: SimEvent[] } {
  let run = createRun(SEED, party, dungeon, content, { clauseId });
  const events: SimEvent[] = [];
  while (run.state.tick < ticks) {
    const tick = run.state.tick;
    const stepped = stepRun(run, commands.filter((c) => c.tick === tick));
    run = stepped.run;
    events.push(...stepped.events);
  }
  return { run, events };
}

describe("interleaved runs keep their own rules (refutes module-state leak)", () => {
  it("a Pact run and a Hush run, stepped alternately, match their own solo replays", () => {
    let runA = createRun(SEED, A_PARTY, A_DUNGEON, A_CONTENT, { clauseId: "pact" });
    let runB = createRun(SEED, B_PARTY, B_DUNGEON, B_CONTENT, { clauseId: "hush" });
    const eventsA: SimEvent[] = [];
    const eventsB: SimEvent[] = [];

    while (runA.state.tick < TICKS || runB.state.tick < TICKS) {
      if (runA.state.tick < TICKS) {
        const tick = runA.state.tick;
        const stepped = stepRun(runA, A_COMMANDS.filter((c) => c.tick === tick));
        runA = stepped.run;
        eventsA.push(...stepped.events);
      }
      if (runB.state.tick < TICKS) {
        const tick = runB.state.tick;
        const stepped = stepRun(runB, B_COMMANDS.filter((c) => c.tick === tick));
        runB = stepped.run;
        eventsB.push(...stepped.events);
      }
    }

    const soloA = soloReplay(A_PARTY, A_DUNGEON, A_CONTENT, "pact", A_COMMANDS, TICKS);
    const soloB = soloReplay(B_PARTY, B_DUNGEON, B_CONTENT, "hush", B_COMMANDS, TICKS);

    expect(hashState(runA.state)).toBe(hashState(soloA.run.state));
    expect(hashState(runB.state)).toBe(hashState(soloB.run.state));

    // Sanity: Pact's rules actually applied to A (not a no-op comparison) -- combo hit 1200
    // (+20%), solo hit 900 (-10%), exactly as tests/sim/integration/clauses-live.test.ts's
    // isolated Pact scenario asserts.
    expect(eventsA.find((e) => e.type === "combo")).toBeDefined();
    const damagesA = eventsA.filter((e) => e.type === "damage").map((e) => e.healthDamage as number);
    expect(damagesA).toEqual([1200, 900]);

    // Sanity: Hush's rules actually applied to B -- only the tick-150 cast (outside the silence
    // window) landed; the tick-110 cast was silenced.
    const damagesB = eventsB.filter((e) => e.type === "damage");
    expect(damagesB).toHaveLength(1);
    expect(damagesB[0]?.tick).toBe(150);
  });
});
