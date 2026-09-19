/**
 * client/src/sim-host/sim-runtime.ts
 *
 * Pure sim-stepping runtime: runs sim/index.ts's createRun/stepRun against a real content/data
 * registry (run-setup.ts) -- the human plus four bots/src bots in Hollow Vault -- and maps the
 * resulting SimState + encounter telegraphs into Snapshot (snapshot-codec.ts). Targeting fixups,
 * Slack spending and run-end detection live in run-policy.ts / hud/run-end.ts; command derivation
 * lives in player-commands.ts / bot-commands.ts. This file is the orchestrator only, kept under
 * the repo's 190-LOC-per-file gate (tests/repo/loc.test.ts) by that split.
 *
 * Extracted out of worker.ts (research/11 D9 follow-up: that file is now the real Worker's own
 * bootstrap/message-loop, not this class) so `step()` stays exactly what it always was -- pure
 * state advancement, no timers, no self/postMessage -- and is testable directly under Node with no
 * worker/DOM faking at all (tests/client/sim-host/sim-runtime.test.ts). worker.ts's tick loop is
 * the only caller in production; it measures its own wall-clock cost around each `step()` call
 * (see that file's docstring for why: metrics.ts's singleton only exists on the main thread,
 * unreachable from inside a real Worker's separate global scope).
 *
 * DEVIATION: no respawn/rez mechanic exists anywhere in sim -- a dead player actor simply stays
 * dead. A full party wipe would otherwise soft-lock the run forever (nothing left to act), so
 * run-policy.ts's signals feed hud/run-end.ts's auto-abandon-after-a-grace-window policy.
 */
import type { PingEvent } from "../input/ping.js";
import type { Snapshot } from "./snapshot.js";
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
import { buildSnapshot } from "./snapshot-codec.js";
import { applyDeathsAndWipe, computeRunEndSignals, fixupTargeting } from "./run-policy.js";
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
  private runEndState: RunEndState = initialRunEndState();
  private readonly maxSlackTicks: number;

  constructor(private readonly setup: RunSetup) {
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
