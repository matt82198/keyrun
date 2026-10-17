/**
 * client/src/sim-host/worker.ts
 *
 * The REAL Worker entry point (bundled as its own esbuild output -- scripts/build.mjs; loaded via
 * `new Worker(new URL("./sim-host/worker.js", import.meta.url), {type:"module"})` from
 * sim-host-client.ts). Owns the fixed-timestep sim loop.
 *
 * research/11-tick-rate-measurement.md (D9) found the PREVIOUS scaffold -- SimHost running on the
 * main thread via `setInterval`, not a real Worker -- a likely cause of the render-staleness
 * regression measured at 30Hz (main-thread contention between rendering, input handling and
 * ticking made a tighter interval harder to hit exactly). That brief's own recommendation was to
 * remove main-thread contention as a jitter source by moving onto a real Worker; this file is that
 * follow-up. See "Follow-up: real Worker" in that doc for the re-measured before/after numbers.
 *
 * Talks to the main thread only through wire.ts's postMessage shapes -- the same shapes a later
 * server/ lane can reuse close to verbatim over a real network transport. No DOM anywhere in this
 * file: content-registry.ts's fetchContentRegistry() uses `fetch`, which Workers support natively;
 * run-setup.ts's buildRunSetup and sim-runtime.ts's SimRuntime are both already DOM-free (that was
 * already true before this lane -- see sim-runtime.ts's own docstring).
 *
 * Tested headlessly by faking `self` (tests/client/sim-host/worker-entry.test.ts drives 200 ticks
 * end to end through this exact file) -- see that file's docstring for why content-registry.ts's
 * fetch call is mocked to the real Node content loader there instead of hitting the network.
 */
import type { ContentRegistry } from "../../../content/schema/registry.js";
import { fetchContentRegistry } from "./content-registry.js";
import { resolveRunSetupOptions } from "./boot-params.js";
import { buildRunSetup } from "./run-setup.js";
import { SimRuntime } from "./sim-runtime.js";
import { buildDisplayInfo } from "./display-info.js";
import { computeTicksToRun } from "./scheduler.js";
import { SIM_TICK_MS } from "./snapshot.js";
import type { InitMessage, MainToWorkerMessage, WorkerToMainMessage } from "./wire.js";

/** Minimal surface this file needs from the real `DedicatedWorkerGlobalScope` -- typed by hand
 * (rather than adding the "webworker" lib to tsconfig.json, which is out of this lane's ownership
 * and conflicts with the existing "DOM" lib's own incompatible global `self`/`postMessage`
 * declarations) and reached via a cast, same as any other worker-entry file that must coexist in a
 * DOM-lib TypeScript program. */
interface WorkerScope {
  postMessage(message: WorkerToMainMessage): void;
  addEventListener(type: "message", listener: (ev: MessageEvent<MainToWorkerMessage>) => void): void;
}
const ctx = self as unknown as WorkerScope;

let runtime: SimRuntime | null = null;
let tickMs = SIM_TICK_MS;
let accumulatedMs = 0;
let lastNowMs = 0;
let intervalHandle: ReturnType<typeof setInterval> | null = null;

function stop(): void {
  if (intervalHandle !== null) clearInterval(intervalHandle);
  intervalHandle = null;
}

/** Fixed-timestep loop with capped catch-up (scheduler.ts, unchanged from the pre-Worker
 * scaffold): decides how many whole ticks to run this callback so a throttled/backgrounded worker
 * resumes cleanly instead of drifting or bursting an unbounded backlog. */
function tickLoop(): void {
  if (!runtime) return;
  const now = Date.now();
  const elapsed = now - lastNowMs;
  lastNowMs = now;
  const plan = computeTicksToRun(accumulatedMs, elapsed, tickMs);
  accumulatedMs = plan.nextAccumulatedMs;
  for (let i = 0; i < plan.ticks; i++) {
    const startedAtMs = performance.now();
    const snapshot = runtime.step();
    const simStepMs = performance.now() - startedAtMs;
    ctx.postMessage({ type: "snapshot", snapshot, simStepMs, elapsedMs: elapsed, tickMs });
    if (snapshot.runEnd) {
      stop();
      return;
    }
  }
}

async function handleInit(msg: InitMessage): Promise<void> {
  tickMs = msg.tickMs ?? SIM_TICK_MS;
  const registry: ContentRegistry = await fetchContentRegistry();
  const opts = resolveRunSetupOptions(new URLSearchParams(msg.search), registry);
  // run-options-wiring: msg.loadout is the screen's persisted build, read main-thread-side
  // (loadout-storage.ts) since a Worker has no localStorage of its own -- see wire.ts's docstring.
  // A bad loadout (applyLoadout's own invariant checks) throws here, caught by this function's
  // caller below and surfaced as an ErrorMessage, never a silent base-kit fallback.
  const setup = buildRunSetup(registry, opts, msg.loadout);
  runtime = new SimRuntime(setup);
  lastNowMs = Date.now();
  const display = buildDisplayInfo(registry, setup.content.abilities); // D10: names for the bar + tags
  ctx.postMessage({ type: "ready", humanActorId: setup.humanActorId, tickMs, slotAbilities: setup.slotAbilities, display });
  intervalHandle = setInterval(tickLoop, tickMs);
}

ctx.addEventListener("message", (ev) => {
  const msg = ev.data;
  if (msg.type === "init") {
    handleInit(msg).catch((err: unknown) => ctx.postMessage({ type: "error", message: String(err) }));
  } else if (msg.type === "command" && runtime) {
    runtime.applyCommand(msg.command);
  }
});
