/**
 * tests/bots/floor-escape.test.ts
 *
 * bots/src/shared/floor.ts: a bot standing in a hostile persistent floor patch (design/BIBLE.md
 * Sec 4/5 -- enemy fire, or an as-yet-unlabelled floor Window) walks itself clear within a bounded
 * number of skill-dial-gated ticks, exactly like tests/bots/telegraph-escape.test.ts's telegraph
 * case; a FRIENDLY floor Window (bots/CLAUDE.md: "allies must STAND IN friendly Windows to close
 * them -- never flee those") is never fled, and a bot whose class can close it walks toward it
 * once nearby; and telegraph escape (bot.ts, skill-dial-free) always outranks floor escape when
 * both overlap the same spot.
 *
 * Usage: `npx vitest run tests/bots/floor-escape.test.ts`
 */
import { describe, expect, it } from "vitest";
import { makeBot, defaultMemory } from "@bots/src/bot.js";
import { damageDecide } from "@bots/src/roles.js";
import { FLOOR_PATCH_PREFIX } from "@bots/src/shared/floor.js";
import { WINDOW_EFFECT_PREFIX } from "@bots/src/shared/pings.js";
import { isInAnyDanger, toMM } from "@bots/src/shared/positioning.js";
import { seedPrng } from "@bots/src/skill.js";
import type { ActorId, ActorState, BotView, SimState, Telegraph } from "@bots/src/types.js";

const MAX_ESCAPE_TICKS = 15;
const STEP_METRES = 2;
const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

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

function table(actors: ActorState[]): Record<ActorId, ActorState> {
  const out: Record<ActorId, ActorState> = {};
  for (const a of actors) out[a.id] = a;
  return out;
}

function makeView(actors: ActorState[], telegraphs: Telegraph[], tick = 10): BotView {
  const state: SimState = { tick, seed: 1, prng: [1, 2, 3, 4], actors: table(actors), runTimerTicks: 0, phase: "combat", log: [] };
  return { state, telegraphs, classKit: NO_CLASS_KIT };
}

describe("hostile floor patch avoidance", () => {
  it("moves a damage bot out of a hostile floor patch within a bounded number of ticks", () => {
    const bot = makeBot();
    let memory = defaultMemory(100);
    let prng = seedPrng(1);
    const self = makeActor({ id: 1, pos: { x: 0, y: 0, z: 0 } });
    const patch: Telegraph = {
      id: `${FLOOR_PATCH_PREFIX}enemy-fire`,
      shape: "circle",
      center: { x: 0, z: 0 },
      radius: 5,
      resolvesTick: 1000,
      danger: false,
    };

    let ticks = 0;
    for (; ticks < MAX_ESCAPE_TICKS; ticks++) {
      const view = makeView([self], [patch], ticks);
      const posMM = { x: toMM(self.pos.x), z: toMM(self.pos.z) };
      if (!isInAnyDanger(posMM, [{ ...patch, radius: 5.5 }])) break;

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
    expect(isInAnyDanger({ x: toMM(self.pos.x), z: toMM(self.pos.z) }, [patch])).toBe(false);
  });

  it("issues no escape move for a patch the bot never stands in", () => {
    const self = makeActor({ id: 1, pos: { x: 100, y: 0, z: 100 } });
    const patch: Telegraph = {
      id: `${FLOOR_PATCH_PREFIX}far-fire`,
      shape: "circle",
      center: { x: 0, z: 0 },
      radius: 5,
      resolvesTick: 1000,
      danger: false,
    };
    const commands = damageDecide(makeView([self], [patch]), self);
    expect(commands.some((c) => c.kind === "move")).toBe(false);
  });
});

describe("friendly floor Window: never fled", () => {
  it("issues no move at all standing dead-center of a Window it can close", () => {
    const self = makeActor({ id: 1, classId: "warden", pos: { x: 0, y: 0, z: 0 } });
    const window: Telegraph = {
      id: `${FLOOR_PATCH_PREFIX}${WINDOW_EFFECT_PREFIX}hymn:chanter`,
      shape: "circle",
      center: { x: 0, z: 0 },
      radius: 3,
      resolvesTick: 1000,
      danger: false,
    };
    const commands = damageDecide(makeView([self], [window]), self);
    expect(commands).toEqual([]);
  });

  it("walks toward a friendly Window it can close, once nearby", () => {
    const self = makeActor({ id: 1, classId: "warden", pos: { x: 20, y: 0, z: 0 } });
    const window: Telegraph = {
      id: `${FLOOR_PATCH_PREFIX}${WINDOW_EFFECT_PREFIX}hymn:chanter`,
      shape: "circle",
      center: { x: 0, z: 0 },
      radius: 3,
      resolvesTick: 1000,
      danger: false,
    };
    const commands = damageDecide(makeView([self], [window]), self);
    const move = commands.find((c) => c.kind === "move");
    expect(move).toBeDefined();
    if (move && move.kind === "move") expect(move.dir.x).toBeLessThan(0); // toward x=0 from x=20
  });

  it("The Lock: never approaches a Window its own class opened", () => {
    const self = makeActor({ id: 1, classId: "warden", pos: { x: 20, y: 0, z: 0 } });
    const window: Telegraph = {
      id: `${FLOOR_PATCH_PREFIX}${WINDOW_EFFECT_PREFIX}sundered:warden`,
      shape: "circle",
      center: { x: 0, z: 0 },
      radius: 3,
      resolvesTick: 1000,
      danger: false,
    };
    expect(damageDecide(makeView([self], [window]), self)).toEqual([]);
  });
});

describe("priority: telegraph escape outranks floor escape", () => {
  it("produces exactly one move -- the ordinary telegraph's escape -- when both overlap", () => {
    const self = makeActor({ id: 1, pos: { x: 0, y: 0, z: 0 } });
    const ordinary: Telegraph = { id: "boss-puddle", shape: "circle", center: { x: 0, z: 0 }, radius: 5, resolvesTick: 1000, danger: true };
    const patch: Telegraph = {
      id: `${FLOOR_PATCH_PREFIX}enemy-fire`,
      shape: "circle",
      center: { x: 0, z: 0 },
      radius: 5,
      resolvesTick: 1000,
      danger: false,
    };
    const view = makeView([self], [ordinary, patch]);
    const bot = makeBot();
    const result = bot.decide(view, defaultMemory(100), self.id, seedPrng(1));

    const moves = result.commands.filter((c) => c.kind === "move");
    expect(moves).toHaveLength(1);
  });
});
