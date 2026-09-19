/**
 * client/src/boot.ts
 *
 * Boot: wires input, camera, sim-host, and the render layer together. Game-rule-free: this only
 * constructs everything and turns input into commands; hud-wiring.ts's runGameLoop owns the actual
 * per-tick/per-frame loop (split out of what used to be main.ts's own boot() -- research/11 D9's
 * real-Worker migration added sim-host-client.ts's async whenReady() handshake, which pushed the
 * single-file version of this past the repo's 190-LOC-per-file gate).
 *
 * URL params: ?key=<2-20>&dungeon=hollow-vault&class=<warden|cinder|chanter> pick the run's Tempo
 * level, dungeon, and which of the five fixed party slots the human plays -- resolved inside the
 * Worker now (see sim-host/worker.ts, sim-host/boot-params.ts), not here; this file only reads
 * `?hz=` (research/11 D9's measurement-only real-time pacing override) to size the Worker's tick
 * interval and to show the "?hz= override active" warning below.
 *
 * Host selection: `?net=1` or `?server=<ws url>` picks net/net-client.ts's NetHostClient (speaks
 * server/src/protocol.ts over WebSocket); anything else keeps the default local Worker host
 * (client/CLAUDE.md: "the same client later swaps the local sim for a server connection without
 * changing the render code"). Both hosts satisfy net/host-handle.ts's `HostHandle`, so nothing
 * below this needs to know which one it got.
 */
import { loadKeybinds, DEFAULT_KEYBINDS, type Keybinds } from "./input/keybinds.js";
import { KeyState } from "./input/keyState.js";
import { PingRateLimiter } from "./input/ping.js";
import type { MoveKeyState } from "./camera/movement.js";
import { OrbitCamera } from "./camera/orbitCamera.js";
import { createScene } from "./render/scene.js";
import { ActorPool } from "./render/actors.js";
import { TelegraphPool } from "./render/telegraphs.js";
import { DamageNumberField } from "./render/damageNumbers.js";
import { TargetReticle } from "./render/reticle.js";
import { Hud } from "./render/hud.js";
import { PingMarkerPool } from "./render/pings.js";
import { SimHostClient } from "./sim-host/sim-host-client.js";
import { metrics, attachMetricsHud } from "./sim-host/metrics.js"; // research/11 (D9): ?metrics=1 HUD + window.__keyrunMetrics
import { parseHz } from "./sim-host/boot-params.js";
import { RunEndView } from "./hud/run-end.js";
import { NetHostClient } from "./net/net-client.js";
import type { HostHandle } from "./net/host-handle.js";
import { runGameLoop, MOVE_CODES } from "./hud-wiring.js";

/** `?net=1` or a `?server=` override selects the net host; anything else keeps the local one. */
function wantsNetHost(searchParams: URLSearchParams): boolean {
  return searchParams.get("net") === "1" || searchParams.has("server");
}

/** research/11 (D9): `?hz=` is a dev-only measurement knob, off by default (parseHz returns
 * undefined for an absent/invalid param, leaving SimHostClient on its sim-derived default tick
 * rate). When set, it changes real-time pacing only -- not game content -- so a visible warning
 * keeps anyone testing with it from mistaking it for a supported gameplay setting. Mounted the same
 * independent-floating-element way metrics.ts's attachMetricsHud() is, so it never touches
 * render/hud.ts's ability-bar/timer HUD. */
function warnIfHzOverridden(hz: number | undefined): void {
  if (hz === undefined) return;
  const el = document.createElement("div");
  el.textContent = `?hz=${hz} override active -- real-time pacing only, not a supported setting (research/11)`;
  el.setAttribute(
    "style",
    "position:absolute;top:8px;left:8px;padding:4px 8px;background:rgba(120,20,20,0.85);" +
      "color:#fff0f0;font:11px/1.3 monospace;border-radius:4px;pointer-events:none;z-index:9999;",
  );
  document.body.appendChild(el);
}

export async function boot(): Promise<void> {
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
  const connectionStateEl = document.createElement("div");
  connectionStateEl.className = "net-connection-state";
  connectionStateEl.hidden = true;
  connectionStateEl.setAttribute(
    "style",
    "position:absolute;top:8px;right:8px;padding:4px 8px;background:rgba(12,16,22,0.8);" +
      "color:#eef2f6;font:11px/1.3 monospace;border-radius:4px;pointer-events:none;z-index:9999;",
  );
  app.appendChild(connectionStateEl);

  const keyState = new KeyState();
  const moveKeys: MoveKeyState = { forward: false, back: false, left: false, right: false };
  const pingLimiter = new PingRateLimiter();
  window.addEventListener("keydown", (ev) => {
    if (ev.repeat) return;
    keyState.onKeyDown(ev.code);
    metrics.noteKeyDown(ev.code, performance.now()); // research/11 (D9): input->visible-tick latency
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

  const searchParams = new URLSearchParams(window.location.search);
  const hz = parseHz(searchParams.get("hz")); // research/11 (D9); undefined => sim-derived default
  warnIfHzOverridden(hz);
  const tickMsOverride = hz ? 1000 / hz : undefined;
  const simHost: HostHandle = wantsNetHost(searchParams)
    ? new NetHostClient(window.location.search, tickMsOverride)
    : new SimHostClient(window.location.search, tickMsOverride);
  const { humanActorId } = await simHost.whenReady();
  attachMetricsHud();

  runGameLoop({
    simHost,
    humanActorId,
    keyState,
    moveKeys,
    pingLimiter,
    keybinds,
    orbitCamera,
    actorPool,
    telegraphPool,
    damageNumbers,
    reticle,
    connectionStateEl,
    pingPool,
    hud,
    runEndView,
    renderer,
    scene,
  });
}
