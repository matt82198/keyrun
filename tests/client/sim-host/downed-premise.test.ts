/**
 * tests/client/sim-host/downed-premise.test.ts
 *
 * The premise of tests/client/e2e/status-net.e2e.ts "a dead party member stays on the floor as a DOWNED tag": the
 * local playable it opens (key 2, Hollow Vault, an idle human Warden plus four bots, seed 1) must actually lose a
 * party member, early enough for the e2e's 120 s wait at ?hz=120. Without this, a content or bot change that keeps
 * the whole party alive shows up only as a page.waitForFunction TimeoutError two minutes into the browser suite
 * (SLAM-TELEGRAPH: the Pair's leadTicks 140 did exactly that at key 5, where the run now times with nobody dead).
 * Drives the same SimRuntime the browser worker steps, headless, with no input.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";

/** Must match LOCAL_DOWNED's key in status-net.e2e.ts. */
const DOWNED_KEY = 2;
/** 25 s of sim at ?hz=120: well inside the e2e's 120 s wait. */
const DEADLINE_TICKS = 3000;

function firstPlayerDeathTick(keyLevel: number): number | null {
  const setup = buildRunSetup(loadContent(), { keyLevel, dungeonId: "hollow-vault", humanClassId: "warden" });
  const host = new SimRuntime(setup);
  for (let i = 0; i < DEADLINE_TICKS; i++) {
    const snapshot = host.step();
    if (snapshot.actors.some((a) => a.kind === "player" && a.health <= 0)) return snapshot.tick;
    if (snapshot.runEnd) return null;
  }
  return null;
}

describe("DOWNED e2e premise", () => {
  it(`a party member dies within ${DEADLINE_TICKS} ticks in the key-${DOWNED_KEY} local playable`, () => {
    const tick = firstPlayerDeathTick(DOWNED_KEY);
    expect(tick, `no player died by tick ${DEADLINE_TICKS} at key ${DOWNED_KEY}: the DOWNED e2e would time out`).not.toBeNull();
  });
});
