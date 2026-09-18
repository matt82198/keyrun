/**
 * tests/bots/pings.test.ts
 *
 * Reactions to party G-pings (bots/src/pings-policy.ts, bots/src/shared/pings.ts):
 * a ping on a casting enemy is kicked by the responsible interrupt bot, a
 * ping on a Window carrier is prioritised by a payoff-capable bot, and a
 * spot ping pulls healer/damage bots toward it within a bounded number of
 * ticks while the tank holds position.
 *
 * Usage: `npx vitest run tests/bots/pings.test.ts`
 */
import { describe, expect, it } from "vitest";
import {
  interruptPingCommands,
  windowPingPayoffCommands,
  spotPingMoveCommands,
  pingReactionCommands,
  PING_MOVE_TICKS,
  PAYOFF_ABILITY_PREFIX,
} from "@bots/src/pings-policy.js";
import { isPingLive, latestLivePing, WINDOW_EFFECT_PREFIX, type PingEvent } from "@bots/src/shared/pings.js";
import { toMM } from "@bots/src/shared/positioning.js";
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

function makeState(actors: ActorState[], tick = 10): SimState {
  const table: Record<number, ActorState> = {};
  for (const a of actors) table[a.id] = a;
  return { tick, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
}

const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

describe("isPingLive / latestLivePing", () => {
  it("a ping is live from its own tick through the window, not before or after", () => {
    const ping: PingEvent = { tick: 100, actorId: 1, kind: "spot", point: { x: 0, z: 0 } };
    expect(isPingLive(ping, 99, 60)).toBe(false);
    expect(isPingLive(ping, 100, 60)).toBe(true);
    expect(isPingLive(ping, 160, 60)).toBe(true);
    expect(isPingLive(ping, 161, 60)).toBe(false);
  });

  it("latestLivePing prefers the newer tick, then the lower actorId", () => {
    const older: PingEvent = { tick: 10, actorId: 1, kind: "target", targetId: 5 };
    const newer: PingEvent = { tick: 20, actorId: 2, kind: "target", targetId: 5 };
    const sameTickHigh: PingEvent = { tick: 20, actorId: 9, kind: "target", targetId: 5 };
    const sameTickLow: PingEvent = { tick: 20, actorId: 3, kind: "target", targetId: 5 };
    expect(latestLivePing([older, newer], 20)).toBe(newer);
    expect(latestLivePing([sameTickHigh, sameTickLow], 20)).toBe(sameTickLow);
  });

  it("filters by kind and drops stale pings", () => {
    const spot: PingEvent = { tick: 0, actorId: 1, kind: "spot", point: { x: 0, z: 0 } };
    const target: PingEvent = { tick: 0, actorId: 1, kind: "target", targetId: 5 };
    expect(latestLivePing([spot, target], 0, 60, "target")).toBe(target);
    expect(latestLivePing([spot], 1000, 60, "spot")).toBeNull();
  });
});

describe("interruptPingCommands", () => {
  it("the responsible damage bot kicks the specific enemy named by a target ping", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const other = makeActor({ id: 101, kind: "enemy", castId: "also-casting" });
    const low = makeActor({ id: 2, role: "damage" });
    const state = makeState([enemy, other, low]);
    const view: BotView = {
      state,
      telegraphs: [],
      classKit: { ...NO_CLASS_KIT, interruptAbilityId: "kick" },
    };
    const ping: PingEvent = { tick: 10, actorId: 2, kind: "target", targetId: 100 };

    const commands = interruptPingCommands(view, low, [ping]);

    expect(commands).toEqual([{ tick: 10, actorId: 2, kind: "cast", abilityId: "kick", targetId: 100 }]);
  });

  it("a higher-actorId bot yields to the lowest-actorId bot with kick ready", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const low = makeActor({ id: 2, role: "damage" });
    const high = makeActor({ id: 3, role: "damage" });
    const state = makeState([enemy, low, high]);
    const view: BotView = { state, telegraphs: [], classKit: { ...NO_CLASS_KIT, interruptAbilityId: "kick" } };
    const ping: PingEvent = { tick: 10, actorId: 2, kind: "target", targetId: 100 };

    expect(interruptPingCommands(view, high, [ping])).toEqual([]);
    expect(interruptPingCommands(view, low, [ping])[0]).toMatchObject({ abilityId: "kick", targetId: 100 });
  });

  it("does nothing once the interrupt is on cooldown", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const low = makeActor({ id: 2, role: "damage", cooldowns: { kick: 999 } });
    const state = makeState([enemy, low]);
    const view: BotView = { state, telegraphs: [], classKit: { ...NO_CLASS_KIT, interruptAbilityId: "kick" } };
    const ping: PingEvent = { tick: 10, actorId: 2, kind: "target", targetId: 100 };
    expect(interruptPingCommands(view, low, [ping])).toEqual([]);
  });

  it("does nothing when the pinged target is not actually casting", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", castId: null });
    const low = makeActor({ id: 2, role: "damage" });
    const state = makeState([enemy, low]);
    const view: BotView = { state, telegraphs: [], classKit: { ...NO_CLASS_KIT, interruptAbilityId: "kick" } };
    const ping: PingEvent = { tick: 10, actorId: 2, kind: "target", targetId: 100 };
    expect(interruptPingCommands(view, low, [ping])).toEqual([]);
  });
});

