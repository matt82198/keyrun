import { describe, expect, it } from "vitest";
import { stepEncounter } from "../../../sim/encounter/index";
import { defaultEncounterRules } from "../../../sim/encounter/clauses";
import type { EnemyScript } from "../../../sim/encounter/scripts";
import type { EncounterState } from "../../../sim/encounter/telegraphs";
import type { DungeonDef } from "../../../sim/encounter/pulls";
import type { ActorState, SimEvent, SimState } from "../../../sim/encounter/types";

function actor(id: number, kind: "player" | "enemy", pos: { x: number; y: number; z: number }, alive = true): ActorState {
  return {
    id,
    kind,
    classId: kind === "enemy" ? "boss" : "warden",
    role: kind === "enemy" ? "none" : "tank",
    pos,
    yaw: 0,
    health: alive ? 100 : 0,
    maxHealth: 100,
    resource: 0,
    maxResource: 0,
    alive,
    targetId: null,
    castId: null,
    castEndsTick: null,
    castStartTick: null,
    cooldowns: {},
    gcdReadyTick: 0,
    effects: [],
    threat: {},
  };
}

const dungeon: DungeonDef = { id: "hollow-vault", pulls: [{ id: "choir", spawns: [] }] };
const scripts: Record<number, EnemyScript> = {
  1: [{ at: "loop", every: 100, cast: "boss_cleave", telegraph: { shape: "cone", angle1024: 200, radius: 6000, leadTicks: 0 } }],
};

function baseEncounter(): EncounterState {
  return { telegraphs: [], breakBars: {}, pullIndex: 0, slackTicks: 100, parTicks: 1000, keyLevel: 1, depleted: false, rules: defaultEncounterRules(1) };
}

describe("stepEncounter", () => {
  it("registers a telegraph and resolves it (with leadTicks 0, same tick) producing a hit list, never touching health", () => {
    const state: SimState = {
      tick: 100,
      seed: 1,
      prng: [1, 2, 3, 4],
      actors: { 1: actor(1, "enemy", { x: 0, y: 0, z: 0 }), 2: actor(2, "player", { x: 0, y: 0, z: 3000 }) },
      runTimerTicks: 100,
      phase: "combat",
      log: [],
    };
    const events: SimEvent[] = [];
    const result = stepEncounter(state, baseEncounter(), dungeon, scripts, (e) => events.push(e));

    // sim/encounter/script-target.ts's P0 fix: with no `abilities` registry passed (stepEncounter's
    // real call site doesn't have one -- see script-target.ts's WIRING NOTE), the enemy's cast now
    // always resolves a best-effort hostile target (threat/nearest living player) here that's the
    // only player actor in this fixture, id 2 -- rather than the pre-fix, never-lands, target-less
    // command this test used to pin.
    expect(result.commands).toEqual([{ tick: 100, actorId: 1, kind: "cast", abilityId: "boss_cleave", targetId: 2 }]);
    expect(events.some((e) => e.type === "telegraph")).toBe(true);
    const hit = events.find((e) => e.type === "telegraph_hit");
    expect(hit?.actorIds).toEqual([2]);
    // encounter's registry is empty again: the telegraph resolved and was pruned this same tick
    expect(result.encounter.telegraphs).toHaveLength(0);
    // no ActorState in `state` was mutated
    expect(result.state.actors[2]?.health).toBe(100);
  });

  it("advances pullIndex once the active pull's enemies are all dead", () => {
    const dungeonWithPull: DungeonDef = {
      id: "d",
      pulls: [{ id: "p0", spawns: [{ actorId: 5, classId: "x", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 100 }] }],
    };
    const state: SimState = {
      tick: 1,
      seed: 1,
      prng: [1, 2, 3, 4],
      actors: { 5: actor(5, "enemy", { x: 0, y: 0, z: 0 }, false) },
      runTimerTicks: 1,
      phase: "combat",
      log: [],
    };
    const result = stepEncounter(state, baseEncounter(), dungeonWithPull, {}, () => {});
    expect(result.encounter.pullIndex).toBe(1);
  });
});
