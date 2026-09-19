// Real sim host: runs sim/index.ts's createRun/stepRun against a real content/data registry
// (run-setup.ts) -- the human plus four bots/src bots in Hollow Vault -- and maps the resulting
// SimState + encounter telegraphs into Snapshot (snapshot-codec.ts). Targeting fixups, Slack
// spending and run-end detection live in run-policy.ts / hud/run-end.ts; command derivation lives
// in player-commands.ts / bot-commands.ts. This file is the orchestrator only, kept under the
// repo's 190-LOC-per-file gate (tests/repo/loc.test.ts) by that split.
//
// DEVIATION (inherited from the pre-existing scaffold this replaces): runs on the main thread via
// a timer rather than inside an actual Worker thread. scripts/build.mjs (off-limits to this lane)
// only bundles fixed entry points with no worker-splitting support, so a literal `new Worker(...)`
// would have no buildable script to load. `start()` drives a fixed-timestep accumulator with
// capped catch-up (scheduler.ts) instead of a bare `setInterval` tick-for-tick, so a throttled/
// backgrounded tab resumes cleanly instead of drifting or bursting an unbounded backlog.
//
// DEVIATION: no respawn/rez mechanic exists anywhere in sim -- a dead player actor simply stays
// dead. A full party wipe would otherwise soft-lock the run forever (nothing left to act), so
// run-policy.ts's signals feed hud/run-end.ts's auto-abandon-after-a-grace-window policy.
import type { AnyCommand } from "../input/commands.js";
import type { PingEvent } from "../input/ping.js";
import type { HostControlCommand } from "./host-commands.js";
import type { Snapshot } from "./snapshot.js";
import { SIM_TICK_MS } from "./snapshot.js";
import type { RunEndState } from "../hud/run-end.js";
import { initialRunEndState, stepRunEnd } from "../hud/run-end.js";

import { createRun, stepRun, type Run } from "../../../sim/index.js";
import type { ActorId } from "../../../sim/core/state.js";
import type { Command } from "../../../sim/core/commands.js";
import { initialSlackTicks } from "../../../sim/encounter/timer.js";
import { makeBot, defaultMemory, type Bot, type BotMemory } from "../../../bots/src/bot.js";
import { seedPrng, type PrngState } from "../../../bots/src/skill.js";
import { PING_REACTION_WINDOW_TICKS } from "../../../bots/src/shared/pings.js";
import type { RunSetup } from "./run-setup.js";
import { computeTicksToRun } from "./scheduler.js";
import { buildSnapshot } from "./snapshot-codec.js";
import { applyDeathsAndWipe, computeRunEndSignals, fixupTargeting } from "./run-policy.js";
import { computePlayerCommands, type PendingInput } from "./player-commands.js";
import { computeBotCommands } from "./bot-commands.js";
import { metrics } from "./metrics.js";

const SEED = 1;

/** Ping is not a sim Command (see client/src/input/ping.ts's own file docstring) -- it only
 * reaches bots/src/bot.ts's pingReactionCommands via computeBotCommands's `pings` list. */
type PingHostCommand = { type: "ping"; event: PingEvent };
type HostCommand = AnyCommand | HostControlCommand | PingHostCommand;

export class SimHost {
  private run: Run;
  private readonly bots: Record<ActorId, Bot> = {};
  private memories: Record<ActorId, BotMemory> = {};
  private prng: PrngState;
  private pending: PendingInput = { moves: [], casts: [], targetCycles: 0 };
  private pendingStopcast = false;
  private abandonRequested = false;
  private recentPings: PingEvent[] = [];
  private intervalHandle: ReturnType<typeof setInterval> | null = null;
  private accumulatedMs = 0;
  private lastNowMs = Date.now();
  private runEndState: RunEndState = initialRunEndState();
  private readonly maxSlackTicks: number;
  /** research/11-tick-rate-measurement.md (D9): real ms per simulated tick. Defaults to the
   * client's SIM_TICK_MS; a caller (main.ts's `?hz=` param) may override it so the measurement
   * lane can drive the same playable at a different real-time tick rate without editing sim/ --
   * see metrics.ts's file docstring for why this is the tick-rate knob, not sim/core/tick.ts's
   * TICK_HZ (that constant is never imported outside sim/; it counts ticks, not wall-clock ms). */
  readonly tickMs: number;

