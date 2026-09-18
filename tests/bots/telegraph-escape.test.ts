/**
 * tests/bots/telegraph-escape.test.ts
 *
 * A damage bot standing dead-center of a dangerous circle telegraph must
 * walk itself clear of the danger zone within a small, bounded number of
 * ticks -- driven purely by bot.ts's decide() move commands.
 *
 * Usage: `npx vitest run tests/bots/telegraph-escape.test.ts`
 */
import { describe, expect, it } from "vitest";
import { makeBot, defaultMemory } from "@bots/src/bot.js";
import { isInAnyDanger, toMM } from "@bots/src/shared/positioning.js";
import { seedPrng } from "@bots/src/skill.js";
import type { ActorState, BotView, SimState, Telegraph } from "@bots/src/types.js";

const MAX_ESCAPE_TICKS = 10;
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

describe("telegraph avoidance", () => {
  it("moves a bot out of a dangerous circle within a bounded number of ticks", () => {
    const bot = makeBot();
    let memory = defaultMemory(100);
    let prng = seedPrng(1);
    const self = makeActor({ id: 1, pos: { x: 0, y: 0, z: 0 } });
    const telegraph: Telegraph = {
      id: "boss-puddle",
      shape: "circle",
      center: { x: 0, z: 0 },
      radius: 5,
      resolvesTick: 1000,
      danger: true,
    };

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

    expect(ticks).toBeLessThan(MAX_ESCAPE_TICKS);
    expect(isInAnyDanger({ x: toMM(self.pos.x), z: toMM(self.pos.z) }, [telegraph])).toBe(false);
  });
});
