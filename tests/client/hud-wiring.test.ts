/**
 * tests/client/hud-wiring.test.ts
 *
 * D10 readability through the composition root (STATE.md standing rule): hud-wiring.ts's real
 * runGameLoop, driven by the client worker host (sim-host/sim-runtime.ts's SimRuntime -- the class
 * worker.ts steps -- built from the REAL content registry by run-setup.ts), rendering through the
 * REAL render/actors.ts ActorPool into a recording fake of "three" (tests/client/support/
 * fake-three.ts). Key presses go through the loop's own input closure (keyState -> deriveAbilityCommands
 * with the host's slot map); assertions read what the pool actually drew.
 *
 * 1. Kick flash: a landed Shield Bash on the Choir lieutenant (actor 102, choir.healCast,
 *    interruptible) must flash its cast bar INTERRUPT_FLASH_FILL -- only possible if the loop hands
 *    the snapshot's one-shot events to ActorPool.sync.
 * 2. Boss colour: the boss (classId "hollowWarden.boss", content-dungeon.ts's boss pull) renders
 *    COLOR_BOSS; an ordinary enemy stays COLOR_ENEMY.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("./support/fake-three.js"));

import * as THREE from "three";
import { loadContent } from "@content/index";
import { buildRunSetup, type RunSetup } from "../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../client/src/sim-host/sim-runtime.js";
import type { Snapshot } from "../../client/src/sim-host/snapshot.js";
import type { AnyCommand } from "../../client/src/input/commands.js";
import { DEFAULT_KEYBINDS } from "../../client/src/input/keybinds.js";
import { PingRateLimiter } from "../../client/src/input/ping.js";
import type { HostHandle } from "../../client/src/net/host-handle.js";
import { ActorPool } from "../../client/src/render/actors.js";
import { INTERRUPT_FLASH_FILL } from "../../client/src/render/castBar.js";
import { COLOR_BOSS, COLOR_ENEMY } from "../../client/src/render/roleColor.js";
import { runGameLoop, type GameLoopDeps } from "../../client/src/hud-wiring.js";
import type { FakeCanvas, Group, Mesh, Sprite } from "./support/fake-three.js";

const LIEUTENANT = 102;
const HEAL = "choir.healCast";

interface Harness {
  /** One tick: pull input -> host.step() -> onSnapshot -> one render frame. */
  tick(pressed?: string[], forward?: boolean): Snapshot;
  groupOf(id: number): Group;
}

function castFill(group: Group): string | null {
  const castBar = group.children[3] as Sprite;
  if (!castBar.visible) return null;
  return ((castBar.material.map!.image as FakeCanvas).ctx.fills[1] ?? null);
}

function capsuleHex(group: Group): number {
  return (group.children[0] as Mesh).material.color.getHex();
}

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

async function harness(setup: RunSetup): Promise<Harness> {
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
  const moveKeys = { forward: false, back: false, left: false, right: false };
  const noop = (): void => undefined;
  const ready = await host.whenReady();
  const deps = {
    simHost: host,
    humanActorId: ready.humanActorId,
    slotAbilities: ready.slotAbilities,
    keyState: { sampleAndClearJustPressed: () => { const s = pressed; pressed = new Set(); return s; } },
    moveKeys,
    pingLimiter: new PingRateLimiter(),
    keybinds: DEFAULT_KEYBINDS,
    orbitCamera: { yaw: 0, update: noop, camera: {} },
    actorPool: new ActorPool(scene as never),
    telegraphPool: { sync: noop },
    damageNumbers: { spawn: noop, update: noop },
    reticle: { update: noop },
    pingPool: { sync: noop },
    hud: { updateTimer: noop, updateSlack: noop, updateAbilityBar: noop },
    runEndView: { show: noop },
    renderer: { render: noop },
    scene,
  } as unknown as GameLoopDeps;
  runGameLoop(deps);

  const groups = new Map<number, Group>();
  return {
    tick(keys = [], forward = false) {
      pressed = new Set(keys);
      moveKeys.forward = forward;
      for (const c of pull!()) runtime.applyCommand(c);
      const snap = runtime.step();
      onSnapshot!(snap);
      const before = scene.children.length;
      frameCb!();
      const fresh = snap.actors.map((a) => a.id).filter((id) => !groups.has(id));
      const added = scene.children.slice(before) as Group[];
      fresh.forEach((id, i) => groups.set(id, added[i]!));
      return snap;
    },
    groupOf(id) {
      const g = groups.get(id);
      if (!g) throw new Error(`no render group for actor ${id}`);
      return g;
    },
  };
}

const registry = loadContent();
const wardenSetup = (): RunSetup => buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });

describe("hud-wiring: a landed kick flashes the kicked actor's cast bar", () => {
  it("Shield Bash (KeyQ via the host slot map) on the lieutenant's heal shows INTERRUPT_FLASH_FILL", async () => {
    const h = await harness(wardenSetup());
    let snap = h.tick();
    for (let i = 0; i < 14; i++) snap = h.tick([], true); // walk into Shield Bash range (-z)
    for (let i = 0; i < 10 && snap.actors.find((a) => a.id === 1)!.targetId !== LIEUTENANT; i++) snap = h.tick([DEFAULT_KEYBINDS.targetCycle]);
    expect(snap.actors.find((a) => a.id === 1)!.targetId).toBe(LIEUTENANT);

    let kickedAt = -1;
    let flashed = false;
    let landed = false;
    for (let i = 0; i < 300 && !flashed; i++) {
      const lt = snap.actors.find((a) => a.id === LIEUTENANT)!;
      const kick = kickedAt < 0 && lt.cast?.abilityId === HEAL;
      snap = h.tick(kick ? [DEFAULT_KEYBINDS.abilityQ] : []);
      if (kick) kickedAt = snap.tick;
      if ((snap.events ?? []).some((e) => e.type === "interrupt" && e.actorId === LIEUTENANT && e.sourceId === 1)) {
        landed = true;
        flashed = castFill(h.groupOf(LIEUTENANT)) === INTERRUPT_FLASH_FILL;
        break;
      }
    }
    expect(kickedAt).toBeGreaterThan(0);
    expect(landed).toBe(true);
    expect(flashed).toBe(true);
    // ...and it holds on the next frame too (the latch outlives the one-shot event).
    h.tick();
    expect(castFill(h.groupOf(LIEUTENANT))).toBe(INTERRUPT_FLASH_FILL);
  });
});

describe("hud-wiring: the boss is visually distinct from ordinary enemies", () => {
  it("an ordinary enemy (the lieutenant) is COLOR_ENEMY", async () => {
    const h = await harness(wardenSetup());
    h.tick();
    expect(capsuleHex(h.groupOf(LIEUTENANT))).toBe(COLOR_ENEMY);
  });

  it("the boss (classId hollowWarden.boss from the real boss pull) is COLOR_BOSS", async () => {
    const setup = wardenSetup();
    const bossPull = setup.dungeon.pulls.find((p) => p.id === "boss")!;
    const bossId = bossPull.spawns[0]!.actorId;
    const h = await harness({ ...setup, dungeon: { ...setup.dungeon, pulls: [bossPull] } });
    const snap = h.tick();
    expect(snap.actors.find((a) => a.id === bossId)?.classId).toBe("hollowWarden.boss");
    expect(COLOR_BOSS).toBe(0x9b59b6);
    expect(capsuleHex(h.groupOf(bossId))).toBe(COLOR_BOSS);
  });
});