  constructor(
    private readonly setup: RunSetup,
    tickMsOverride?: number,
  ) {
    this.tickMs = tickMsOverride ?? SIM_TICK_MS;
    metrics.hz = Math.round(1000 / this.tickMs);
    this.run = createRun(SEED, setup.party, setup.dungeon, setup.content);
    fixupTargeting(this.run, this.setup);

    for (const member of setup.party) {
      if (member.id === setup.humanActorId) continue;
      this.bots[member.id] = makeBot();
      this.memories[member.id] = defaultMemory(80);
    }
    this.prng = seedPrng(SEED);
    this.maxSlackTicks = initialSlackTicks(setup.keyLevel);
  }

  /** Queues a command produced by the input layer; applied on the next tick. */
  applyCommand(cmd: HostCommand): void {
    if (cmd.type === "move") this.pending.moves.push({ dx: cmd.dx, dz: cmd.dz });
    else if (cmd.type === "cast") this.pending.casts.push(cmd.abilityId);
    else if (cmd.type === "targetCycle") this.pending.targetCycles += 1;
    else if (cmd.type === "stopcast") this.pendingStopcast = true;
    else if (cmd.type === "abandon") this.abandonRequested = true;
    else if (cmd.type === "ping") this.recentPings.push(cmd.event);
  }

  /** Fixed-timestep loop with capped catch-up (scheduler.ts); `pullCommands`, when given, is
   * called once per simulated tick so input sampling stays tick-synced. Stops itself once the run
   * ends (hud/run-end.ts's terminal state). */
  start(onSnapshot: (snapshot: Snapshot) => void, pullCommands?: () => AnyCommand[]): () => void {
    this.lastNowMs = Date.now();
    this.intervalHandle = setInterval(() => {
      const now = Date.now();
      const elapsed = now - this.lastNowMs;
      metrics.recordTickInterval(elapsed, this.tickMs);
      this.lastNowMs = now;
      const plan = computeTicksToRun(this.accumulatedMs, elapsed, this.tickMs);
      this.accumulatedMs = plan.nextAccumulatedMs;
      for (let i = 0; i < plan.ticks; i++) {
        if (pullCommands) for (const cmd of pullCommands()) this.applyCommand(cmd);
        const snapshot = this.step();
        metrics.recordSnapshotBytes(JSON.stringify(snapshot).length);
        onSnapshot(snapshot);
        if (this.runEndState.info) {
          this.stop();
          return;
        }
      }
    }, this.tickMs);
    return () => this.stop();
  }

  private stop(): void {
    if (this.intervalHandle !== null) clearInterval(this.intervalHandle);
    this.intervalHandle = null;
  }

  /** Advances the run by exactly one tick and returns the resulting Snapshot. Public so a
   * headless caller (tests/client/sim-host/worker-smoke.test.ts, a future server) can drive the
   * sim tick-by-tick without going through start()'s real-time scheduler. */
  step(): Snapshot {
    const stepStartedAtMs = performance.now();
    const snapshot = this.stepUninstrumented();
    metrics.recordSimStep(performance.now() - stepStartedAtMs);
    return snapshot;
  }

  private stepUninstrumented(): Snapshot {
    const tick = this.run.state.tick;
    this.recentPings = this.recentPings.filter((p) => tick - p.tick <= PING_REACTION_WINDOW_TICKS);
    const botResult = computeBotCommands(this.run, this.bots, this.memories, this.prng, this.recentPings);
    this.memories = botResult.memories;
    this.prng = botResult.prng;

    const commands: Command[] = [...computePlayerCommands(this.run, this.setup, tick, this.pending), ...botResult.commands];
    this.pending = { moves: [], casts: [], targetCycles: 0 };
    if (this.pendingStopcast) {
      commands.push({ tick, actorId: this.setup.humanActorId, kind: "stopcast" });
      this.pendingStopcast = false;
    }

    const stepped = stepRun(this.run, commands);
    this.run = stepped.run;
    fixupTargeting(this.run, this.setup);
    this.run = applyDeathsAndWipe(this.run, stepped.events, tick);

    const signals = computeRunEndSignals(this.run, this.setup, this.abandonRequested, tick);
    this.abandonRequested = false;
    this.runEndState = stepRunEnd(this.runEndState, signals);

    return buildSnapshot(this.run, this.setup, this.maxSlackTicks, this.runEndState.info);
  }
}
