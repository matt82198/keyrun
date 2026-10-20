/**
 * tests/client/hud-wiring-ping-window.test.ts
 *
 * D10 readability gap items 3, 4 and 6 through the composition root: hud-wiring.ts's real
 * runGameLoop, driven by the client worker host (sim-host/sim-runtime.ts's SimRuntime over the REAL
 * content registry), rendering through the REAL render/actors.ts ActorPool into the recording fake
 * of "three". Assertions read what the loop handed the ping pool and the HUD.
 *
 * 6. A spot ping (G with no target) lands where the camera's crosshair ray meets the floor, not at
 *    the pinger's feet.
 * 4. A target ping reads "Kick!" while the target is mid-way through an interruptible cast,
 *    "Window!" while it carries an open Window, else "Focus" (render/pingLabel.ts's context).
 * 3. While an enemy carries a Window your class can close, the HUD bar is told which payoff
 *    abilities are live (render/payoff-ready.ts).
 */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("./support/fake-three.js"));

import * as THREE from "three";
import { loadContent } from "@content/index";
import { buildRunSetup, type RunSetup } from "../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../client/src/sim-host/sim-runtime.js";
import { buildDisplayInfo } from "../../client/src/sim-host/display-info.js";
import type { Snapshot } from "../../client/src/sim-host/snapshot.js";
import type { AnyCommand } from "../../client/src/input/commands.js";
import { DEFAULT_KEYBINDS } from "../../client/src/input/keybinds.js";
import { PingRateLimiter, type PingEvent } from "../../client/src/input/ping.js";
import type { HostHandle } from "../../client/src/net/host-handle.js";
import type { PingWorldPlacement } from "../../client/src/render/pings.js";
import { ActorPool } from "../../client/src/render/actors.js";
import { runGameLoop, type GameLoopDeps } from "../../client/src/hud-wiring.js";

const LIEUTENANT = 102;
const HEAL = "choir.healCast";
const G = DEFAULT_KEYBINDS.ping;

interface PingSync {
  events: PingEvent[];
  placementFor: (e: PingEvent) => PingWorldPlacement | null;
  labelFor: (e: PingEvent) => string;
}

interface Harness {
  tick(pressed?: string[], forward?: boolean): Snapshot;
  lastPing(): { event: PingEvent; placement: PingWorldPlacement | null; label: string };
  payoffArgs: (ReadonlySet<string> | undefined)[];
}

let frameCb: (() => void) | null = null;
beforeEach(() => {
  vi.stubGlobal("requestAnimationFrame", (cb: () => void) => { frameCb = cb; return 0; });
  vi.stubGlobal("window", { addEventListener: () => undefined });
});
afterEach(() => {
  vi.unstubAllGlobals();
  frameCb = null;
});

/** The fake camera sits at (3, 8, 4) m looking along (0.5, -1, -1): its centre ray meets y=0 at (7, 0, -4). */
function fakeCamera() {
  return {
    position: new THREE.Vector3(3, 8, 4),
    getWorldDirection: (t: THREE.Vector3) => t.set(0.5, -1, -1),
  };
}

async function harness(setup: RunSetup): Promise<Harness> {
  const runtime = new SimRuntime(setup);
  let onSnapshot: ((s: Snapshot) => void) | null = null;
  let pull: (() => AnyCommand[]) | undefined;
  const host: HostHandle = {
    tickMs: 50,
    whenReady: async () => ({ humanActorId: setup.humanActorId, slotAbilities: setup.slotAbilities }),
    applyCommand: (c) => runtime.applyCommand(c),
    start(cb, p) { onSnapshot = cb; pull = p; return () => undefined; },
  };
  const scene = new THREE.Scene();
  let pressed = new Set<string>();
  const moveKeys = { forward: false, back: false, left: false, right: false };
  const noop = (): void => undefined;
  let sync: PingSync | null = null;
  const payoffArgs: (ReadonlySet<string> | undefined)[] = [];
  const deps = {
    simHost: host,
    humanActorId: setup.humanActorId,
    slotAbilities: setup.slotAbilities,
    display: buildDisplayInfo(registry, setup.content.abilities),
    keyState: { sampleAndClearJustPressed: () => { const s = pressed; pressed = new Set(); return s; } },
    moveKeys,
    pingLimiter: new PingRateLimiter(),
    keybinds: DEFAULT_KEYBINDS,
    orbitCamera: { yaw: 0, update: noop, camera: fakeCamera() },
    actorPool: new ActorPool(scene as never),
    telegraphPool: { sync: noop },
    damageNumbers: { spawn: noop, update: noop },
    reticle: { update: noop },
    pingPool: { sync: (events: PingEvent[], _t: number, placementFor: PingSync["placementFor"], labelFor: PingSync["labelFor"]) => { sync = { events, placementFor, labelFor }; } },
    hud: { updateTimer: noop, updateSlack: noop, updateRunReadout: noop, updateAbilityBar: (_c: unknown, _s: unknown, _g: unknown, payoff?: ReadonlySet<string>) => { payoffArgs.push(payoff); } },
    runEndView: { show: noop },
    renderer: { render: noop },
    scene,
  } as unknown as GameLoopDeps;
  runGameLoop(deps);
  return {
    tick(keys = [], forward = false) {
      pressed = new Set(keys);
      moveKeys.forward = forward;
      for (const c of pull!()) runtime.applyCommand(c);
      const snap = runtime.step();
      onSnapshot!(snap);
      frameCb!();
      return snap;
    },
    lastPing() {
      const s = sync!;
      const event = s.events[s.events.length - 1]!;
      return { event, placement: s.placementFor(event), label: s.labelFor(event) };
    },
    payoffArgs,
  };
}

