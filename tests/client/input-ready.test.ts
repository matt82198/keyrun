/**
 * tests/client/input-ready.test.ts
 *
 * client/src/input-ready.ts through its two real producers (WINDOW-PING-READY, finding f-d75b278e):
 *
 * 1. readability.ts's mountReadability binds the REAL hud/onboarding.ts overlay: `window.__keyrunInputReady`
 *    is false before the first keydown dismisses onboarding and true after (with the input sampler live).
 * 2. hud-wiring.ts's real runGameLoop reports its sampler ticks, and stamps a G press with the SIM tick:
 *    when the sim (a Worker, its own thread) runs far ahead of the main-thread sampler -- a slow CI runner --
 *    the ping is still unexpired for the ping pool (render/pingLabel.ts isPingExpired). Stamped with the
 *    lagging local tick, every ping was born expired and never drawn (the window-ping.e2e.ts flake).
 */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("./support/fake-three.js"));

import * as THREE from "three";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../client/src/sim-host/sim-runtime.js";
import type { Snapshot } from "../../client/src/sim-host/snapshot.js";
import type { AnyCommand } from "../../client/src/input/commands.js";
import { DEFAULT_KEYBINDS } from "../../client/src/input/keybinds.js";
import { PingRateLimiter, type PingEvent } from "../../client/src/input/ping.js";
import type { HostHandle } from "../../client/src/net/host-handle.js";
import { ActorPool } from "../../client/src/render/actors.js";
import { isPingExpired } from "../../client/src/render/pingLabel.js";
import { runGameLoop, type GameLoopDeps } from "../../client/src/hud-wiring.js";
import { mountReadability } from "../../client/src/readability.js";
import { inputReadiness } from "../../client/src/input-ready.js";

class FakeEl {
  children: FakeEl[] = [];
  className = "";
  hidden = false;
  textContent = "";
  dataset: Record<string, string> = {};
  style: Record<string, string> = {};
  constructor(readonly tagName: string) {}
  appendChild(c: FakeEl): FakeEl {
    this.children.push(c);
    return c;
  }
  get firstChild(): FakeEl | null {
    return this.children[0] ?? null;
  }
}

/** A window stand-in whose keydown listeners can be fired, like a real key press. */
function fakeWindow() {
  const listeners = new Map<string, Set<() => void>>();
  return {
    addEventListener: (type: string, fn: () => void) => void (listeners.get(type) ?? listeners.set(type, new Set()).get(type)!).add(fn),
    removeEventListener: (type: string, fn: () => void) => void listeners.get(type)?.delete(fn),
    press: () => [...(listeners.get("keydown") ?? [])].forEach((fn) => fn()),
  };
}

let win: ReturnType<typeof fakeWindow>;
let frameCb: (() => void) | null = null;

beforeEach(() => {
  win = fakeWindow();
  vi.stubGlobal("window", win);
  const canvasDoc = globalThis.document; // fake-three.ts's canvas-only document, kept for the render pools
  const createElement = (t: string): unknown => (t === "canvas" ? canvasDoc.createElement(t) : new FakeEl(t));
  vi.stubGlobal("document", { createElement, createTextNode: (t: string) => ({ textContent: t }) });
  vi.stubGlobal("requestAnimationFrame", (cb: () => void) => {
    frameCb = cb;
    return 0;
  });
  inputReadiness.noteInputTick(0, null);
});

afterEach(() => {
  vi.unstubAllGlobals();
  frameCb = null;
});

const inputReady = (): boolean | undefined => (win as unknown as Window).__keyrunInputReady;

describe("input-ready: window.__keyrunInputReady follows onboarding dismissal (readability.ts)", () => {
  it("is false before the first keydown dismisses onboarding, and true after", () => {
    mountReadability(new FakeEl("div") as unknown as HTMLElement, new THREE.Scene() as never, DEFAULT_KEYBINDS, {} as ActorPool);
    inputReadiness.noteInputTick(3, 40); // the input sampler is live with a snapshot in hand
    expect(inputReady()).toBe(false);
    win.press();
    expect(inputReady()).toBe(true);
  });

  it("stays false after dismissal until the input sampler has run with a snapshot", () => {
    mountReadability(new FakeEl("div") as unknown as HTMLElement, new THREE.Scene() as never, DEFAULT_KEYBINDS, {} as ActorPool);
    win.press();
    expect(inputReady()).toBe(false);
    inputReadiness.noteInputTick(1, null);
    expect(inputReady()).toBe(false);
    inputReadiness.noteInputTick(2, 7);
    expect(inputReady()).toBe(true);
  });
});

describe("input-ready: the real game loop reports its ticks and stamps pings with the sim tick", () => {
  it("a G press while the sim runs 100 ticks ahead of the sampler reaches the ping pool unexpired", async () => {
    const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    const runtime = new SimRuntime(setup);
    let onSnapshot: ((s: Snapshot) => void) | null = null;
    let pull: (() => AnyCommand[]) | undefined;
    const host: HostHandle = {
      tickMs: 50,
      whenReady: async () => ({ humanActorId: setup.humanActorId, slotAbilities: setup.slotAbilities }),
      applyCommand: (c) => runtime.applyCommand(c),
      start(cb, p) {
        onSnapshot = cb;
        pull = p;
        return () => undefined;
      },
    };
    const scene = new THREE.Scene();
    let pressed = new Set<string>();
    const synced: Array<{ events: PingEvent[]; tick: number }> = [];
    const noop = (): void => undefined;
    runGameLoop({
      simHost: host,
      humanActorId: setup.humanActorId,
      slotAbilities: setup.slotAbilities,
      keyState: { sampleAndClearJustPressed: () => { const s = pressed; pressed = new Set(); return s; } },
      moveKeys: { forward: false, back: false, left: false, right: false },
      pingLimiter: new PingRateLimiter(),
      keybinds: DEFAULT_KEYBINDS,
      orbitCamera: { yaw: 0, update: noop, camera: { position: new THREE.Vector3(3, 8, 4), getWorldDirection: (t: THREE.Vector3) => t.set(0.5, -1, -1) } },
      actorPool: new ActorPool(scene as never),
      telegraphPool: { sync: noop },
      damageNumbers: { spawn: noop, update: noop },
      reticle: { update: noop },
      pingPool: { sync: (events: PingEvent[], tick: number) => void synced.push({ events: [...events], tick }) },
      hud: { updateTimer: noop, updateSlack: noop, updateAbilityBar: noop, updateRunReadout: noop },
      runEndView: { show: noop },
      renderer: { render: noop },
      scene,
    } as unknown as GameLoopDeps);

    let last!: Snapshot;
    for (let i = 0; i < 100; i++) onSnapshot!((last = runtime.step())); // the Worker ticks on; the sampler is starved
    pressed = new Set([DEFAULT_KEYBINDS.ping]);
    for (const c of pull!()) runtime.applyCommand(c); // the sampler's FIRST tick: localTick 1
    frameCb!();

    expect(inputReadiness.inputTicks).toEqual({ input: 1, sim: last.tick });
    const frame = synced[synced.length - 1]!;
    expect(frame.tick).toBe(last.tick);
    expect(frame.events).toHaveLength(1);
    expect(frame.events[0]!.tick).toBe(last.tick);
    expect(isPingExpired(frame.events[0]!, frame.tick)).toBe(false);
  });
});
