/**
 * tests/client/sim-host/sim-runtime.test.ts
 *
 * The dispatch brief's headless smoke test (renamed from worker-smoke.test.ts: research/11 D9's
 * real-Worker follow-up moved this class out of worker.ts -- now the actual Worker bootstrap --
 * into sim-runtime.ts; see that file's docstring): constructs SimRuntime with the REAL content
 * registry (content/index.ts's loadContent(), which works fine under Node/Vitest -- see
 * run-setup.ts's docstring for why the browser path is a separate fetch-based loader), the real
 * sim, and real bots/src bots, then steps 200 ticks without throwing. No DOM/Worker/postMessage
 * touched anywhere in this path (SimRuntime.step() is pure state advancement), so this needs no
 * jsdom and no self/postMessage faking -- see worker-entry.test.ts for the test that exercises the
 * real worker.ts bootstrap instead.
 *
 * KNOWN SIM DEFECT still open at time of writing (outside this lane's sim/** ownership, not
 * worked around here per the coordinator's instruction): sim/encounter/scripts.ts's
 * stepEnemyScripts() emits enemy "cast" Commands with no `targetId` at all, so any enemy ability
 * with a "target" shape (e.g. choir.meleeSwing, pair.meleeSwing, hollowWarden.slam) fails
 * sim/combat/casting.ts's startCast ("target_required") and never actually casts -- only
 * cone/circle/self-shaped enemy abilities (hollowWarden.cleave, seed.slam, pair.stackCall, The
 * Lock) resolve. Telegraphs and AoE damage still work; single-target enemy melee is silently
 * inert until this lands (tracked as fix/enemy-targeting upstream).
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";
import type { ActorSnapshot } from "../../../client/src/sim-host/snapshot.js";

const registry = loadContent();

function freshHost(humanClassId: string, keyLevel = 5): SimRuntime {
  const setup = buildRunSetup(registry, { keyLevel, dungeonId: "hollow-vault", humanClassId });
  return new SimRuntime(setup);
}

describe("sim-runtime: SimRuntime steps 200 ticks without throwing", () => {
  for (const humanClassId of ["warden", "cinder", "chanter"]) {
    it(`survives 200 ticks with the human playing ${humanClassId}`, () => {
      const host = freshHost(humanClassId);
      // createRun starts state.tick at 0; the first step() call advances it to 1.
      let lastTick = 0;
      for (let i = 0; i < 200; i++) {
        const snapshot = host.step();
        expect(snapshot.tick).toBe(lastTick + 1);
        lastTick = snapshot.tick;
        for (const actor of snapshot.actors) {
          expect(Number.isFinite(actor.pos.x)).toBe(true);
          expect(Number.isFinite(actor.pos.z)).toBe(true);
          expect(Number.isFinite(actor.health)).toBe(true);
          expect(actor.health).toBeGreaterThanOrEqual(0);
        }
      }
      expect(lastTick).toBe(200);
    });
  }

  it("spawns five actors (the human + four bots) at the first stepped snapshot", () => {
    const host = freshHost("warden");
    const snapshot = host.step();
    const players = snapshot.actors.filter((a: ActorSnapshot) => a.kind === "player");
    expect(players).toHaveLength(5);
  });

  it("bots deal damage to the first pull's enemies over time (Choir engages)", () => {
    const host = freshHost("warden");
    let snapshot = host.step();
    for (let i = 0; i < 199; i++) snapshot = host.step();
    const enemies = snapshot.actors.filter((a: ActorSnapshot) => a.kind === "enemy");
    expect(enemies.length).toBeGreaterThan(0);
    const anyDamaged = enemies.some((e: ActorSnapshot) => e.health < e.maxHealth) || snapshot.pullIndex! > 0;
    expect(anyDamaged).toBe(true);
  });

  it("applies Tempo key-level scaling (higher key = tougher first pull)", () => {
    const low = freshHost("warden", 2);
    const high = freshHost("warden", 20);
    const lowSnap = low.step();
    const highSnap = high.step();
    const lowEnemy = lowSnap.actors.find((a: ActorSnapshot) => a.kind === "enemy")!;
    const highEnemy = highSnap.actors.find((a: ActorSnapshot) => a.kind === "enemy")!;
    expect(highEnemy.maxHealth).toBeGreaterThan(lowEnemy.maxHealth);
  });

  it("reaches a terminal run-end state well before the horizon on an abandon command", () => {
    const host = freshHost("warden");
    host.applyCommand({ type: "abandon", tick: 0 });
    const snapshot = host.step();
    // P2 (policy-inside-stepRun): run-end is now decided inside stepRun itself, stamped with the
    // tick stepRun just advanced TO (1, same as snapshot.tick) -- not the pre-step tick a host-side
    // wrapper used to capture before calling stepRun.
    expect(snapshot.runEnd).toEqual({ outcome: "abandoned", tick: 1 });
  });
});
