/**
 * tests/bots/healer-keeps-pressing.test.ts
 *
 * HEALER-KEEPS-PRESSING (PR #362 ruling, the WoW Mythic+ loop): in a fight with nobody under the
 * 70% heal threshold the Chanter healer presses its non-heal button (Dissonance) at the party's
 * current target instead of idling; Cadence, Hymn, Chorus and the Refrain verse swap are never
 * pressed by this rule. Twin of game/bots/Keyrun.Bots.Tests/HealerKeepsPressingTests.cs.
 *
 * Usage: `npx vitest run tests/bots/healer-keeps-pressing.test.ts`
 */
import { describe, expect, it } from "vitest";
import "./real-content-setup.js";
import { healerDecide } from "@bots/src/roles.js";
import type { ActorId, ActorState, BotView, SimState } from "@bots/src/types.js";

const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 2, kind: "player", classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 0 }, yaw: 0,
    health: 250, maxHealth: 250, resource: 1, maxResource: 1, alive: true, targetId: null,
    castId: null, castEndsTick: null, castStartTick: null, cooldowns: {}, gcdReadyTick: 0,
    effects: [], threat: {}, ...overrides,
  };
}

function makeView(actors: ActorState[]): BotView {
  const table: Record<ActorId, ActorState> = {};
  for (const a of actors) table[a.id] = a;
  const state: SimState = { tick: 10, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, telegraphs: [], classKit: NO_CLASS_KIT };
}

const tank = (overrides: Partial<ActorState> = {}): ActorState =>
  makeActor({ id: 1, classId: "warden", role: "tank", pos: { x: 2000, y: 0, z: 0 }, health: 400, maxHealth: 400, targetId: 10, ...overrides });
const foe = (): ActorState =>
  makeActor({ id: 10, kind: "enemy", classId: "choir.melee", role: "none", pos: { x: 3000, y: 0, z: 0 }, health: 300, maxHealth: 300 });

describe("healer keeps pressing", () => {
  it("nobody under 70% in a fight: the Chanter casts Dissonance at the tank's target", () => {
    const healer = makeActor({});
    expect(healerDecide(makeView([tank(), healer, foe()]), healer)).toEqual([
      { tick: 10, actorId: 2, kind: "target", targetId: 10 },
      { tick: 10, actorId: 2, kind: "cast", abilityId: "chanter.dissonance", targetId: 10 },
    ]);
  });

  it("never reaches for a heal (Cadence, Hymn, Chorus) or the verse swap (Refrain) when Dissonance is down", () => {
    const healer = makeActor({ cooldowns: { "chanter.dissonance": 999 } });
    expect(healerDecide(makeView([tank(), healer, foe()]), healer)).toEqual([]);
  });

  it("a hurt ally still wins over the press", () => {
    const healer = makeActor({});
    const cmds = healerDecide(makeView([tank({ health: 100 }), healer, foe()]), healer);
    expect(cmds.length).toBeGreaterThan(0);
    expect(cmds.some((c) => c.kind === "cast" && c.abilityId === "chanter.dissonance")).toBe(false);
  });
});
