/**
 * tests/client/hud-wiring-event-latch.test.ts
 *
 * The client host may deliver several snapshots per render frame (a worker catching up, or net
 * backfill); hud-wiring.ts renders only the latest STATE. Snapshot.events are one-shot (present in
 * exactly one snapshot, never repeated), so an event carried by a snapshot that is overwritten
 * before a frame consumes it must still reach ActorPool.sync -- otherwise a landed kick never
 * flashes (STATE.md standing rule: a mechanic not wired is a P0). Drives the REAL runGameLoop with
 * a recording actorPool; asserts on what the loop actually handed the renderer.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("./support/fake-three.js"));

import type { Snapshot, SnapshotEvent } from "../../client/src/sim-host/snapshot.js";
import type { HostHandle } from "../../client/src/net/host-handle.js";
import { runGameLoop, type GameLoopDeps } from "../../client/src/hud-wiring.js";
import { MAX_LATCHED_EVENTS } from "../../client/src/render/event-latch.js";

let frameCb: (() => void) | null = null;

beforeEach(() => {
  vi.stubGlobal("requestAnimationFrame", (cb: () => void) => {
    frameCb = cb;
    return 0;
  });
  vi.stubGlobal("window", { addEventListener: () => undefined });
});

afterEach(() => {
  vi.unstubAllGlobals();
  frameCb = null;
});

function kick(tick: number, actorId: number): SnapshotEvent {
  return { type: "interrupt", tick, actorId, sourceId: 1, school: "physical" };
}

function snap(tick: number, events?: SnapshotEvent[]): Snapshot {
  const base: Snapshot = { tick, actors: [], telegraphs: [], runTimerTicks: tick, timerLimitTicks: 1000 };
  return events ? { ...base, events } : base;
}

interface SyncCall {
  tick: number;
  events: SnapshotEvent[];
}

function boot(): { push(s: Snapshot): void; frame(): void; calls: SyncCall[] } {
  let onSnapshot: ((s: Snapshot) => void) | null = null;
  const host: HostHandle = {
    tickMs: 50,
    whenReady: async () => ({ humanActorId: 1 }),
    applyCommand: () => undefined,
    start(cb) {
      onSnapshot = cb;
      return () => undefined;
    },
  };
  const calls: SyncCall[] = [];
  const noop = (): void => undefined;
  const deps = {
    simHost: host,
    humanActorId: 1,
    slotAbilities: undefined,
    keyState: { sampleAndClearJustPressed: () => new Set<string>() },
    moveKeys: { forward: false, back: false, left: false, right: false },
    pingLimiter: {},
    keybinds: {},
    orbitCamera: { yaw: 0, update: noop, camera: {} },
    actorPool: {
      sync: (_i: unknown, _r: unknown, tick: number, events?: readonly SnapshotEvent[]) => calls.push({ tick, events: [...(events ?? [])] }),
      worldPositionOf: () => null,
    },
    telegraphPool: { sync: noop },
    damageNumbers: { spawn: noop, update: noop },
    reticle: { update: noop },
    pingPool: { sync: noop },
    hud: { updateTimer: noop, updateSlack: noop, updateAbilityBar: noop },
    runEndView: { show: noop },
    renderer: { render: noop },
    scene: {},
  } as unknown as GameLoopDeps;
  runGameLoop(deps);
  return { push: (s) => onSnapshot!(s), frame: () => frameCb!(), calls };
}

describe("hud-wiring: one-shot events survive snapshots skipped between render frames", () => {
  it("two snapshots with distinct events before one frame: both event sets reach ActorPool.sync, state is the latest", () => {
    const loop = boot();
    const a = kick(10, 101);
    const b = kick(11, 102);
    loop.push(snap(10, [a]));
    loop.push(snap(11, [b]));
    loop.frame();
    expect(loop.calls).toHaveLength(1);
    expect(loop.calls[0]!.tick).toBe(11);
    expect(loop.calls[0]!.events).toEqual([a, b]);
  });

  it("consumed events are cleared: the next frame with no new snapshot delivers none", () => {
    const loop = boot();
    loop.push(snap(10, [kick(10, 101)]));
    loop.frame();
    loop.frame();
    expect(loop.calls.map((c) => c.events.length)).toEqual([1, 0]);
  });

  it("a snapshot with no events between two eventful ones loses nothing", () => {
    const loop = boot();
    const a = kick(20, 101);
    const c = kick(22, 103);
    loop.push(snap(20, [a]));
    loop.push(snap(21));
    loop.push(snap(22, [c]));
    loop.frame();
    expect(loop.calls[0]!.tick).toBe(22);
    expect(loop.calls[0]!.events).toEqual([a, c]);
  });

  it("a stalled render loop is bounded: past MAX_LATCHED_EVENTS the oldest events drop, the newest survive", () => {
    const loop = boot();
    const total = MAX_LATCHED_EVENTS + 5;
    for (let t = 0; t < total; t++) loop.push(snap(t, [kick(t, 100 + t)]));
    loop.frame();
    const got = loop.calls[0]!.events;
    expect(got).toHaveLength(MAX_LATCHED_EVENTS);
    expect(got[0]!.tick).toBe(5);
    expect(got[got.length - 1]!.tick).toBe(total - 1);
  });
});
