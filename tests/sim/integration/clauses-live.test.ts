/**
 * tests/sim/integration/clauses-live.test.ts — P0 regression guard for clause-mechanics: proves
 * the four Clauses (design/BIBLE.md sec 8, sim/encounter/clauses.ts) actually change a real run's
 * outcome through the public composition root (`createRun(..., { clauseId })` + `stepRun`), not
 * just their own pure unit functions in isolation. Before this fix, `driftTelegraphs`,
 * `shouldSilence`, `applyPactDamageBp`, and `pendingEchoSpawns` were exported but NOTHING on the
 * tick path called them (verified: zero call sites in sim/encounter/index.ts, sim/combat/
 * casting.ts, damage.ts, rules.ts, sim/index.ts, sim/run-wiring.ts) -- picking a Clause changed
 * `encounter.rules` and nothing else observable.
 *
 * Every scenario below runs the SAME seed with and without the Clause active and diffs the real
 * SimEvent/state output, over up to 600 ticks, through createRun/stepRun only.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import type { DungeonDef } from "@sim/encounter/pulls";
import type { AbilityDef } from "@sim/combat/abilities";
import { startCast } from "@sim/combat/casting";
import { setActiveEncounterRules } from "@sim/combat/clause-hooks";
import { applyClause, defaultEncounterRules, type ClauseDef } from "@sim/encounter/clauses";

const SEED = 90210;
const TICKS = 600;

function runTicks(run: Run, commands: Command[], ticks: number): { run: Run; events: SimEvent[] } {
  const events: SimEvent[] = [];
  let current = run;
  while (current.state.tick < ticks) {
    const tick = current.state.tick;
    const stepped = stepRun(current, commands.filter((c) => c.tick === tick));
    current = stepped.run;
    events.push(...stepped.events);
  }
  return { run: current, events };
}

// ---------------------------------------------------------------------------------------------
// Tide: a boss telegraph's origin drifts 50 mm/tick; a stationary actor outside the original
// circle gets hit once enough drift has accumulated.
// ---------------------------------------------------------------------------------------------
describe("Tide: telegraph origin drifts and hits an actor outside the original circle", () => {
  const PLAYER_ID = 1;
  const DUMMY_ID = 2; // stationary: never issued a command, never moves
  const BOSS_ID = 100;

  const WAVE: AbilityDef = {
    id: "wave",
    castTicks: 0,
    gcdTicks: 0,
    cooldownTicks: 0,
    charges: 1,
    cost: { resource: "energy", amount: 0 },
    range: 0,
    shape: { kind: "circle", radius: 1000 },
    interruptible: false,
    effects: [{ op: "damage", amount: 1, school: "physical" }],
  };

  const DUNGEON: DungeonDef = {
    id: "tide-pull",
    // speedMmPerTick 0: the boss stays at the origin so only Tide's drift moves the circle (a damage circle is an
    // engaging cast since CHASE-AREA-REACH, and a boss free to walk would chase the dummy into its own circle).
    pulls: [{ id: "boss", spawns: [{ actorId: BOSS_ID, classId: "boss", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 5000, speedMmPerTick: 0 }] }],
  };

  function content(withClauses: boolean): RunContent {
    const base: RunContent = {
      abilities: { wave: WAVE },
      effects: {},
      lockoutTicks: 40,
      baseSpeedMmPerTick: 180,
      actorRadiusMm: 400,
      bounds: { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 },
      enemyScripts: {
        [BOSS_ID]: [{ at: "loop", every: 300, cast: "wave", telegraph: { shape: "circle", radius: 1000, leadTicks: 50 } }],
      },
    };
    if (!withClauses) return base;
    const TIDE: ClauseDef = { id: "tide", name: "Tide", kind: "tide", params: { driftMmPerTick: 50 }, description: "d" };
    return { ...base, clauses: { tide: TIDE } };
  }

  const PARTY: PartyMemberSpec[] = [
    { id: PLAYER_ID, classId: "warrior", role: "damage", pos: { x: -3000, y: 0, z: 0 }, maxHealth: 200, maxResource: 100 },
    { id: DUMMY_ID, classId: "target-dummy", role: "damage", pos: { x: 2500, y: 0, z: 0 }, maxHealth: 300, maxResource: 0 },
  ];

  it("with no clause: the stationary actor at x=2500 is never inside the boss's r=1000 circle", () => {
    const run = createRun(SEED, PARTY, DUNGEON, content(false));
    const { events } = runTicks(run, [], TICKS);
    const hits = events.filter((e) => e.type === "telegraph_hit");
    expect(hits.length).toBeGreaterThan(0);
    for (const hit of hits) expect((hit.actorIds as number[]).includes(DUMMY_ID)).toBe(false);
  });

  it("with Tide active: the same stationary actor gets hit once the drifted circle reaches it", () => {
    const run = createRun(SEED, PARTY, DUNGEON, content(true), { clauseId: "tide" });
    const { events } = runTicks(run, [], TICKS);
    const hits = events.filter((e) => e.type === "telegraph_hit");
    expect(hits.length).toBeGreaterThan(0);
    expect(hits.some((hit) => (hit.actorIds as number[]).includes(DUMMY_ID))).toBe(true);
  });
});

// ---------------------------------------------------------------------------------------------
// Hush: a cast started inside the recurring 40-tick silence window is refused (silenced); the
// same cast outside the window succeeds. Compared against a no-clause run with the same seed.
// ---------------------------------------------------------------------------------------------
describe("Hush: a cast inside the silence window is refused; the same cast outside it succeeds", () => {
  const PLAYER_ID = 1;
  const ENEMY_ID = 100;

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

  const DUNGEON: DungeonDef = {
    id: "hush-pull",
    pulls: [{ id: "boss", spawns: [{ actorId: ENEMY_ID, classId: "boss", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 100000 }] }],
  };

  const PARTY: PartyMemberSpec[] = [{ id: PLAYER_ID, classId: "warrior", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 200, maxResource: 100 }];

  function content(withClauses: boolean): RunContent {
    const base: RunContent = {
      abilities: { zap: ZAP },
      effects: {},
      lockoutTicks: 40,
      baseSpeedMmPerTick: 180,
      actorRadiusMm: 400,
      bounds: { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 },
      enemyScripts: {},
    };
    if (!withClauses) return base;
    const HUSH: ClauseDef = { id: "hush", name: "Hush", kind: "hush", params: { intervalTicks: 100, silenceDurationTicks: 40 }, description: "d" };
    return { ...base, clauses: { hush: HUSH } };
  }

  // window: ticks [100, 140). 110 is inside; 150 is outside.
  const commands: Command[] = [
    { tick: 110, actorId: PLAYER_ID, kind: "cast", abilityId: "zap", targetId: ENEMY_ID },
    { tick: 150, actorId: PLAYER_ID, kind: "cast", abilityId: "zap", targetId: ENEMY_ID },
  ];

  it("with no clause: both the in-window-tick and out-of-window-tick casts land", () => {
    const run = createRun(SEED, PARTY, DUNGEON, content(false));
    const { events } = runTicks(run, commands, 200);
    expect(events.filter((e) => e.type === "damage").length).toBe(2);
  });

  it("with Hush active: only the tick-150 cast (outside the window) lands", () => {
    const run = createRun(SEED, PARTY, DUNGEON, content(true), { clauseId: "hush" });
    const { events } = runTicks(run, commands, 200);
    const damageEvents = events.filter((e) => e.type === "damage");
    expect(damageEvents).toHaveLength(1);
    expect(damageEvents[0]?.tick).toBe(150);
  });

  it("startCast itself returns the \"silenced\" reason when Hush's window is active (precise reason check)", () => {
    const run = createRun(SEED, PARTY, DUNGEON, content(true), { clauseId: "hush" });
    setActiveEncounterRules(run.encounter.rules);
    const caster = run.state.actors[PLAYER_ID]!;
    const err = startCast(run.state, caster, ZAP, ENEMY_ID, undefined, 110);
    expect(err).toBe("silenced");
    const okErr = startCast(run.state, caster, ZAP, ENEMY_ID, undefined, 150);
    expect(okErr).toBeNull();
    setActiveEncounterRules(undefined);
  });
});

// ---------------------------------------------------------------------------------------------
// Pact: a combo hit deals +20% (basis points), a solo hit deals -10%, versus no-clause, exact
// integers.
// ---------------------------------------------------------------------------------------------
describe("Pact: combo damage is scaled up, solo damage is scaled down, exact integers", () => {
  const SETUP_ID = 1;
  const PAYOFF_ID = 2;
  const ENEMY_ID = 100;

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
  const SOLO: AbilityDef = {
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

  const DUNGEON: DungeonDef = {
    id: "pact-pull",
    pulls: [{ id: "boss", spawns: [{ actorId: ENEMY_ID, classId: "boss", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 1000000 }] }],
  };

  const PARTY: PartyMemberSpec[] = [
    { id: SETUP_ID, classId: "alpha", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 200, maxResource: 100 },
    { id: PAYOFF_ID, classId: "beta", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 200, maxResource: 100 },
  ];

  function content(withClauses: boolean): RunContent {
    const base: RunContent = {
      abilities: { open: OPEN, close: CLOSE, solo: SOLO },
      effects: {},
      lockoutTicks: 40,
      baseSpeedMmPerTick: 180,
      actorRadiusMm: 400,
      bounds: { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 },
      enemyScripts: {},
      combos: { alpha: { beta: { id: "combo1", effects: [{ op: "damage", amount: 1000, school: "physical" }] } } },
    };
    if (!withClauses) return base;
    const PACT: ClauseDef = { id: "pact", name: "Pact", kind: "pact", params: { comboDamageBp: 2000, soloDamageBp: -1000 }, description: "d" };
    return { ...base, clauses: { pact: PACT } };
  }

  const commands: Command[] = [
    { tick: 10, actorId: SETUP_ID, kind: "cast", abilityId: "open", targetId: ENEMY_ID },
    { tick: 20, actorId: PAYOFF_ID, kind: "cast", abilityId: "close", targetId: ENEMY_ID },
    { tick: 30, actorId: SETUP_ID, kind: "cast", abilityId: "solo", targetId: ENEMY_ID },
  ];

  it("with no clause: combo hit deals 1000, solo hit deals 1000", () => {
    const run = createRun(SEED, PARTY, DUNGEON, content(false));
    const { events } = runTicks(run, commands, 40);
    const comboDamage = events.find((e) => e.type === "combo");
    expect(comboDamage).toBeDefined();
    const damages = events.filter((e) => e.type === "damage").map((e) => e.healthDamage as number);
    expect(damages).toEqual([1000, 1000]);
  });

  it("with Pact active: combo hit deals 1200 (+20%), solo hit deals 900 (-10%)", () => {
    const run = createRun(SEED, PARTY, DUNGEON, content(true), { clauseId: "pact" });
    const { events } = runTicks(run, commands, 40);
    const comboDamage = events.find((e) => e.type === "combo");
    expect(comboDamage).toBeDefined();
    const damages = events.filter((e) => e.type === "damage").map((e) => e.healthDamage as number);
    expect(damages).toEqual([1200, 900]);
  });
});

// ---------------------------------------------------------------------------------------------
// Echo: an interrupted enemy cast queues a delayed weak-add spawn (600 HP, 40 ticks later) and
// the add appears in state.
// ---------------------------------------------------------------------------------------------
describe("Echo: an interrupted cast queues a delayed weak-add spawn that appears in state", () => {
  const PLAYER_ID = 1;
  const ENEMY_ID = 100;

  const ENEMY_CAST: AbilityDef = {
    id: "enemycast",
    castTicks: 30,
    gcdTicks: 0,
    cooldownTicks: 0,
    charges: 1,
    cost: { resource: "energy", amount: 0 },
    range: 100000,
    shape: { kind: "target" },
    interruptible: true,
    effects: [{ op: "damage", amount: 50, school: "physical" }],
  };
  const KICK: AbilityDef = {
    id: "kick",
    castTicks: 0,
    gcdTicks: 0,
    cooldownTicks: 0,
    charges: 1,
    cost: { resource: "energy", amount: 0 },
    range: 100000,
    shape: { kind: "target" },
    interruptible: false,
    effects: [{ op: "interrupt" }],
  };

  const DUNGEON: DungeonDef = {
    id: "echo-pull",
    pulls: [{ id: "boss", spawns: [{ actorId: ENEMY_ID, classId: "boss", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 5000 }] }],
  };

  const PARTY: PartyMemberSpec[] = [{ id: PLAYER_ID, classId: "warrior", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 200, maxResource: 100 }];

  function content(withClauses: boolean): RunContent {
    const base: RunContent = {
      abilities: { enemycast: ENEMY_CAST, kick: KICK },
      effects: {},
      lockoutTicks: 40,
      baseSpeedMmPerTick: 180,
      actorRadiusMm: 400,
      bounds: { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 },
      enemyScripts: {},
    };
    if (!withClauses) return base;
    const ECHO: ClauseDef = { id: "echo", name: "Echo", kind: "echo", params: { addMaxHealth: 600, spawnDelayTicks: 40 }, description: "d" };
    return { ...base, clauses: { echo: ECHO } };
  }

  // interrupt lands at tick 15 (< castEndsTick 45), strictly before completion -> succeeds.
  const commands: Command[] = [
    { tick: 10, actorId: ENEMY_ID, kind: "cast", abilityId: "enemycast", targetId: PLAYER_ID },
    { tick: 15, actorId: PLAYER_ID, kind: "cast", abilityId: "kick", targetId: ENEMY_ID },
  ];

  function countEchoAdds(run: Run): number {
    return Object.values(run.state.actors).filter((a) => a.classId === "echo-add").length;
  }

  it("with no clause: the interrupt lands but no add ever spawns", () => {
    const run = createRun(SEED, PARTY, DUNGEON, content(false));
    const { run: finalRun, events } = runTicks(run, commands, TICKS);
    expect(events.some((e) => e.type === "interrupt")).toBe(true);
    expect(events.some((e) => e.type === "echo_spawn")).toBe(false);
    expect(countEchoAdds(finalRun)).toBe(0);
  });

  it("with Echo active: the interrupt queues a spawn that realizes exactly 40 ticks later (tick 55), 600 HP", () => {
    const run = createRun(SEED, PARTY, DUNGEON, content(true), { clauseId: "echo" });

    let current = run;
    let spawnTick: number | null = null;
    while (current.state.tick < TICKS) {
      const tick = current.state.tick;
      const stepped = stepRun(current, commands.filter((c) => c.tick === tick));
      current = stepped.run;
      if (spawnTick === null && stepped.events.some((e) => e.type === "echo_spawn")) spawnTick = current.state.tick;
      if (spawnTick !== null) break;
    }

    expect(spawnTick).toBe(55);
    expect(countEchoAdds(current)).toBe(1);
    const add = Object.values(current.state.actors).find((a) => a.classId === "echo-add");
    expect(add?.maxHealth).toBe(600);
    expect(add?.health).toBe(600);
    expect(add?.alive).toBe(true);
  });
});
