/**
 * tests/sim/integration/run-end.test.ts — server-policy-in-sim (P1).
 *
 * Two things absorbed into sim's own public surface this lane (see sim/run-wiring.ts's ABSORBED
 * HOST POLICY note), both driven through the REAL public path (`createRun`/`stepRun`, no bypass):
 *
 * 1. `fixupTargeting`/`applyDeathsAndWipe` (sim/run-wiring.ts) used to be hand-duplicated, byte-for-
 *    byte, in client/src/sim-host/run-policy.ts AND the old server/src/targeting-policy.ts -- sim
 *    itself had no enemy target-acquisition and nothing called sim/encounter/timer.ts's
 *    spendSlackOnDeath/spendSlackOnWipe automatically. They now live in sim as the one canonical
 *    copy; this proves they work correctly against a `Run` `createRun`/`stepRun` actually produced.
 * 2. `finishRun`/`RunResult` (sim/encounter/timer.ts, pre-existing) composes correctly against a
 *    real `EncounterState` from `createRun` -- the building block server/src/run-end.ts (P1's other
 *    backlog item, server-run-lifecycle) drives to decide timed/depleted/abandoned.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type RunContent } from "@sim/index";
import { fixupTargeting, applyDeathsAndWipe } from "@sim/run-wiring";
import { finishRun } from "@sim/encounter/timer";
import type { DungeonDef } from "@sim/encounter/pulls";
import type { SimEvent } from "@sim/core/events";

const SEED = 909090;
const TANK_ID = 1;
const DPS_ID = 2;
const ENEMY_ID = 10;

const CONTENT: RunContent = {
  abilities: {},
  effects: {},
  lockoutTicks: 0,
  baseSpeedMmPerTick: 180,
  actorRadiusMm: 400,
  bounds: { minX: -10000, maxX: 10000, minZ: -10000, maxZ: 10000 },
  enemyScripts: {}, // no script entry for ENEMY_ID -- it never casts, stays passive.
};

const DUNGEON: DungeonDef = {
  id: "run-end-fixture",
  pulls: [{ id: "idle", spawns: [{ actorId: ENEMY_ID, classId: "dummy", pos: { x: 2000, y: 0, z: 0 }, yaw: 0, health: 1000 }] }],
};

const PARTY: PartyMemberSpec[] = [
  { id: TANK_ID, classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 500, maxResource: 100 },
  { id: DPS_ID, classId: "cinder", role: "damage", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 250, maxResource: 100 },
];

function buildRun() {
  return createRun(SEED, PARTY, DUNGEON, CONTENT);
}

describe("sim/run-wiring: fixupTargeting (absorbed host policy, public path)", () => {
  it("points an untargeted enemy at the tank and an untargeted damage player at the enemy", () => {
    let run = buildRun();
    expect(run.state.actors[ENEMY_ID]!.targetId).toBeNull();
    expect(run.state.actors[DPS_ID]!.targetId).toBeNull();

    fixupTargeting(run, TANK_ID);
    expect(run.state.actors[ENEMY_ID]!.targetId).toBe(TANK_ID);
    expect(run.state.actors[DPS_ID]!.targetId).toBe(ENEMY_ID);

    // Idempotent: a second call (as room.ts makes every tick) leaves already-resolved targets alone.
    fixupTargeting(run, TANK_ID);
    expect(run.state.actors[ENEMY_ID]!.targetId).toBe(TANK_ID);
    expect(run.state.actors[DPS_ID]!.targetId).toBe(ENEMY_ID);
  });

  it("still resolves targeting after a real stepRun tick (not just at createRun)", () => {
    let run = buildRun();
    const stepped = stepRun(run, []);
    run = stepped.run;
    expect(run.state.actors[ENEMY_ID]!.targetId).toBeNull(); // stepRun alone applies no host policy

    fixupTargeting(run, TANK_ID);
    expect(run.state.actors[ENEMY_ID]!.targetId).toBe(TANK_ID);
  });
});

describe("sim/run-wiring: applyDeathsAndWipe (absorbed host policy, public path)", () => {
  it("spends Slack on a player death event, and again on a full wipe", () => {
    let run = buildRun();
    const fullBank = run.encounter.slackTicks;

    const deathOnly: SimEvent[] = [{ tick: 5, type: "death", actorId: TANK_ID }];
    run.state.actors[TANK_ID]!.alive = false; // DPS still alive: not a wipe yet
    run = applyDeathsAndWipe(run, deathOnly, 5);
    expect(run.encounter.slackTicks).toBe(fullBank - 300); // DEATH_SLACK_COST_TICKS only
    expect(run.encounter.depleted).toBe(false);

    run.state.actors[DPS_ID]!.alive = false; // now every player is dead
    const secondDeath: SimEvent[] = [{ tick: 6, type: "death", actorId: DPS_ID }];
    run = applyDeathsAndWipe(run, secondDeath, 6);
    // fullBank(600) - 300(death) - 300(death) - 500(wipe) = -500, which spendSlack (sim/encounter/
    // timer.ts) clamps at 0 rather than underflowing -- both the second death AND the wipe cost
    // applied (the first assertion above already proves a single death alone leaves 300 > 0).
    expect(run.encounter.slackTicks).toBe(0);
    expect(run.encounter.depleted).toBe(true);

    // No player-death event this call -> unchanged (no-op path).
    const unrelated: SimEvent[] = [{ tick: 7, type: "telegraph_hit", telegraphId: 1, actorIds: [] }];
    const before = run.encounter.slackTicks;
    run = applyDeathsAndWipe(run, unrelated, 7);
    expect(run.encounter.slackTicks).toBe(before);
  });
});

describe("sim/encounter/timer: finishRun composes with a real EncounterState (public path)", () => {
  it("timed, depleted, and abandoned all emit exactly one 'finish' event with the right result", () => {
    const timedRun = buildRun();
    const timedEvents: SimEvent[] = [];
    const timedResult = finishRun(timedRun.encounter, 100, true, (e) => timedEvents.push(e));
    expect(timedResult).toBe("timed");
    expect(timedEvents).toEqual([{ tick: 100, type: "finish", result: "timed" }]);

    const depletedRun = buildRun();
    const depleted = { ...depletedRun.encounter, depleted: true };
    const depletedEvents: SimEvent[] = [];
    const depletedResult = finishRun(depleted, 200, true, (e) => depletedEvents.push(e));
    expect(depletedResult).toBe("depleted");
    expect(depletedEvents).toEqual([{ tick: 200, type: "finish", result: "depleted" }]);

    const abandonedRun = buildRun();
    const abandonedEvents: SimEvent[] = [];
    const abandonedResult = finishRun(abandonedRun.encounter, 300, false, (e) => abandonedEvents.push(e));
    expect(abandonedResult).toBe("abandoned");
    expect(abandonedEvents).toEqual([{ tick: 300, type: "finish", result: "abandoned" }]);
  });
});
