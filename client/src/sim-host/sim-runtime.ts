/**
 * client/src/sim-host/sim-runtime.ts
 *
 * Pure sim-stepping runtime: runs sim/index.ts's createRun/stepRun against a real content/data
 * registry (run-setup.ts) -- the human plus four bots/src bots in Hollow Vault -- and maps the
 * resulting SimState + encounter telegraphs into Snapshot (snapshot-codec.ts). Command derivation
 * lives in player-commands.ts / bot-commands.ts. This file is the orchestrator only, kept under
 * the repo's 190-LOC-per-file gate (tests/repo/loc.test.ts) by that split.
 *
 * P2 (policy-inside-stepRun): targeting fixup, Slack spend on death/wipe, and run-end detection
 * used to be a separate call this file made every tick (run-policy.ts, now deleted) into
 * hud/run-end.ts's own state machine (also deleted -- see that file's docstring). All of it lives
 * inside sim/index.ts's stepRun now; this file just forwards an explicit human abandon in as
 * `stepRun`'s `abandonRequested` option (the one signal sim cannot derive on its own) and reads
 * `this.run.runEnd` back out. DEVIATION note this replaces: no respawn/rez mechanic exists anywhere
 * in sim -- a dead player actor simply stays dead. A full party wipe would otherwise soft-lock the
 * run forever (nothing left to act), so sim/run-wiring.ts's checkRunEnd auto-ends it "depleted"
 * after a grace window.
 *
 * Extracted out of worker.ts (research/11 D9 follow-up: that file is now the real Worker's own
 * bootstrap/message-loop, not this class) so `step()` stays exactly what it always was -- pure
 * state advancement, no timers, no self/postMessage -- and is testable directly under Node with no
 * worker/DOM faking at all (tests/client/sim-host/sim-runtime.test.ts). worker.ts's tick loop is
 * the only caller in production; it measures its own wall-clock cost around each `step()` call
 * (see that file's docstring for why: metrics.ts's singleton only exists on the main thread,
 * unreachable from inside a real Worker's separate global scope).
 */
import type { PingEvent } from "../input/ping.js";
import type { RunEndInfo, Snapshot } from "./snapshot.js";

import { createRun, stepRun, type Run } from "../../../sim/index.js";
import type { ActorId } from "../../../sim/core/state.js";
import type { Command } from "../../../sim/core/commands.js";
import { initialSlackTicks } from "../../../sim/encounter/timer.js";
import { makeBot, defaultMemory, type Bot, type BotMemory } from "../../../bots/src/bot.js";
import { seedPrng, type PrngState } from "../../../bots/src/skill.js";
import { PING_REACTION_WINDOW_TICKS } from "../../../bots/src/shared/pings.js";
import type { RunSetup } from "./run-setup.js";
import { buildSnapshot } from "./snapshot-codec.js";
import { computePlayerCommands, type PendingInput } from "./player-commands.js";
import { computeBotCommands } from "./bot-commands.js";
import type { HostCommand } from "./wire.js";

const SEED = 1;

export class SimRuntime {
  private run: Run;
  private readonly bots: Record<ActorId, Bot> = {};
  private memories: Record<ActorId, BotMemory> = {};
  private prng: PrngState;
  private pending: PendingInput = { moves: [], casts: [], targetCycles: 0 };
  private pendingStopcast = false;
  private abandonRequested = false;
  private recentPings: PingEvent[] = [];
  private readonly maxSlackTicks: number;

  constructor(private readonly setup: RunSetup) {
    // run-options-wiring: setup.runOptions carries the Clause pick + loadout-resolved abilities
    // (run-setup.ts's buildRunSetup); targeting already resolved: see sim/index.ts's createRun.
    this.run = createRun(SEED, setup.party, setup.dungeon, setup.content, setup.runOptions);

    for (const member of setup.party) {
      if (member.id === setup.humanActorId) continue;
      this.bots[member.id] = makeBot();
      this.memories[member.id] = defaultMemory(80);
    }
    this.prng = seedPrng(SEED);
    this.maxSlackTicks = initialSlackTicks(setup.keyLevel);
  }

  /** Queues a command produced by the host layer; applied on the next step(). */
  applyCommand(cmd: HostCommand): void {
    if (cmd.type === "move") this.pending.moves.push({ dx: cmd.dx, dz: cmd.dz });
    else if (cmd.type === "cast") this.pending.casts.push(cmd.abilityId);
    else if (cmd.type === "targetCycle") this.pending.targetCycles += 1;
    else if (cmd.type === "stopcast") this.pendingStopcast = true;
    else if (cmd.type === "abandon") this.abandonRequested = true;
    else if (cmd.type === "ping") this.recentPings.push(cmd.event);
  }

  /** Advances the run by exactly one tick and returns the resulting Snapshot. */
  step(): Snapshot {
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

    const stepped = stepRun(this.run, commands, { abandonRequested: this.abandonRequested });
    this.run = stepped.run;
    this.abandonRequested = false;

    const runEnd: RunEndInfo | null =
      this.run.runEnd.result !== null && this.run.runEnd.resultTick !== null
        ? { outcome: this.run.runEnd.result, tick: this.run.runEnd.resultTick }
        : null;

    return buildSnapshot(this.run, this.setup, this.maxSlackTicks, runEnd);
  }
}
