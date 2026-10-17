/**
 * client/src/hud-wiring.ts
 *
 * The running game loop: split out of main.ts (research/11 D9's real-Worker migration pushed
 * main.ts past the repo's 190-LOC-per-file gate). boot.ts constructs every DOM/scene/pool/hud
 * object and the host (local Worker or net/net-client.ts's NetHostClient -- both satisfy
 * net/host-handle.ts's `HostHandle`), then hands them here once; this file owns the snapshot
 * stream (interpolation state), the per-tick input-sampling closure, and the requestAnimationFrame
 * render loop -- state shared across ticks and frames, so it all stays in one module.
 *
 * Usage: `runGameLoop(deps)` (called once, from boot.ts, after `simHost.whenReady()` resolves).
 */
import type * as THREE from "three";
import { computeMoveDeltaMm, type MoveKeyState } from "./camera/movement.js";
import type { OrbitCamera } from "./camera/orbitCamera.js";
import type { ActorPool } from "./render/actors.js";
import type { TelegraphPool } from "./render/telegraphs.js";
import type { DamageNumberField } from "./render/damageNumbers.js";
import type { TargetReticle } from "./render/reticle.js";
import type { Hud } from "./render/hud.js";
import { PingMarkerPool, pingLabelText, type PingWorldPlacement } from "./render/pings.js";
import type { KeyState } from "./input/keyState.js";
import { deriveAbilityCommands, type AnyCommand } from "./input/commands.js";
import { derivePingEvent, type PingEvent, type PingGroundPoint, type PingRateLimiter } from "./input/ping.js";
import type { Keybinds } from "./input/keybinds.js";
import type { SlotAbilities } from "./input/slot-abilities.js";
import { interpolateSnapshots } from "./sim-host/interpolate.js";
import type { ActorSnapshot, Snapshot } from "./sim-host/snapshot.js";
import { metrics } from "./sim-host/metrics.js";
import { deriveHostControlCommands } from "./sim-host/host-commands.js";
import { RunEndView } from "./hud/run-end.js";
import type { ConnectionState, HostHandle, NetNotice } from "./net/host-handle.js";

const MOVE_SPEED_MM_PER_TICK = 180; // ~3.6 m/s at 20Hz
const MOVE_CODES: Record<string, keyof MoveKeyState> = { KeyW: "forward", KeyS: "back", KeyA: "left", KeyD: "right" };

// Only NetHostClient (net/net-client.ts) reports connection state; render/hud.ts is off-limits.
const CONNECTION_STATE_LABEL: Record<ConnectionState, string> = {
  connecting: "connecting…",
  connected: "connected",
  reconnecting: "reconnecting…",
  error: "connection error",
  run_end: "run ended",
};

export interface GameLoopDeps {
  simHost: HostHandle;
  humanActorId: number;
  /** From the host's whenReady() (input/slot-abilities.ts): read by both the cast path and the HUD. */
  slotAbilities: SlotAbilities | undefined;
  keyState: KeyState;
  moveKeys: MoveKeyState;
  pingLimiter: PingRateLimiter;
  keybinds: Keybinds;
  orbitCamera: OrbitCamera;
  actorPool: ActorPool;
  telegraphPool: TelegraphPool;
  damageNumbers: DamageNumberField;
  reticle: TargetReticle;
  pingPool: PingMarkerPool;
  hud: Hud;
  runEndView: RunEndView;
  renderer: THREE.WebGLRenderer;
  scene: THREE.Scene;
  connectionStateEl?: HTMLDivElement; // shown/updated only for a host implementing onConnectionState
}

export { MOVE_CODES };

// Wires the net host's connection-state line + notices into `el`; a no-op for the local host.
function wireConnectionState(simHost: HostHandle, el: HTMLDivElement | undefined): void {
  if (!el || !simHost.onConnectionState) return;
  el.hidden = false;
  simHost.onConnectionState((state, detail) => {
    el.textContent = detail ? `${CONNECTION_STATE_LABEL[state]} -- ${detail}` : CONNECTION_STATE_LABEL[state];
    el.dataset.state = state;
  });
  simHost.onNotice?.((notice: NetNotice) => {
    el.title = notice.kind === "late" ? `late cmd tick ${notice.originalTick}->${notice.newTick}` : `backfilled ticks ${notice.fromTick}->${notice.toTick}`;
  });
}

export function runGameLoop(deps: GameLoopDeps): void {
  const { simHost, humanActorId, actorPool, telegraphPool, damageNumbers, reticle, pingPool, hud, runEndView, orbitCamera, renderer, scene, connectionStateEl } = deps;
  wireConnectionState(simHost, connectionStateEl);
  let localTick = 0;
  let prevSnapshot: Snapshot | null = null;
  let nextSnapshot: Snapshot | null = null;
  let lastSnapshotAt = performance.now();
  let rawById = new Map<number, ActorSnapshot>();
  let prevHealthById = new Map<number, number>();
  const pingEvents: PingEvent[] = [];

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
      const player = snapshot.actors.find((a) => a.id === humanActorId);
      if (player) hud.updateAbilityBar(player.cooldowns, deps.slotAbilities);
      if (snapshot.runEnd) runEndView.show(snapshot.runEnd);
    },
    () => {
      localTick += 1;
      const commands: AnyCommand[] = [];

      const move = computeMoveDeltaMm(deps.moveKeys, orbitCamera.yaw, MOVE_SPEED_MM_PER_TICK);
      if (move.dx !== 0 || move.dz !== 0) commands.push({ type: "move", dx: move.dx, dz: move.dz, tick: localTick });

      const justPressed = deps.keyState.sampleAndClearJustPressed();
      metrics.noteInputSample([...justPressed], performance.now()); // research/11 (D9)
      commands.push(...deriveAbilityCommands(justPressed, deps.keybinds, deps.slotAbilities, localTick));
      for (const control of deriveHostControlCommands(justPressed, localTick)) simHost.applyCommand(control);

      const player = rawById.get(humanActorId);
      const groundPoint: PingGroundPoint = player ? { x: player.pos.x, z: player.pos.z } : { x: 0, z: 0 };
      const ping = derivePingEvent(justPressed, deps.keybinds.ping, localTick, humanActorId, player?.targetId ?? null, groundPoint, deps.pingLimiter);
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
      const alpha = (performance.now() - lastSnapshotAt) / simHost.tickMs;
      metrics.recordRenderAlpha(alpha, simHost.tickMs); // research/11 (D9): render staleness
      const interpolated = interpolateSnapshots(prevSnapshot, nextSnapshot, alpha);
      actorPool.sync(interpolated, rawById, nextSnapshot.tick);
      telegraphPool.sync(nextSnapshot.telegraphs, nextSnapshot.tick);
      pingPool.sync(pingEvents, nextSnapshot.tick, placementForPing, (e) => pingLabelText(e));

      const player = interpolated.find((a) => a.id === humanActorId);
      if (player) orbitCamera.update(player.pos.x / 1000, player.pos.y / 1000, player.pos.z / 1000);

      const targetId = player ? rawById.get(humanActorId)?.targetId ?? null : null;
      reticle.update(targetId !== null ? actorPool.worldPositionOf(targetId) : null);
    }

    damageNumbers.update(1 / 60);
    renderer.render(scene, orbitCamera.camera);
  }
  requestAnimationFrame(frame);
}
