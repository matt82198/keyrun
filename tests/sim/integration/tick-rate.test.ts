/**
 * tests/sim/integration/tick-rate.test.ts — pins the fix for the double-tick defect: every real
 * `stepRun()` call must advance `run.state.tick` by exactly 1, matching TICK_HZ (20 Hz / 50ms per
 * tick, sim/core/tick.ts). Before the fix, sim/combat/rules.ts's `StepRules` also incremented
 * `state.tick` internally, and sim/core/tick.ts's `step()` incremented it again on the same
 * mutated object, so every `stepRun()` silently advanced the clock by 2 -- run timers, cooldowns,
 * cast times, Slack and Tempo all ran at double speed relative to TICK_HZ. Content tick constants
 * happen to be multiples of 10, so the doubling was invisible to hash/behaviour assertions that
 * only ever sampled even ticks.
 *
 * sim/core/tick.ts's `step()` is the single owner of the tick (see sim/CLAUDE.md and that file's
 * doc comment); sim/combat/rules.ts must never advance `state.tick` itself.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import { hashState } from "@sim/core/state";
import type { DungeonDef } from "@sim/encounter/pulls";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import type { AbilityDef } from "@sim/combat/abilities";
import { loadContent } from "@content/index";

const PLAYER_ID = 1;
const TARGET_ID = 2;

const EMPTY_DUNGEON: DungeonDef = { id: "tick-rate-empty", pulls: [{ id: "none", spawns: [] }] };

const LONGCAST: AbilityDef = {
  id: "longcast",
  castTicks: 20,
  gcdTicks: 20,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: true,
  effects: [{ op: "damage", amount: 10, school: "physical" }],
};

const PARTY: PartyMemberSpec[] = [
  { id: PLAYER_ID, classId: "warden", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 2000, maxResource: 150 },
  { id: TARGET_ID, classId: "warden", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 2000, maxResource: 150 },
];

/** Real ability/effect registry (content/index.ts's loadContent), so this pins the defect against
 * the actual production content, not a hand-rolled stand-in. `longcast` is added on top for the
 * cast-timing assertion below, which needs a known, exact castTicks value. */
function buildRunContent(): RunContent {
  const reg = loadContent();
  return {
    abilities: { ...Object.fromEntries(reg.abilities), longcast: LONGCAST },
    effects: Object.fromEntries(reg.effects),
    lockoutTicks: 80,
    baseSpeedMmPerTick: 180,
    actorRadiusMm: 400,
    bounds: { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 },
    enemyScripts: {},
  };
}

function freshRun(): Run {
  return createRun(1, PARTY, EMPTY_DUNGEON, buildRunContent());
}

describe("tick rate: stepRun advances state.tick by exactly TICK_HZ's one tick per call", () => {
  it("N stepRun calls with the real content registry advance state.tick to exactly N, not 2N", () => {
    let run = freshRun();
    const N = 50;
    for (let i = 0; i < N; i += 1) {
      run = stepRun(run, []).run;
    }
    expect(run.state.tick).toBe(N);
  });

  it("a 20-tick cast completes exactly 20 stepRun calls after the call that started it", () => {
    let run = freshRun();
    const startCmd: Command = { tick: run.state.tick, actorId: PLAYER_ID, kind: "cast", abilityId: "longcast", targetId: TARGET_ID };
    const started = stepRun(run, [startCmd]);
    run = started.run;
    expect(run.state.actors[PLAYER_ID]!.castId).toBe("longcast");

    let completedAtCall = -1;
    let events: SimEvent[] = [];
    for (let i = 1; i <= 20; i += 1) {
      const stepped = stepRun(run, []);
      run = stepped.run;
      events = stepped.events;
      if (events.some((e) => e.type === "cast_complete" && e.abilityId === "longcast")) {
        completedAtCall = i;
        break;
      }
      // The cast must still be in flight before its 20th tick -- if it completed earlier (or
      // castId cleared early) the tick rate is running fast.
      expect(run.state.actors[PLAYER_ID]!.castId).toBe("longcast");
    }

    expect(completedAtCall).toBe(20);
    expect(run.state.actors[PLAYER_ID]!.castId).toBeNull();
  });

  it("two independent runs with the same seed and command stream hash equal", () => {
    const commands: Command[] = [
      { tick: 5, actorId: PLAYER_ID, kind: "cast", abilityId: "longcast", targetId: TARGET_ID },
      { tick: 40, actorId: PLAYER_ID, kind: "cast", abilityId: "longcast", targetId: TARGET_ID },
    ];

    function run60Ticks(): Run {
      let run = freshRun();
      for (let t = 0; t < 60; t += 1) {
        const tickCommands = commands.filter((c) => c.tick === t);
        run = stepRun(run, tickCommands).run;
      }
      return run;
    }

    const a = run60Ticks();
    const b = run60Ticks();
    expect(hashState(a.state)).toBe(hashState(b.state));
    expect(a.state.tick).toBe(60);
  });
});
