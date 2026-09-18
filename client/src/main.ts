// Boot: wires input, camera, sim-host, and the render layer together.
// Game-rule-free: this only turns input into commands and renders
// snapshots; sim-host/worker.ts owns all state.
//
// URL params: ?key=<2-20>&dungeon=hollow-vault&class=<warden|cinder|chanter> pick the run's Tempo
// level, dungeon, and which of the five fixed party slots the human plays (see
// sim-host/content-party.ts -- content ships three classes, not four; unknown/missing values fall
// back to sane defaults with a console warning rather than failing to boot).
import * as THREE from "three";
import { loadKeybinds, DEFAULT_KEYBINDS, type Keybinds } from "./input/keybinds.js";
import { KeyState } from "./input/keyState.js";
import { deriveAbilityCommands, type AnyCommand } from "./input/commands.js";
import { derivePingEvent, PingRateLimiter, type PingEvent, type PingGroundPoint } from "./input/ping.js";
import { computeMoveDeltaMm, type MoveKeyState } from "./camera/movement.js";
import { OrbitCamera } from "./camera/orbitCamera.js";
import { createScene } from "./render/scene.js";
import { ActorPool } from "./render/actors.js";
import { TelegraphPool } from "./render/telegraphs.js";
import { DamageNumberField } from "./render/damageNumbers.js";
import { TargetReticle } from "./render/reticle.js";
import { Hud } from "./render/hud.js";
import { PingMarkerPool, pingLabelText, type PingWorldPlacement } from "./render/pings.js";
import { SimHost } from "./sim-host/worker.js";
import { interpolateSnapshots } from "./sim-host/interpolate.js";
import type { ActorSnapshot, Snapshot } from "./sim-host/snapshot.js";
import { SIM_TICK_MS } from "./sim-host/snapshot.js";
import { fetchContentRegistry } from "./sim-host/content-registry.js";
import { buildRunSetup } from "./sim-host/run-setup.js";
import { resolveRunSetupOptions } from "./sim-host/boot-params.js";
import { deriveHostControlCommands } from "./sim-host/host-commands.js";
import { RunEndView } from "./hud/run-end.js";

const MOVE_SPEED_MM_PER_TICK = 180; // ~3.6 m/s at 20Hz

const MOVE_CODES: Record<string, keyof MoveKeyState> = {
  KeyW: "forward",
  KeyS: "back",
  KeyA: "left",
  KeyD: "right",
};

