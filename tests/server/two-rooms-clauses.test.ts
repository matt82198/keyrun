// Usage: modstate-repro -- proof (or refutation) that sim/combat/clause-hooks.ts's `activeRules`
// and sim/combat/affinity.ts's `registeredEffects` module-scoped registries stay correct when TWO
// rooms are stepped in the SAME Node process, one with an active Clause and one without, driven
// directly through the Room API's public `tick()` (bypassing setInterval/WS so the interleaving
// order is exact and test-controlled, same reason tests/server/determinism.test.ts drives
// createRun/stepRun directly for its own comparison).
//
// Both rooms are ticked ALTERNATELY (A, B, A, B, ...) for 600 ticks -- the same interleaving
// server/src/lobby.ts produces for concurrent rooms in one process. Each room's final state hash
// is then compared against a SOLO replay: a second, freshly-built Room with identical content and
// clauseId, stepped 600 ticks completely alone (never interleaved with anything else). Room.tick()
// is a thin wrapper over sim/index.ts's createRun+stepRun (server/src/room.ts) with no other
// module-scoped state of its own -- bots are seeded from the same fixed `SEED` export every time
// (server/src/room.ts), so a solo Room's bot-driven command stream is exactly reproducible and
// this comparison is equivalent to (but does not require hand-capturing) a raw createRun+stepRun
// replay of the same commands.
//
// If either registry above were only set at room-construction time (or leaked across rooms), one
// room interleaved with the other would silently run some ticks under the OTHER room's Clause/
// affinity table and its hash would diverge from its own solo replay.
import { describe, expect, it } from "vitest";
import { hashState } from "../../sim/core/state";
import { buildServerContent, DUNGEON_ID, loadServerRegistry, type ServerContent } from "../../server/src/content-setup.js";
import { Room } from "../../server/src/room.js";

const TICKS = 600;
const TEST_KEY_LEVEL = 3;

function tickRoom(room: Room, ticks: number): void {
  for (let i = 0; i < ticks; i++) room.tick();
}

describe("server: two rooms in one process keep their own Clause and affinity rules", () => {
  it("a Pact room and a no-clause room, ticked alternately, match their own solo replays", () => {
    const registry = loadServerRegistry();
    const content: ServerContent = buildServerContent(registry, DUNGEON_ID, TEST_KEY_LEVEL);

    // Room A: Pact active. Room B: no clause at all (firstQueuer omitted).
    const roomA = new Room("room-a", content, undefined, { role: "tank", clauseId: "pact" });
    const roomB = new Room("room-b", content);

    for (let i = 0; i < TICKS; i++) {
      roomA.tick();
      roomB.tick();
    }

    const hashA = hashState(roomA.run.state);
    const hashB = hashState(roomB.run.state);

    // Solo replays: fresh rooms, identical content/options, never interleaved with anything else.
    const soloA = new Room("solo-a", content, undefined, { role: "tank", clauseId: "pact" });
    tickRoom(soloA, TICKS);
    const soloB = new Room("solo-b", content);
    tickRoom(soloB, TICKS);

    expect(hashA).toBe(hashState(soloA.run.state));
    expect(hashB).toBe(hashState(soloB.run.state));
    // Sanity: Pact's clause is actually wired into room A's run (not a no-op comparison) and room
    // B genuinely has no clause active.
    expect(roomA.run.encounter.rules.comboDamageBp).not.toBe(0);
    expect(roomB.run.encounter.rules.comboDamageBp).toBe(0);
  });

  it("a Hush room and a Pact room, ticked alternately, match their own solo replays (order reversed)", () => {
    const registry = loadServerRegistry();
    const content: ServerContent = buildServerContent(registry, DUNGEON_ID, TEST_KEY_LEVEL);

    const roomHush = new Room("room-hush", content, undefined, { role: "healer", clauseId: "hush" });
    const roomPact = new Room("room-pact", content, undefined, { role: "tank", clauseId: "pact" });

    for (let i = 0; i < TICKS; i++) {
      // Reversed order vs. the first test -- interleaving must be safe regardless of which room
      // steps first on a given tick.
      roomHush.tick();
      roomPact.tick();
    }

    const soloHush = new Room("solo-hush", content, undefined, { role: "healer", clauseId: "hush" });
    tickRoom(soloHush, TICKS);
    const soloPact = new Room("solo-pact", content, undefined, { role: "tank", clauseId: "pact" });
    tickRoom(soloPact, TICKS);

    expect(hashState(roomHush.run.state)).toBe(hashState(soloHush.run.state));
    expect(hashState(roomPact.run.state)).toBe(hashState(soloPact.run.state));
  });
});
