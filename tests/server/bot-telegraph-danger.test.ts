// Usage: STATE.md defect (f) -- server bots must read a telegraph's `danger` from its class (the
// same client/src/sim-host/telegraph-map.ts rule the local worker host's bots use, PR #61), never
// a hardcoded `true`: a stack circle is the one telegraph a bot should stand IN.
//
// Two layers:
//  1. Room-level behaviour: a long-lived telegraph is placed right on a bot-controlled damage
//     actor, then the room ticks. A stack circle ("pair.stackCall") must NOT move the bot; an
//     avoid circle ("seed.slam", the control) must. Driven through Room.tick() (public for tests,
//     same as run-end.test.ts) -- the only path that decides the bot's view.
//  2. Parity: server/src/telegraph.ts's toBotTelegraph equals the client host's for every
//     telegraphed ability the shipped content emits, plus an unknown-ability fallback.
import { describe, expect, it } from "vitest";
import { Room } from "../../server/src/room.js";
import { toBotTelegraph } from "../../server/src/telegraph.js";
import { buildServerContent, loadServerRegistry, DUNGEON_ID, DEFAULT_KEY_LEVEL } from "../../server/src/content-setup.js";
import {
  TELEGRAPH_CLASS_BY_ABILITY,
  toBotTelegraph as clientToBotTelegraph,
} from "../../client/src/sim-host/telegraph-map.js";
import type { Telegraph } from "../../sim/encounter/telegraphs";

const BOT_ACTOR = 3; // a damage slot (roster.ts), bot-controlled since no human joins
const TICKS = 10;

/** End position of BOT_ACTOR after TICKS room ticks, with an optional long-lived circle of
 * `abilityId` placed on it at tick 0 (null = no telegraph: the baseline the bot would do anyway). */
function endPosWithTelegraph(abilityId: string | null): { x: number; z: number } {
  const room = new Room("tg-room", buildServerContent(loadServerRegistry(), DUNGEON_ID, DEFAULT_KEY_LEVEL));
  const actor = room.run.state.actors[BOT_ACTOR];
  if (!actor) throw new Error("bot actor missing");
  const start = { x: actor.pos.x, z: actor.pos.z };
  if (abilityId !== null) room.run.encounter.telegraphs.push(circleOn(start, abilityId));
  for (let i = 0; i < TICKS; i++) room.tick();
  const end = room.run.state.actors[BOT_ACTOR];
  if (!end) throw new Error("bot actor missing after ticks");
  return { x: end.pos.x, z: end.pos.z };
}

function circleOn(start: { x: number; z: number }, abilityId: string): Telegraph {
  return {
    id: 9001,
    sourceId: 100,
    abilityId,
    shape: "circle",
    origin: { x: start.x, y: 0, z: start.z },
    yaw: 0,
    radius: 1500,
    resolvesTick: 100_000, // never resolves inside the test window
  };
}

describe("server: bot telegraph danger derives from the telegraph class", () => {
  it("a stack circle does not make a bot flee: it moves exactly as with no telegraph at all", () => {
    expect(endPosWithTelegraph("pair.stackCall")).toEqual(endPosWithTelegraph(null));
  });

  it("control: an avoid circle does make the bot flee (it ends somewhere else)", () => {
    expect(endPosWithTelegraph("seed.slam")).not.toEqual(endPosWithTelegraph(null));
  });

  it("toBotTelegraph matches the client host for every shipped telegraph class", () => {
    const ids = [...Object.keys(TELEGRAPH_CLASS_BY_ABILITY), "unknown.ability"];
    for (const abilityId of ids) {
      const t: Telegraph = {
        id: 7, sourceId: 100, abilityId, shape: "cone", origin: { x: 10, y: 0, z: -20 }, yaw: 64,
        radius: 900, angle1024: 128, resolvesTick: 40,
      };
      expect(toBotTelegraph(t)).toEqual(clientToBotTelegraph(t));
    }
    const stack = { id: 1, sourceId: 100, abilityId: "pair.stackCall", shape: "circle" as const, origin: { x: 0, y: 0, z: 0 }, yaw: 0, radius: 500, resolvesTick: 5 };
    expect(toBotTelegraph(stack).danger).toBe(false);
  });
});
