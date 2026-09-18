/**
 * tests/bots/class-specific-roles.test.ts
 *
 * Class-specific role behaviour driven by the real content kits (bots/src/kits.ts):
 * Chanter's healer swaps into Mend verse once the party's missing health crosses the threshold
 * (design/BIBLE.md Sec 5: "swaps verse when the party's missing health crosses a threshold"), and
 * Warden's tank pops Vigil (self-sustain) under a higher health floor than Brace (defensive).
 *
 * Usage: `npx vitest run tests/bots/class-specific-roles.test.ts`
 */
import { describe, expect, it } from "vitest";
import "./real-content-setup.js";
import { healerDecide, tankDecide } from "@bots/src/roles.js";
import type { ActorId, ActorState, BotView, SimState } from "@bots/src/types.js";

const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 1,
    kind: "player",
    classId: "chanter",
    role: "healer",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 100,
    maxHealth: 100,
    resource: 0,
    maxResource: 1,
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

function makeView(actors: ActorState[]): BotView {
  const table: Record<ActorId, ActorState> = {};
  for (const a of actors) table[a.id] = a;
  const state: SimState = { tick: 10, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, telegraphs: [], classKit: NO_CLASS_KIT };
}

describe("Chanter healer: verse swap on party missing-health threshold", () => {
  it("swaps to Mend (Refrain) when not already in Mend and the party is badly hurt", () => {
    const chanter = makeActor({ id: 1, resource: 0 }); // 0 == Ward, per the local convention
    const hurt = makeActor({ id: 2, role: "damage", health: 10, maxHealth: 100 }); // 90% missing
    const view = makeView([chanter, hurt]);

    const commands = healerDecide(view, chanter);

    expect(commands).toEqual([{ tick: 10, actorId: 1, kind: "cast", abilityId: "chanter.refrain" }]);
  });

  it("does not verse-swap when already in Mend (heals through Refrain's targeted heal-triage path instead)", () => {
    const chanter = makeActor({ id: 1, resource: 1 }); // already Mend
    const hurt = makeActor({ id: 2, role: "damage", health: 10, maxHealth: 100 });
    const view = makeView([chanter, hurt]);

    const commands = healerDecide(view, chanter);

    // A bare (untargeted) Refrain would be the verse-swap signature; a targeted one is the
    // ordinary heal-triage cast -- proving the swap branch itself did not fire.
    expect(commands).toEqual([{ tick: 10, actorId: 1, kind: "cast", abilityId: "chanter.refrain", targetId: hurt.id }]);
  });

  it("does not swap when the party is healthy", () => {
    const chanter = makeActor({ id: 1, resource: 0 });
    const healthy = makeActor({ id: 2, role: "damage", health: 95, maxHealth: 100 });
    const view = makeView([chanter, healthy]);

    const commands = healerDecide(view, chanter);

    expect(commands.some((c) => c.kind === "cast" && c.abilityId === "chanter.refrain")).toBe(false);
  });
});

describe("Warden tank: Vigil under a higher floor than Brace", () => {
  it("pops Vigil (self-sustain) between the Vigil and Brace floors, when affordable", () => {
    const warden = makeActor({ id: 1, classId: "warden", role: "tank", health: 55, maxHealth: 100, resource: 60 });
    const view = makeView([warden]);

    const commands = tankDecide(view, warden);

    expect(commands.some((c) => c.kind === "cast" && c.abilityId === "warden.vigil")).toBe(true);
    expect(commands.some((c) => c.kind === "cast" && c.abilityId === "warden.brace")).toBe(false);
  });

  it("pops Brace instead, under Brace's lower floor", () => {
    const warden = makeActor({ id: 1, classId: "warden", role: "tank", health: 30, maxHealth: 100, resource: 60 });
    const view = makeView([warden]);

    const commands = tankDecide(view, warden);

    expect(commands.some((c) => c.kind === "cast" && c.abilityId === "warden.brace")).toBe(true);
    expect(commands.some((c) => c.kind === "cast" && c.abilityId === "warden.vigil")).toBe(false);
  });

  it("does not pop Vigil without enough Momentum to afford it", () => {
    const warden = makeActor({ id: 1, classId: "warden", role: "tank", health: 55, maxHealth: 100, resource: 10 });
    const view = makeView([warden]);

    const commands = tankDecide(view, warden);

    expect(commands.some((c) => c.kind === "cast")).toBe(false);
  });
});
