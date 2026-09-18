/**
 * tests/bots/interrupt.test.ts
 *
 * Two damage bots both have an interrupt ready against the same enemy cast;
 * only the lowest actorId should issue the kick, the other should fall back
 * to its rotation instead.
 *
 * Usage: `npx vitest run tests/bots/interrupt.test.ts`
 */
import { describe, expect, it } from "vitest";
import { isInterruptResponsible } from "@bots/src/shared/priority.js";
import { damageDecide } from "@bots/src/roles.js";
import type { ActorState, BotView, SimState } from "@bots/src/types.js";

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

describe("interrupt assignment", () => {
  it("isInterruptResponsible picks only the lowest eligible actorId", () => {
    expect(isInterruptResponsible(5, [5, 7, 12])).toBe(true);
    expect(isInterruptResponsible(7, [5, 7, 12])).toBe(false);
    expect(isInterruptResponsible(1, [])).toBe(false);
  });

  it("only the lowest-actorId bot with kick ready casts the interrupt", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const low = makeActor({ id: 2, cooldowns: {} });
    const high = makeActor({ id: 3, cooldowns: {} });
    const state: SimState = {
      tick: 10,
      seed: 1,
      prng: [1, 2, 3, 4],
      actors: { [enemy.id]: enemy, [low.id]: low, [high.id]: high },
      runTimerTicks: 0,
      phase: "combat",
      log: [],
    };
    const view: BotView = {
      state,
      telegraphs: [],
      classKit: { rotation: ["filler"], interruptAbilityId: "kick", defensiveAbilityId: null, healAbilityId: null },
    };

    const lowCommands = damageDecide(view, low);
    const highCommands = damageDecide(view, high);

    expect(lowCommands[0]).toMatchObject({ kind: "cast", abilityId: "kick", targetId: 100 });
    expect(highCommands.some((c) => c.kind === "cast" && c.abilityId === "kick")).toBe(false);
  });

  it("the responsible bot falls back to rotation once its interrupt is on cooldown", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const low = makeActor({ id: 2, cooldowns: { kick: 999 } });
    const state: SimState = {
      tick: 10,
      seed: 1,
      prng: [1, 2, 3, 4],
      actors: { [enemy.id]: enemy, [low.id]: low },
      runTimerTicks: 0,
      phase: "combat",
      log: [],
    };
    const view: BotView = {
      state,
      telegraphs: [],
      classKit: { rotation: ["filler"], interruptAbilityId: "kick", defensiveAbilityId: null, healAbilityId: null },
    };
    const commands = damageDecide(view, low);
    expect(commands[0]).toMatchObject({ kind: "cast", abilityId: "filler" });
  });
});
