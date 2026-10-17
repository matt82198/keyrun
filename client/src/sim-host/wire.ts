/**
 * client/src/sim-host/wire.ts
 *
 * postMessage protocol between the main thread (sim-host-client.ts) and the real Worker
 * (worker.ts) -- research/11-tick-rate-measurement.md (D9)'s follow-up recommendation: move the
 * fixed-timestep sim loop off the main-thread-timer scaffold onto an actual Worker thread.
 * Deliberately small, JSON-shaped messages (no functions, no class instances) -- the same shapes a
 * later server/ lane can reuse close to verbatim over a real network transport (client -> server
 * commands, server -> client snapshots), per this lane's dispatch brief.
 *
 * Usage: main thread posts `InitMessage` once then any number of `CommandMessage`s; the worker
 * posts one `ReadyMessage` after boot, then a `SnapshotMessage` per simulated tick (or an
 * `ErrorMessage` if init failed).
 */
import type { AnyCommand } from "../input/commands.js";
import type { PingEvent } from "../input/ping.js";
import type { HostControlCommand } from "./host-commands.js";
import type { Snapshot } from "./snapshot.js";
import type { RawLoadout } from "./loadout-storage.js";
import type { SlotAbilities } from "../input/slot-abilities.js";
import type { DisplayInfo } from "./display-info.js";

/** Ping is not a sim Command (see input/ping.ts's file docstring) -- it only reaches
 * bots/src/bot.ts's pingReactionCommands via bot-commands.ts's `pings` list. */
export type PingHostCommand = { type: "ping"; event: PingEvent };
export type HostCommand = AnyCommand | HostControlCommand | PingHostCommand;

/** `search`: the page's raw `location.search` (e.g. "?key=5&dungeon=hollow-vault&class=warden"),
 * re-parsed worker-side so content loading + option resolution (content-registry.ts's fetch,
 * boot-params.ts's resolveRunSetupOptions) happens exactly once, in the worker, instead of being
 * duplicated on both sides of the postMessage boundary. `tickMs`: research/11 (D9)'s `?hz=`
 * override, resolved once on the main thread (boot-params.ts's parseHz) and forwarded so both
 * sides schedule off the same number rather than re-deriving it independently. `loadout`
 * (run-options-wiring): the screen's persisted build, read from localStorage on the main thread
 * (loadout-storage.ts's `readPersistedLoadout` -- a Worker has no `localStorage` of its own) since
 * that is a main-thread-only API; the worker resolves it into real abilities once it has the
 * content registry (run-setup.ts's `buildRunSetup`). Omitted (or `null`) for "no persisted
 * loadout" -- the run then uses every class's base kit, unchanged from before this addition. */
export interface InitMessage {
  type: "init";
  search: string;
  tickMs?: number;
  loadout?: RawLoadout | null;
}
export interface CommandMessage {
  type: "command";
  command: HostCommand;
}
export type MainToWorkerMessage = InitMessage | CommandMessage;

/** `slotAbilities`: run-setup.ts's one resolution of the human's keybind slot -> ability id map
 * (input/slot-abilities.ts), so the main thread's cast path and HUD read the worker's answer. */
export interface ReadyMessage {
  type: "ready";
  humanActorId: number;
  tickMs: number;
  slotAbilities: SlotAbilities;
  /** D10: display-info.ts's names/timings for the ability bar and actor name tags. */
  display: DisplayInfo;
}
/** `simStepMs`/`elapsedMs`: worker.ts's own measurement of its step cost and scheduler jitter --
 * metrics.ts's `window.__keyrunMetrics` singleton only exists on the main thread (a Worker has its
 * own separate globals, so a metrics instance bundled into worker.js would be an orphan no one
 * ever reads); sim-host-client.ts relays these numbers into the main thread's real metrics.ts
 * instance unchanged, via the same `recordSimStep`/`recordTickInterval` calls worker.ts used to
 * make directly back when it ran on the main thread. */
export interface SnapshotMessage {
  type: "snapshot";
  snapshot: Snapshot;
  simStepMs: number;
  elapsedMs: number;
  tickMs: number;
}
export interface ErrorMessage {
  type: "error";
  message: string;
}
export type WorkerToMainMessage = ReadyMessage | SnapshotMessage | ErrorMessage;
