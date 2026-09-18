/**
 * tests/bots/ping-wiring.test.ts
 *
 * pings-policy.ts's pingReactionCommands() is wired into bot.ts's decideForActor(): a live ping
 * reaction fires even while the skill-dial gate is shut (pings are urgent party calls, not
 * rotation -- see bot.ts's header), and a ping reaction that casts suppresses the normal
 * role-dispatch cast that same tick so a bot never double-casts.
 *
 * Usage: `npx vitest run tests/bots/ping-wiring.test.ts`
 */
import { describe, expect, it } from "vitest";
import { makeBot, type BotMemory } from "@bots/src/bot.js";
import { seedPrng } from "@bots/src/skill.js";
import type { PingEvent } from "@bots/src/shared/pings.js";
import type { ActorId, ActorState, BotView, SimState } from "@bots/src/types.js";

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

function makeView(actors: ActorState[], tick: number, rotation: string[] = []): BotView {
  const table: Record<ActorId, ActorState> = {};
  for (const a of actors) table[a.id] = a;
  const state: SimState = { tick, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, telegraphs: [], classKit: { rotation, interruptAbilityId: "kick", defensiveAbilityId: null, healAbilityId: null } };
}

describe("bot.decide() ping wiring", () => {
  it("reacts to a live interrupt ping even while the skill-dial gate is shut", () => {
    const bot = makeBot();
    const memory: BotMemory = { skillDial: 0, nextActionTick: 1000 };
    const prng = seedPrng(1);
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const self = makeActor({ id: 2 });
    const view = makeView([enemy, self], 10);
    const ping: PingEvent = { tick: 10, actorId: 3, kind: "target", targetId: 100 };

    const result = bot.decide(view, memory, self.id, prng, [ping]);

    expect(result.commands).toEqual([{ tick: 10, actorId: 2, kind: "cast", abilityId: "kick", targetId: 100 }]);
  });

  it("does not react to pings when none are passed (back-compat: pings defaults to none)", () => {
    const bot = makeBot();
    const memory: BotMemory = { skillDial: 0, nextActionTick: 1000 };
    const prng = seedPrng(1);
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const self = makeActor({ id: 2 });
    const view = makeView([enemy, self], 10);

    const result = bot.decide(view, memory, self.id, prng);

    expect(result.commands).toEqual([]);
  });

  it("a ping-triggered cast suppresses this tick's role-dispatch cast (no double cast)", () => {
    const bot = makeBot();
    const memory: BotMemory = { skillDial: 100, nextActionTick: 0 }; // gate open
    const prng = seedPrng(1);
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const self = makeActor({ id: 2 });
    const view = makeView([enemy, self], 10, ["filler"]);
    const ping: PingEvent = { tick: 10, actorId: 3, kind: "target", targetId: 100 };

    const result = bot.decide(view, memory, self.id, prng, [ping]);

    const casts = result.commands.filter((c) => c.kind === "cast");
    expect(casts).toHaveLength(1);
    expect(casts[0]).toMatchObject({ abilityId: "kick" });
  });
});
