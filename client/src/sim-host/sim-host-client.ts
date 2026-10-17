/**
 * client/src/sim-host/sim-host-client.ts
 *
 * Main-thread side of the real-Worker split (research/11 D9 follow-up; see worker.ts's file
 * docstring). Keeps the pre-Worker SimHost's public shape (`start(onSnapshot, pullCommands)`,
 * `applyCommand`, `.tickMs`) so boot.ts/hud-wiring.ts barely differ from old main.ts, but every sim
 * step now happens on a real Worker thread, reached only through wire.ts's postMessage shapes.
 *
 * Content loading (the registry fetch, dungeon/class/key-level option resolution) moved entirely
 * into the worker -- see wire.ts's InitMessage docstring -- so this class never touches
 * content-registry.ts or run-setup.ts itself; it only learns `humanActorId` once the worker's
 * `ReadyMessage` arrives (`whenReady()`), since content-driven values are no longer available
 * synchronously on this side the way `buildRunSetup`'s return value used to be.
 *
 * Input sampling stays tick-cadenced on this side (client/CLAUDE.md: "at most one movement command
 * ... per sim tick"): this class runs its own scheduler.ts-driven accumulator, calling
 * `pullCommands()` once per due tick and forwarding each resulting command to the worker
 * immediately. There is no synchronous round-trip across a postMessage boundary -- multiple
 * commands posted between two worker ticks simply queue in SimRuntime's own `pending` buffers
 * (sim-runtime.ts), the same absorption the pre-Worker scaffold already relied on.
 *
 * Usage: `const client = new SimHostClient(location.search, tickMsOverride, loadout);
 * const { humanActorId } = await client.whenReady();
 * const stop = client.start(onSnapshot, pullCommands);`
 */
import type { AnyCommand } from "../input/commands.js";
import type { Snapshot } from "./snapshot.js";
import { SIM_TICK_MS } from "./snapshot.js";
import { computeTicksToRun } from "./scheduler.js";
import { metrics } from "./metrics.js";
import type { RawLoadout } from "./loadout-storage.js";
import type { HostCommand, MainToWorkerMessage, WorkerToMainMessage } from "./wire.js";
import type { SlotAbilities } from "../input/slot-abilities.js";

export interface SimHostReady {
  humanActorId: number;
  /** The host's keybind slot -> ability id map (input/slot-abilities.ts). The local worker always
   * reports it; net/net-client.ts does not yet (the server's `joined` carries no slot map), and
   * without it ability keys cast nothing (fail closed) -- tracked follow-up, not a fallback. */
  slotAbilities?: SlotAbilities;
}

export class SimHostClient {
  private readonly worker: Worker;
  private readonly readyPromise: Promise<SimHostReady>;
  private readyResolve!: (r: SimHostReady) => void;
  private onSnapshot: ((snapshot: Snapshot) => void) | null = null;
  private inputIntervalHandle: ReturnType<typeof setInterval> | null = null;
  private accumulatedMs = 0;
  private lastNowMs = Date.now();
  readonly tickMs: number;

  constructor(search: string, tickMsOverride?: number, loadout?: RawLoadout | null) {
    this.tickMs = tickMsOverride ?? SIM_TICK_MS;
    metrics.hz = Math.round(1000 / this.tickMs);
    this.readyPromise = new Promise((resolve) => {
      this.readyResolve = resolve;
    });
    // See worker.ts's file docstring: the path is written relative to where THIS module ends up
    // bundled (dist/client/src/main.js, since sim-host-client.ts is inlined into the main.ts
    // entry, never its own esbuild entry point) -- "./sim-host/worker.js" matches source layout
    // (client/src/main.ts -> client/src/sim-host/worker.ts) because scripts/build.mjs's outbase
    // "." mirrors that layout 1:1 under dist/.
    this.worker = new Worker(new URL("./sim-host/worker.js", import.meta.url), { type: "module" });
    this.worker.onmessage = (ev: MessageEvent<WorkerToMainMessage>) => this.onWorkerMessage(ev.data);
    // exactOptionalPropertyTypes: only include `tickMs`/`loadout` at all when actually given
    // (run-options-wiring: `loadout` is the screen's persisted build -- see wire.ts's docstring).
    this.post({
      type: "init",
      search,
      ...(tickMsOverride === undefined ? {} : { tickMs: tickMsOverride }),
      ...(loadout ? { loadout } : {}),
    });
  }

  /** Resolves once the worker has booted (content fetched, run created) and reports the one piece
   * of content-driven state the main thread still needs synchronously: which actor id it plays. */
  whenReady(): Promise<SimHostReady> {
    return this.readyPromise;
  }

  applyCommand(cmd: HostCommand): void {
    this.post({ type: "command", command: cmd });
  }

  /** Starts the tick-cadenced input scheduler; `onSnapshot` fires for every snapshot relayed back
   * from the worker. Returns a stop function that also terminates the worker. */
  start(onSnapshot: (snapshot: Snapshot) => void, pullCommands?: () => AnyCommand[]): () => void {
    this.onSnapshot = onSnapshot;
    this.lastNowMs = Date.now();
    this.inputIntervalHandle = setInterval(() => {
      const now = Date.now();
      const elapsed = now - this.lastNowMs;
      this.lastNowMs = now;
      const plan = computeTicksToRun(this.accumulatedMs, elapsed, this.tickMs);
      this.accumulatedMs = plan.nextAccumulatedMs;
      for (let i = 0; i < plan.ticks; i++) {
        if (pullCommands) for (const cmd of pullCommands()) this.applyCommand(cmd);
      }
    }, this.tickMs);
    return () => this.stop();
  }

  private onWorkerMessage(msg: WorkerToMainMessage): void {
    if (msg.type === "ready") {
      this.readyResolve({ humanActorId: msg.humanActorId, slotAbilities: msg.slotAbilities });
    } else if (msg.type === "snapshot") {
      metrics.recordSimStep(msg.simStepMs);
      metrics.recordTickInterval(msg.elapsedMs, msg.tickMs);
      metrics.recordSnapshotBytes(JSON.stringify(msg.snapshot).length);
      this.onSnapshot?.(msg.snapshot);
    } else {
      console.error("keyrun sim worker error:", msg.message);
    }
  }

  private post(msg: MainToWorkerMessage): void {
    this.worker.postMessage(msg);
  }

  private stop(): void {
    if (this.inputIntervalHandle !== null) clearInterval(this.inputIntervalHandle);
    this.inputIntervalHandle = null;
    this.worker.terminate();
  }
}