describe("windowPingPayoffCommands", () => {
  it("a payoff-capable bot targets and casts its payoff on a pinged Window carrier", () => {
    const enemy = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}shatter`, sourceId: 2, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    const self = makeActor({ id: 2, role: "damage", targetId: null });
    const state = makeState([enemy, self]);
    const view: BotView = {
      state,
      telegraphs: [],
      classKit: { ...NO_CLASS_KIT, rotation: ["filler", `${PAYOFF_ABILITY_PREFIX}bolt`] },
    };
    const ping: PingEvent = { tick: 10, actorId: 3, kind: "target", targetId: 100 };

    const commands = windowPingPayoffCommands(view, self, [ping]);

    expect(commands).toEqual([
      { tick: 10, actorId: 2, kind: "target", targetId: 100 },
      { tick: 10, actorId: 2, kind: "cast", abilityId: `${PAYOFF_ABILITY_PREFIX}bolt`, targetId: 100 },
    ]);
  });

  it("skips the target command when already targeting the pinged carrier", () => {
    const enemy = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}shatter`, sourceId: 2, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    const self = makeActor({ id: 2, role: "damage", targetId: 100 });
    const state = makeState([enemy, self]);
    const view: BotView = {
      state,
      telegraphs: [],
      classKit: { ...NO_CLASS_KIT, rotation: [`${PAYOFF_ABILITY_PREFIX}bolt`] },
    };
    const ping: PingEvent = { tick: 10, actorId: 3, kind: "target", targetId: 100 };
    expect(windowPingPayoffCommands(view, self, [ping])).toEqual([
      { tick: 10, actorId: 2, kind: "cast", abilityId: `${PAYOFF_ABILITY_PREFIX}bolt`, targetId: 100 },
    ]);
  });

  it("does nothing for a class with no payoff ability in its rotation", () => {
    const enemy = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}shatter`, sourceId: 2, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    const self = makeActor({ id: 2, role: "damage" });
    const state = makeState([enemy, self]);
    const view: BotView = { state, telegraphs: [], classKit: { ...NO_CLASS_KIT, rotation: ["filler"] } };
    const ping: PingEvent = { tick: 10, actorId: 3, kind: "target", targetId: 100 };
    expect(windowPingPayoffCommands(view, self, [ping])).toEqual([]);
  });

  it("does nothing when the pinged target carries no Window effect", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", effects: [] });
    const self = makeActor({ id: 2, role: "damage" });
    const state = makeState([enemy, self]);
    const view: BotView = {
      state,
      telegraphs: [],
      classKit: { ...NO_CLASS_KIT, rotation: [`${PAYOFF_ABILITY_PREFIX}bolt`] },
    };
    const ping: PingEvent = { tick: 10, actorId: 3, kind: "target", targetId: 100 };
    expect(windowPingPayoffCommands(view, self, [ping])).toEqual([]);
  });
});

describe("spotPingMoveCommands", () => {
  it("the tank does not move for a spot ping", () => {
    const tank = makeActor({ id: 1, role: "tank", pos: { x: 0, y: 0, z: 0 } });
    const state = makeState([tank]);
    const view: BotView = { state, telegraphs: [], classKit: NO_CLASS_KIT };
    const ping: PingEvent = { tick: 10, actorId: 2, kind: "spot", point: { x: 10, z: 0 } };
    expect(spotPingMoveCommands(view, tank, [ping])).toEqual([]);
  });

  it("moves a healer/damage bot to a spot ping within PING_MOVE_TICKS, simulated tick by tick", () => {
    const STEP_METRES = 1.5;
    const self = makeActor({ id: 2, role: "healer", pos: { x: 0, y: 0, z: 0 } });
    const spotPoint = { x: 10, z: 0 };

    let ticks = 0;
    for (; ticks < PING_MOVE_TICKS; ticks++) {
      const ping: PingEvent = { tick: 0, actorId: 3, kind: "spot", point: spotPoint };
      const view: BotView = { state: makeState([self], ticks), telegraphs: [], classKit: NO_CLASS_KIT };
      const commands = spotPingMoveCommands(view, self, [ping]);
      const move = commands.find((c) => c.kind === "move");
      if (!move) break;
      if (move.kind === "move") {
        self.pos = { x: self.pos.x + move.dir.x * STEP_METRES, y: 0, z: self.pos.z + move.dir.z * STEP_METRES };
      }
    }

    expect(ticks).toBeLessThan(PING_MOVE_TICKS);
    const distMM = Math.hypot(toMM(spotPoint.x - self.pos.x), toMM(spotPoint.z - self.pos.z));
    expect(distMM).toBeLessThanOrEqual(500);
  });

  it("stops producing move commands once the ping goes stale", () => {
    const self = makeActor({ id: 2, role: "damage", pos: { x: 0, y: 0, z: 0 } });
    const state = makeState([self], PING_MOVE_TICKS + 1);
    const view: BotView = { state, telegraphs: [], classKit: NO_CLASS_KIT };
    const ping: PingEvent = { tick: 0, actorId: 3, kind: "spot", point: { x: 10, z: 0 } };
    expect(spotPingMoveCommands(view, self, [ping])).toEqual([]);
  });
});

describe("pingReactionCommands priority", () => {
  it("prefers the interrupt reaction over a Window payoff and a spot move", () => {
    const enemy = makeActor({
      id: 100,
      kind: "enemy",
      castId: "big-cast",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}shatter`, sourceId: 2, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    const self = makeActor({ id: 2, role: "damage" });
    const state = makeState([enemy, self]);
    const view: BotView = {
      state,
      telegraphs: [],
      classKit: {
        rotation: [`${PAYOFF_ABILITY_PREFIX}bolt`],
        interruptAbilityId: "kick",
        defensiveAbilityId: null,
        healAbilityId: null,
      },
    };
    const targetPing: PingEvent = { tick: 10, actorId: 3, kind: "target", targetId: 100 };
    const spotPing: PingEvent = { tick: 10, actorId: 3, kind: "spot", point: { x: 50, z: 50 } };

    const commands = pingReactionCommands(view, self, [targetPing, spotPing]);

    expect(commands).toEqual([{ tick: 10, actorId: 2, kind: "cast", abilityId: "kick", targetId: 100 }]);
  });

  it("falls back to a spot move when no interrupt or payoff reaction applies", () => {
    const self = makeActor({ id: 2, role: "damage", pos: { x: 0, y: 0, z: 0 } });
    const state = makeState([self]);
    const view: BotView = { state, telegraphs: [], classKit: NO_CLASS_KIT };
    const spotPing: PingEvent = { tick: 10, actorId: 3, kind: "spot", point: { x: 5, z: 0 } };

    const commands = pingReactionCommands(view, self, [spotPing]);

    expect(commands).toHaveLength(1);
    expect(commands[0]?.kind).toBe("move");
  });
});
