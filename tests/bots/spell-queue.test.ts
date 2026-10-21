/**
 * tests/bots/spell-queue.test.ts
 *
 * Bots press inside the spell queue window (PR #370, sim/combat/spell-queue.ts). Twin of
 * game/bots/Keyrun.Bots.Tests/SpellQueueBotsTests.cs: mid-cast, a bot is not locked out once its cast is in its last
 * SPELL_QUEUE_WINDOW_TICKS ticks, so it presses an off-GCD ability that the sim holds and fires when the cast ends; the
 * kick ledger counts such a kicker when that landing tick is inside the enemy's cast, and picks the kick that lands
 * first, ties to the lowest id (BIBLE s3).
 *
 * Usage: `npx vitest run tests/bots/spell-queue.test.ts`
 */
import { describe, expect, it } from "vitest";
import { isCastLocked, resolveInterrupt } from "@bots/src/shared/ledger.js";
import { abilityReady } from "@bots/src/roles/shared.js";
import type { ActorId, ActorState } from "@bots/src/types.js";
import { SPELL_QUEUE_WINDOW_TICKS } from "../../sim/combat/spell-queue.js";

const NOW = 100;
const KIT = { offGcdAbilityIds: ["cinder.quench"] };

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 1, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 100,
    maxHealth: 100, resource: 0, maxResource: 0, alive: true, targetId: null, castId: null, castEndsTick: null,
    castStartTick: null, cooldowns: {}, gcdReadyTick: 0, effects: [], threat: {}, ...overrides,
  };
}

function casting(id: number, endsTick: number): ActorState {
  return makeActor({ id, castId: "cinder.emberBolt", castStartTick: NOW - 10, castEndsTick: endsTick });
}

function cantor(endsTick: number): ActorState {
  return makeActor({ id: 9, kind: "enemy", classId: "choir.cantor", role: "none", castId: "choir.healCast", castStartTick: NOW - 5, castEndsTick: endsTick });
}

function table(actors: ActorState[]): Record<ActorId, ActorState> {
  const out: Record<ActorId, ActorState> = {};
  for (const a of actors) out[a.id] = a;
  return out;
}

describe("bots and the spell queue window", () => {
  it("a bot in the last window ticks of its cast is ready for an off-GCD press", () => {
    const self = casting(3, NOW + SPELL_QUEUE_WINDOW_TICKS);
    expect(isCastLocked(self, NOW)).toBe(false);
    expect(abilityReady(self, "cinder.quench", NOW, KIT)).toBe(true);
    self.castEndsTick = NOW + SPELL_QUEUE_WINDOW_TICKS + 1;
    expect(isCastLocked(self, NOW)).toBe(true);
    expect(abilityReady(self, "cinder.quench", NOW, KIT)).toBe(false);
  });

  it("the ledger assigns a kicker whose cast ends inside the window and before the enemy cast", () => {
    const actors = table([casting(3, NOW + 4), cantor(NOW + 75)]);
    expect(resolveInterrupt(actors, "cinder.quench", [3], NOW, [], true)).toEqual({ targetId: 9, kickerId: 3 });
  });

  it("the ledger never assigns a kick that would land once the enemy cast is over", () => {
    const actors = table([casting(3, NOW + 4), cantor(NOW + 4)]);
    expect(resolveInterrupt(actors, "cinder.quench", [3], NOW, [], true)).toBeNull();
  });

  it("the ledger picks the kick that lands first, then the lowest id", () => {
    const queued = casting(3, NOW + 4);
    const actors = table([queued, makeActor({ id: 4 }), cantor(NOW + 75)]);
    expect(resolveInterrupt(actors, "cinder.quench", [3, 4], NOW, [], true)?.kickerId).toBe(4);
    queued.castId = null;
    queued.castEndsTick = null;
    expect(resolveInterrupt(actors, "cinder.quench", [4, 3], NOW, [], true)?.kickerId).toBe(3);
  });
});
