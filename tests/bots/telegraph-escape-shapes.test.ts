/**
 * tests/bots/telegraph-escape-shapes.test.ts
 *
 * Extends tests/bots/telegraph-escape.test.ts's circle case to the other two real telegraph
 * shapes (design/BIBLE.md Sec 3: "circle, cone, line"): a bot standing inside a dangerous cone or
 * line must walk itself clear before the telegraph resolves, driven purely by bot.ts's decide().
 *
 * Usage: `npx vitest run tests/bots/telegraph-escape-shapes.test.ts`
 */
import { describe, expect, it } from "vitest";
import { makeBot, defaultMemory } from "@bots/src/bot.js";
import { isInAnyDanger, toMM } from "@bots/src/shared/positioning.js";
import { seedPrng } from "@bots/src/skill.js";
import type { ActorState, BotView, SimState, Telegraph } from "@bots/src/types.js";

const MAX_ESCAPE_TICKS = 15;
const STEP_METRES = 2;

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 1,
    kind: "player",
    classId: "warrior",
    role: "damage",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 100,
    maxHealth: 100,
    resource: 0,
    maxResource: 0,
    alive: true,
    targetId: null,
    castId: null,
    castEndsTick: null,
    castStartTick: null,
    cooldowns: {},
    gcdReadyTick: 0,
    effects: [],
    threat: {},
    ...overrides,
  };
}

function runEscape(startPos: { x: number; z: number }, telegraph: Telegraph): { ticks: number; finalPos: { x: number; z: number } } {
  const bot = makeBot();
  let memory = defaultMemory(100);
  let prng = seedPrng(1);
  const self = makeActor({ id: 1, pos: { x: startPos.x, y: 0, z: startPos.z } });

  let ticks = 0;
  for (; ticks < MAX_ESCAPE_TICKS; ticks++) {
    const state: SimState = {
      tick: ticks,
      seed: 1,
      prng: [1, 2, 3, 4],
      actors: { [self.id]: self },
      runTimerTicks: 0,
      phase: "combat",
      log: [],
    };
    const view: BotView = {
      state,
      telegraphs: [telegraph],
      classKit: { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null },
    };
    const posMM = { x: toMM(self.pos.x), z: toMM(self.pos.z) };
    if (!isInAnyDanger(posMM, [telegraph])) break;

    const result = bot.decide(view, memory, self.id, prng);
    memory = result.memory;
    prng = result.prng;
    const move = result.commands.find((c) => c.kind === "move");
    expect(move).toBeDefined();
    if (move && move.kind === "move") {
      self.pos = { x: self.pos.x + move.dir.x * STEP_METRES, y: 0, z: self.pos.z + move.dir.z * STEP_METRES };
    }
  }

  return { ticks, finalPos: { x: self.pos.x, z: self.pos.z } };
}

describe("telegraph avoidance: real shapes", () => {
  it("escapes a dangerous cone within a bounded number of ticks", () => {
    const telegraph: Telegraph = {
      id: "boss-cleave",
      shape: "cone",
      center: { x: 0, z: 0 },
      length: 8,
      angle1024: 200,
      yaw: 0,
      resolvesTick: 1000,
      danger: true,
    };
    // Standing well inside the cone's arc, close to the apex.
    const { ticks, finalPos } = runEscape({ x: 3, z: 0 }, telegraph);

    expect(ticks).toBeLessThan(MAX_ESCAPE_TICKS);
    expect(isInAnyDanger({ x: toMM(finalPos.x), z: toMM(finalPos.z) }, [telegraph])).toBe(false);
  });

  it("escapes a dangerous line within a bounded number of ticks", () => {
    const telegraph: Telegraph = {
      id: "boss-beam",
      shape: "line",
      center: { x: 0, z: 0 },
      yaw: 0,
      length: 20,
      width: 6,
      resolvesTick: 1000,
      danger: true,
    };
    // Standing on the beam's centreline, partway down its length.
    const { ticks, finalPos } = runEscape({ x: 5, z: 0 }, telegraph);

    expect(ticks).toBeLessThan(MAX_ESCAPE_TICKS);
    expect(isInAnyDanger({ x: toMM(finalPos.x), z: toMM(finalPos.z) }, [telegraph])).toBe(false);
  });

  it("a bot that never enters the danger zone issues no escape move", () => {
    const telegraph: Telegraph = {
      id: "far-puddle",
      shape: "circle",
      center: { x: 100, z: 100 },
      radius: 5,
      resolvesTick: 1000,
      danger: true,
    };
    const { ticks } = runEscape({ x: 0, z: 0 }, telegraph);
    expect(ticks).toBe(0);
  });
});