async function boot(): Promise<void> {
  const app = document.getElementById("app");
  if (!app) throw new Error("#app container missing from index.html");

  const canvas = document.createElement("canvas");
  canvas.className = "scene-canvas";
  app.appendChild(canvas);

  let keybinds: Keybinds;
  try {
    keybinds = await loadKeybinds("./keybinds.json");
  } catch {
    keybinds = DEFAULT_KEYBINDS;
  }

  const { scene, renderer } = createScene(canvas);
  const orbitCamera = new OrbitCamera(canvas, window.innerWidth / window.innerHeight);
  const actorPool = new ActorPool(scene);
  const telegraphPool = new TelegraphPool(scene);
  const damageNumbers = new DamageNumberField(scene);
  const reticle = new TargetReticle(scene);
  const pingPool = new PingMarkerPool(scene);
  const hud = new Hud(app, keybinds);
  const runEndView = new RunEndView(app);

  const keyState = new KeyState();
  const moveKeys: MoveKeyState = { forward: false, back: false, left: false, right: false };
  const pingLimiter = new PingRateLimiter();
  const pingEvents: PingEvent[] = [];

  window.addEventListener("keydown", (ev) => {
    if (ev.repeat) return;
    keyState.onKeyDown(ev.code);
    const moveField = MOVE_CODES[ev.code];
    if (moveField) moveKeys[moveField] = true;
  });
  window.addEventListener("keyup", (ev) => {
    keyState.onKeyUp(ev.code);
    const moveField = MOVE_CODES[ev.code];
    if (moveField) moveKeys[moveField] = false;
  });

  function resize(): void {
    const width = window.innerWidth;
    const height = window.innerHeight;
    renderer.setSize(width, height);
    orbitCamera.setAspect(width / height);
  }
  window.addEventListener("resize", resize);
  resize();

  const registry = await fetchContentRegistry();
  const opts = resolveRunSetupOptions(new URLSearchParams(window.location.search), registry);
  const setup = buildRunSetup(registry, opts);

  let localTick = 0;
  const simHost = new SimHost(setup);
  let prevSnapshot: Snapshot | null = null;
  let nextSnapshot: Snapshot | null = null;
  let lastSnapshotAt = performance.now();
  let rawById = new Map<number, ActorSnapshot>();
  let prevHealthById = new Map<number, number>();

  function placementForPing(event: PingEvent): PingWorldPlacement | null {
    if (event.targetId !== undefined) {
      const pos = actorPool.worldPositionOf(event.targetId);
      return pos ? { x: pos.x, y: pos.y, z: pos.z } : null;
    }
    // No ground raycast implemented here -- a spot ping lands at the pinger's own position.
    const pos = actorPool.worldPositionOf(event.actorId);
    return pos ? { x: pos.x, y: 0, z: pos.z } : null;
  }

  const stop = simHost.start(
    (snapshot) => {
      prevSnapshot = nextSnapshot ?? snapshot;
      nextSnapshot = snapshot;
      lastSnapshotAt = performance.now();
      rawById = new Map(snapshot.actors.map((a) => [a.id, a]));

      for (const actor of snapshot.actors) {
        const prevHealth = prevHealthById.get(actor.id);
        if (prevHealth !== undefined && actor.health < prevHealth) {
          const pos = actorPool.worldPositionOf(actor.id);
          if (pos) damageNumbers.spawn(pos.x, pos.y + 1.9, pos.z, prevHealth - actor.health);
        }
        prevHealthById.set(actor.id, actor.health);
      }

      hud.updateTimer(snapshot.runTimerTicks, snapshot.timerLimitTicks);
      hud.updateSlack(snapshot.slackTicks ?? 0, snapshot.maxSlackTicks ?? 0, snapshot.depleted ?? false);
      const player = snapshot.actors.find((a) => a.id === setup.humanActorId);
      if (player) hud.updateAbilityBar(player.cooldowns);
      if (snapshot.runEnd) runEndView.show(snapshot.runEnd);
    },
    () => {
      localTick += 1;
      const commands: AnyCommand[] = [];

      const move = computeMoveDeltaMm(moveKeys, orbitCamera.yaw, MOVE_SPEED_MM_PER_TICK);
      if (move.dx !== 0 || move.dz !== 0) {
        commands.push({ type: "move", dx: move.dx, dz: move.dz, tick: localTick });
      }

      const justPressed = keyState.sampleAndClearJustPressed();
      commands.push(...deriveAbilityCommands(justPressed, keybinds, localTick));
      for (const control of deriveHostControlCommands(justPressed, localTick)) simHost.applyCommand(control);

      const player = rawById.get(setup.humanActorId);
      const groundPoint: PingGroundPoint = player ? { x: player.pos.x, z: player.pos.z } : { x: 0, z: 0 };
      const ping = derivePingEvent(justPressed, keybinds.ping, localTick, setup.humanActorId, player?.targetId ?? null, groundPoint, pingLimiter);
      if (ping) {
        pingEvents.push(ping);
        simHost.applyCommand({ type: "ping", event: ping });
      }

      return commands;
    },
  );
  window.addEventListener("beforeunload", stop);

  function frame(): void {
    requestAnimationFrame(frame);

    if (prevSnapshot && nextSnapshot) {
      const alpha = (performance.now() - lastSnapshotAt) / SIM_TICK_MS;
      const interpolated = interpolateSnapshots(prevSnapshot, nextSnapshot, alpha);
      actorPool.sync(interpolated, rawById, nextSnapshot.tick);
      telegraphPool.sync(nextSnapshot.telegraphs, nextSnapshot.tick);
      pingPool.sync(pingEvents, nextSnapshot.tick, placementForPing, (e) => pingLabelText(e));

      const player = interpolated.find((a) => a.id === setup.humanActorId);
      if (player) orbitCamera.update(player.pos.x / 1000, player.pos.y / 1000, player.pos.z / 1000);

      const targetId = player ? rawById.get(setup.humanActorId)?.targetId ?? null : null;
      reticle.update(targetId !== null ? actorPool.worldPositionOf(targetId) : null);
    }

    damageNumbers.update(1 / 60);
    renderer.render(scene, orbitCamera.camera);
  }
  requestAnimationFrame(frame);
}

boot().catch((err) => {
  console.error("keyrun client failed to boot", err);
  const app = document.getElementById("app");
  if (app) app.textContent = `Failed to start: ${String(err)}`;
});