const registry = loadContent();
const setupFor = (humanClassId: string): RunSetup => buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId });
const lt = (s: Snapshot) => s.actors.find((a) => a.id === LIEUTENANT)!;

async function targetingLieutenant(): Promise<{ h: Harness; snap: Snapshot }> {
  const h = await harness(setupFor("warden"));
  let snap = h.tick();
  for (let i = 0; i < 14; i++) snap = h.tick([], true);
  for (let i = 0; i < 10 && snap.actors.find((a) => a.id === 1)!.targetId !== LIEUTENANT; i++) snap = h.tick([DEFAULT_KEYBINDS.targetCycle]);
  expect(snap.actors.find((a) => a.id === 1)!.targetId).toBe(LIEUTENANT);
  return { h, snap };
}

describe("hud-wiring: a spot ping lands at the crosshair's floor point (item 6)", () => {
  it("G with no target pings the aimed ground point, not the pinger", async () => {
    const h = await harness(setupFor("warden"));
    const first = h.tick();
    const me = first.actors.find((a) => a.id === 1)!;
    expect(me.targetId).toBeNull();
    h.tick([G]);
    const { event, placement, label } = h.lastPing();
    expect(event.kind).toBe("spot");
    expect(event.point).toEqual({ x: 7000, z: -4000 });
    expect(placement).toMatchObject({ x: 7, y: 0, z: -4 });
    expect(label).toBe("Stack here");
    expect(Math.hypot(placement!.x - me.pos.x / 1000, placement!.z - me.pos.z / 1000)).toBeGreaterThan(1);
  });
});

describe("hud-wiring: target pings carry cast/window context (item 4)", () => {
  it("reads Kick! when pinged mid-way through the lieutenant's interruptible heal", async () => {
    const { h } = await targetingLieutenant();
    let snap = h.tick();
    for (let i = 0; i < 300 && !(lt(snap).cast?.abilityId === HEAL && lt(snap).cast?.kickable); i++) snap = h.tick();
    expect(lt(snap).cast?.kickable).toBe(true);
    h.tick([G]);
    const { event, label } = h.lastPing();
    expect(event).toMatchObject({ kind: "target", targetId: LIEUTENANT });
    expect(label).toBe("Kick!");
  });

  it("reads Window! on a target carrying an open Window and no kickable cast, Focus otherwise", async () => {
    const { h, snap: start } = await targetingLieutenant();
    let snap = start;
    for (let i = 0; i < 300 && (lt(snap).cast?.kickable || lt(snap).window); i++) snap = h.tick();
    expect(lt(snap).cast?.kickable ?? false).toBe(false);
    h.tick([G]);
    expect(h.lastPing().label).toBe("Focus");
    for (let i = 0; i < 300 && !(lt(snap).window && !lt(snap).cast?.kickable && snap.tick > 30); i++) snap = h.tick([DEFAULT_KEYBINDS.ability1]);
    expect(lt(snap).window).toBeTruthy();
    h.tick([G]);
    expect(h.lastPing().label).toBe("Window!");
  });
});

describe("hud-wiring: the bar is told which payoff keys a Window has made live (item 3)", () => {
  // The cinder BOTS open Kindled (a cinder setup) on enemies; warden closes it as Forge.
  it("as warden: passes Guard Break + Reckoning while an enemy carries a cinder Window, and none otherwise", async () => {
    const h = await harness(setupFor("warden"));
    let lit = 0;
    let unlitWithoutWindow = 0;
    for (let i = 0; i < 400; i++) {
      const snap = h.tick([], i < 10);
      const arg = h.payoffArgs[h.payoffArgs.length - 1];
      const closable = snap.actors.some((a) => a.kind === "enemy" && a.alive && a.window?.setupClass === "cinder" && snap.tick < a.window.endsTick);
      if (closable) {
        expect([...(arg ?? [])].sort()).toEqual(["warden.guardBreak", "warden.reckoning"]);
        lit += 1;
      } else if (!snap.actors.some((a) => a.kind === "enemy" && a.window)) {
        expect(arg?.size ?? 0).toBe(0);
        unlitWithoutWindow += 1;
      }
    }
    expect(lit).toBeGreaterThan(0);
    expect(unlitWithoutWindow).toBeGreaterThan(0);
  });

  it("as cinder: your own class's Kindled never lights your bar (no self-combo)", async () => {
    const h = await harness(setupFor("cinder"));
    // BOTS-FIVE-BUTTONS: the warden bot now casts Strike, so Sundered (a warden Window the Cinder closes as
    // Shatter) also opens; on those ticks the bar lights with exactly Cinder's Payoffs. Every other tick,
    // including every tick an enemy carries only Kindled, the bar must be unlit.
    let kindledOnlyTicks = 0;
    let otherClassLit = 0;
    for (let i = 0; i < 400; i++) {
      const snap = h.tick([], i < 10);
      const arg = h.payoffArgs[h.payoffArgs.length - 1];
      const otherClosable = snap.actors.some((a) => a.kind === "enemy" && a.alive && a.window && a.window.setupClass !== "cinder" && snap.tick < a.window.endsTick);
      if (otherClosable) {
        expect([...(arg ?? [])].sort()).toEqual(["cinder.coldSnap", "cinder.detonate"]);
        otherClassLit += 1;
        continue;
      }
      if (snap.actors.some((a) => a.kind === "enemy" && a.window?.setupClass === "cinder")) kindledOnlyTicks += 1;
      expect(arg?.size ?? 0).toBe(0);
    }
    expect(kindledOnlyTicks).toBeGreaterThan(0);
    expect(otherClassLit).toBeGreaterThan(0);
  });
});
