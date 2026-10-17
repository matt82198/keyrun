/**
 * tests/client/render/cast-fx.test.ts
 *
 * D10 projections + actor dressing through the composition root: hud-wiring.ts's real runGameLoop,
 * driven by the real client host (SimRuntime over real content), rendering through the REAL
 * render/actors.ts ActorPool and render/cast-fx.ts CastFxPool into tests/client/support/fake-three.ts.
 * A Guard Break press must put a cone projection on the ground and flash what it hit; every actor
 * must carry a name tag that says who it is.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("../support/fake-three.js"));

import * as THREE from "three";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";
import { buildDisplayInfo } from "../../../client/src/sim-host/display-info.js";
import type { AnyCommand } from "../../../client/src/input/commands.js";
import { DEFAULT_KEYBINDS } from "../../../client/src/input/keybinds.js";
import { PingRateLimiter } from "../../../client/src/input/ping.js";
import type { HostHandle } from "../../../client/src/net/host-handle.js";
import { ActorPool, HIT_FLASH_HOSTILE } from "../../../client/src/render/actors.js";
import { CastFxPool } from "../../../client/src/render/cast-fx.js";
import { runGameLoop, type GameLoopDeps } from "../../../client/src/hud-wiring.js";
import type { Snapshot } from "../../../client/src/sim-host/snapshot.js";

let frameCb: (() => void) | null = null;
beforeEach(() => {
  vi.stubGlobal("requestAnimationFrame", (cb: () => void) => ((frameCb = cb), 0));
  vi.stubGlobal("window", { addEventListener: () => undefined });
});
afterEach(() => {
  vi.unstubAllGlobals();
  frameCb = null;
});

const registry = loadContent();

async function boot() {
  const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
  const display = buildDisplayInfo(registry, setup.content.abilities);
  const runtime = new SimRuntime(setup);
  let onSnapshot: ((s: Snapshot) => void) | null = null;
  let pull: (() => AnyCommand[]) | undefined;
  const host: HostHandle = {
    tickMs: 50,
    whenReady: async () => ({ humanActorId: setup.humanActorId, slotAbilities: setup.slotAbilities, display }),
    applyCommand: (c) => runtime.applyCommand(c),
    start(cb, p) {
      onSnapshot = cb;
      pull = p;
      return () => undefined;
    },
  };
  const scene = new THREE.Scene();
  const actorPool = new ActorPool(scene as never);
  const castFx = new CastFxPool(scene as never);
  let pressed = new Set<string>();
  const moveKeys = { forward: false, back: false, left: false, right: false };
  const noop = (): void => undefined;
  runGameLoop({
    simHost: host, humanActorId: setup.humanActorId, slotAbilities: setup.slotAbilities, display, castFx,
    keyState: { sampleAndClearJustPressed: () => { const s = pressed; pressed = new Set(); return s; } },
    moveKeys, pingLimiter: new PingRateLimiter(), keybinds: DEFAULT_KEYBINDS,
    orbitCamera: { yaw: 0, update: noop, camera: {} }, actorPool, telegraphPool: { sync: noop },
    damageNumbers: { spawn: noop, update: noop }, reticle: { update: noop }, pingPool: { sync: noop },
    hud: { updateTimer: noop, updateSlack: noop, updateAbilityBar: noop, updateRunReadout: noop },
    runEndView: { show: noop }, renderer: { render: noop }, scene,
  } as unknown as GameLoopDeps);
  const tick = (keys: string[] = [], forward = false): Snapshot => {
    pressed = new Set(keys);
    moveKeys.forward = forward;
    for (const c of pull!()) runtime.applyCommand(c);
    const snap = runtime.step();
    onSnapshot!(snap);
    frameCb!();
    return snap;
  };
  return { setup, actorPool, castFx, tick };
}

describe("cast projections through the game loop", () => {
  it("Guard Break (key 2) draws a cone on the ground and flashes every enemy it hit", async () => {
    const h = await boot();
    let snap = h.tick();
    // orbit yaw 0: "forward" walks -z, into the two Choir Acolytes.
    for (let i = 0; i < 12; i++) snap = h.tick([], true);
    let hit: number[] = [];
    for (let i = 0; i < 60 && hit.length === 0; i++) {
      snap = h.tick(i % 5 === 0 ? [DEFAULT_KEYBINDS.ability2] : []);
      const e = (snap.events ?? []).find((x) => x.type === "cast" && x.actorId === 1 && x.abilityId === "warden.guardBreak");
      if (e?.type === "cast") hit = e.hitIds;
      if (hit.length > 0) break;
    }
    expect(hit.length).toBeGreaterThan(0);
    expect(h.castFx.activeShapes()).toContain("cone");
    expect(h.castFx.activeLabels()).toBeGreaterThan(0); // "Guard Break" floats over YOU
    for (const id of hit) expect(h.actorPool.flashOf(id, snap.tick)).toBe(HIT_FLASH_HOSTILE);
    // ...and the projection fades: after FX_SECONDS of frames it is gone.
    for (let i = 0; i < 60; i++) h.castFx.update(1 / 60);
    expect(h.castFx.activeShapes()).not.toContain("cone");
  });

  it("every actor wears a name tag: YOU, role-lettered bots, enemies by content name", async () => {
    const h = await boot();
    h.tick();
    const tags = h.actorPool.tagTitles();
    expect(tags[1]).toBe("YOU: Warden");
    expect(tags[2]).toBe("[H] Chanter");
    expect(tags[3]).toBe("[D] Cinder");
    expect(tags[100]).toBe("Choir Acolyte");
    expect(tags[102]).toBe("Choir Cantor");
  });
});
