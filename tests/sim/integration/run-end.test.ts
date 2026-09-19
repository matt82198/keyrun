/**
 * tests/sim/integration/run-end.test.ts — policy-inside-stepRun (P2).
 *
 * fixupTargeting/applyDeathsAndWipe/run-end detection (sim/run-wiring.ts) are no longer host-called
 * -- `createRun`/`stepRun` alone apply them every tick (see sim/index.ts's stepRun docstring). Every
 * assertion below drives ONLY the public path: `createRun` + repeated `stepRun` calls, exactly what
 * server/src/room.ts and client/src/sim-host/sim-runtime.ts do. No test imports run-wiring.ts's
 * helpers directly -- if it worked, it worked through the one real call site.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type RunContent } from "@sim/index";
import { WIPE_GRACE_TICKS } from "@sim/run-wiring";
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
  pulls: [{ id: "the-only-pull", spawns: [{ actorId: ENEMY_ID, classId: "dummy", pos: { x: 2000, y: 0, z: 0 }, yaw: 0, health: 1000 }] }],
};

const PARTY: PartyMemberSpec[] = [
  { id: TANK_ID, classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 500, maxResource: 100 },
  { id: DPS_ID, classId: "cinder", role: "damage", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 250, maxResource: 100 },
];

function buildRun() {
  return createRun(SEED, PARTY, DUNGEON, CONTENT);
}

describe("createRun: targeting resolves at creation, with no host call", () => {
  it("points the enemy at the tank and the damage player at the enemy immediately", () => {
    const run = buildRun();
    expect(run.state.actors[ENEMY_ID]!.targetId).toBe(TANK_ID);
    expect(run.state.actors[DPS_ID]!.targetId).toBe(ENEMY_ID);
  });
});

describe("stepRun: targeting fixup stays resolved every tick, with no host call", () => {
  it("keeps re-resolving after a real stepRun tick even if a target is cleared", () => {
    let run = buildRun();
    run.state.actors[ENEMY_ID]!.targetId = null; // simulate combat clearing a dead target's slot
    const stepped = stepRun(run, []);
    run = stepped.run;
    expect(run.state.actors[ENEMY_ID]!.targetId).toBe(TANK_ID); // stepRun alone re-applies it
  });
});

describe("stepRun: a full wipe ends the run as 'depleted', through stepRun alone", () => {
  it("auto-ends after WIPE_GRACE_TICKS with a 'run_end' SimEvent, exactly once, and 'depleted' true", () => {
    let run = buildRun();
    // Killing actors directly (rather than through real combat damage, which this bare-bones
    // fixture content has no abilities to deal) mirrors tests/server/run-end.test.ts's own
    // "forcing a wipe through real combat would be slow and content-balance-dependent" approach.
    for (const actor of Object.values(run.state.actors)) {
      if (actor.kind === "player") actor.alive = false;
    }

    let allEvents: SimEvent[] = [];
    for (let i = 0; i < WIPE_GRACE_TICKS + 5; i++) {
      const stepped = stepRun(run, []);
      run = stepped.run;
      allEvents = allEvents.concat(stepped.events);
    }

    // A full wipe is always reported "depleted", never "abandoned" -- see run-wiring.ts's
    // checkRunEnd docstring: an unrecoverable wipe is a failure to finish, not a voluntary quit,
    // regardless of whether this particular wipe's deaths happened to route Slack to 0 through
    // applyDeathsAndWipe's own per-death cost (these actors were killed directly, out of band, so
    // they never did) -- stepRun forces the Snapshot-facing `depleted` flag to match.
    expect(run.encounter.depleted).toBe(true);
    expect(run.runEnd.result).toBe("depleted");

    const runEndEvents = allEvents.filter((e) => e.type === "run_end");
    expect(runEndEvents).toHaveLength(1);
    expect(runEndEvents[0]).toMatchObject({ result: "depleted" });

    // Idempotent: further ticks never re-fire run_end.
    const stepped = stepRun(run, []);
    expect(stepped.events.some((e) => e.type === "run_end")).toBe(false);
  });
});

describe("stepRun: dungeon complete ends the run 'timed' or 'depleted', through stepRun alone", () => {
  it("ends 'timed' once the final pull's enemies are all dead", () => {
    let run = buildRun();
    run.state.actors[ENEMY_ID]!.alive = false;
    run.state.actors[ENEMY_ID]!.health = 0;

    const stepped = stepRun(run, []);
    run = stepped.run;
    expect(run.encounter.pullIndex).toBe(1); // advanced past the dungeon's only pull
    expect(run.runEnd.result).toBe("timed");
    expect(stepped.events.some((e) => e.type === "run_end" && e.result === "timed")).toBe(true);
  });
});

describe("stepRun: an explicit abandonRequested ends the run 'abandoned', through stepRun alone", () => {
  it("ends immediately, distinct from the auto-wipe-timeout 'depleted' path", () => {
    const run = buildRun();
    const stepped = stepRun(run, [], { abandonRequested: true });
    expect(stepped.run.runEnd.result).toBe("abandoned");
    expect(stepped.events).toContainEqual({ tick: 1, type: "finish", result: "abandoned" });
    expect(stepped.events).toContainEqual({ tick: 1, type: "run_end", result: "abandoned" });
  });
});
