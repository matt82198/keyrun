/**
 * tests/client/sim-host/worker-smoke.test.ts
 *
 * The dispatch brief's headless smoke test: constructs SimHost with the REAL content registry
 * (content/index.ts's loadContent(), which works fine under Node/Vitest -- see run-setup.ts's
 * docstring for why the browser path is a separate fetch-based loader), the real sim, and real
 * bots/src bots, then steps 200 ticks without throwing. No DOM/Three.js touched anywhere in this
 * path (SimHost.step() is pure state advancement), so this needs no jsdom.
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
import { SimHost } from "../../../client/src/sim-host/worker.js";

const registry = loadContent();

function freshHost(humanClassId: string, keyLevel = 5): SimHost {
  const setup = buildRunSetup(registry, { keyLevel, dungeonId: "hollow-vault", humanClassId });
  return new SimHost(setup);
}

describe("worker-smoke: SimHost steps 200 ticks without throwing", () => {
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
    const players = snapshot.actors.filter((a) => a.kind === "player");
    expect(players).toHaveLength(5);
  });

  it("bots deal damage to the first pull's enemies over time (Choir engages)", () => {
    const host = freshHost("warden");
    let snapshot = host.step();
    for (let i = 0; i < 199; i++) snapshot = host.step();
    const enemies = snapshot.actors.filter((a) => a.kind === "enemy");
    expect(enemies.length).toBeGreaterThan(0);
    const anyDamaged = enemies.some((e) => e.health < e.maxHealth) || snapshot.pullIndex! > 0;
    expect(anyDamaged).toBe(true);
  });

  it("applies Tempo key-level scaling (higher key = tougher first pull)", () => {
    const low = freshHost("warden", 2);
    const high = freshHost("warden", 20);
    const lowSnap = low.step();
    const highSnap = high.step();
    const lowEnemy = lowSnap.actors.find((a) => a.kind === "enemy")!;
    const highEnemy = highSnap.actors.find((a) => a.kind === "enemy")!;
    expect(highEnemy.maxHealth).toBeGreaterThan(lowEnemy.maxHealth);
  });

  it("reaches a terminal run-end state well before the horizon on an abandon command", () => {
    const host = freshHost("warden");
    host.applyCommand({ type: "abandon", tick: 0 });
    const snapshot = host.step();
    expect(snapshot.runEnd).toEqual({ outcome: "abandoned", tick: 0 });
  });
});
