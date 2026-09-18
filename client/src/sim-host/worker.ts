// Real sim host: runs sim/index.ts's createRun/stepRun against a real content/data registry
// (run-setup.ts) -- the human plus four bots/src bots in Hollow Vault -- and maps the resulting
// SimState + encounter telegraphs into Snapshot.
//
// DEVIATION (inherited from the pre-existing scaffold this replaces): runs on the main thread via
// a timer rather than inside an actual Worker thread. scripts/build.mjs (off-limits to this lane)
// only bundles fixed entry points with no worker-splitting support, so a literal `new Worker(...)`
// would have no buildable script to load. `start()` now drives a fixed-timestep accumulator with
// capped catch-up (scheduler.ts) instead of a bare `setInterval` tick-for-tick, so a throttled/
// backgrounded tab resumes cleanly instead of drifting or bursting an unbounded backlog.
//
// DEVIATION: no respawn/rez mechanic exists anywhere in sim -- a dead player actor simply stays
// dead. A full party wipe would otherwise soft-lock the run forever (nothing left to act), so
// `updateRunEnd` auto-abandons WIPE_GRACE_TICKS after every player is dead (see hud/run-end.ts).
//
// Enemies never auto-target (sim/combat has no nearest-enemy AI) and damage bots do not acquire
// their own targets either (bots/src/roles.ts's damageDecide casts at `self.targetId` only) --
// `fixupTargeting()` points every untargeted enemy at the tank and every damage bot without a
// living target at the current pull's first living enemy, once per tick.
import type { ActorSnapshot, Snapshot } from "./snapshot.js";
import { SIM_TICK_MS } from "./snapshot.js";
import type { AnyCommand } from "../input/commands.js";
import type { HostControlCommand } from "./host-commands.js";
import type { RunEndSignals, RunEndState } from "../hud/run-end.js";
import { initialRunEndState, stepRunEnd } from "../hud/run-end.js";

import { createRun, stepRun, type Run } from "../../../sim/index.js";
import type { ActorId, ActorState, Tick } from "../../../sim/core/state.js";
import type { Command } from "../../../sim/core/commands.js";
import type { SimEvent } from "../../../sim/core/events.js";
import { yawFromComponents } from "../../../sim/core/atan.js";
import { cycleTarget } from "../../../sim/combat/targeting.js";
import { spendSlackOnDeath, spendSlackOnWipe, initialSlackTicks } from "../../../sim/encounter/timer.js";
import { makeBot, runBots, defaultMemory, type Bot, type BotMemory } from "../../../bots/src/bot.js";
import { seedPrng, type PrngState } from "../../../bots/src/skill.js";
import type { BotView } from "../../../bots/src/types.js";
import type { RunSetup } from "./run-setup.js";
import { computeTicksToRun } from "./scheduler.js";
import { toBotTelegraph, toTelegraphSnapshot } from "./telegraph-map.js";

const SEED = 1;

type HostCommand = AnyCommand | HostControlCommand;

function remainingCooldowns(actor: ActorState, tick: Tick): Record<string, number> {
  const out: Record<string, number> = {};
  for (const abilityId of Object.keys(actor.cooldowns)) {
    out[abilityId] = Math.max(0, (actor.cooldowns[abilityId] ?? 0) - tick);
  }
  return out;
}

function toActorSnapshot(actor: ActorState, tick: Tick): ActorSnapshot {
  return {
    id: actor.id,
    kind: actor.kind,
    role: actor.role,
    classId: actor.classId,
    pos: { ...actor.pos },
    yaw: actor.yaw,
    health: actor.health,
    maxHealth: actor.maxHealth,
    resource: actor.resource,
    maxResource: actor.maxResource,
    alive: actor.alive,
    targetId: actor.targetId,
    cast:
      actor.castId !== null && actor.castStartTick !== null && actor.castEndsTick !== null
        ? { abilityId: actor.castId, startTick: actor.castStartTick, endTick: actor.castEndsTick }
        : null,
    cooldowns: remainingCooldowns(actor, tick),
  };
}

export class SimHost {
  private run: Run;
  private readonly bots: Record<ActorId, Bot> = {};
  private memories: Record<ActorId, BotMemory> = {};
  private prng: PrngState;
  private pendingMoves: { dx: number; dz: number }[] = [];
  private pendingCasts: string[] = [];
  private pendingTargetCycles = 0;
  private pendingStopcast = false;
  private abandonRequested = false;
  private intervalHandle: ReturnType<typeof setInterval> | null = null;
  private accumulatedMs = 0;
  private lastNowMs = Date.now();
  private runEndState: RunEndState = initialRunEndState();
  private readonly maxSlackTicks: number;

  constructor(private readonly setup: RunSetup) {
    this.run = createRun(SEED, setup.party, setup.dungeon, setup.content);
    this.fixupTargeting();

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
    if (cmd.type === "move") this.pendingMoves.push({ dx: cmd.dx, dz: cmd.dz });
    else if (cmd.type === "cast") this.pendingCasts.push(cmd.abilityId);
    else if (cmd.type === "targetCycle") this.pendingTargetCycles += 1;
    else if (cmd.type === "stopcast") this.pendingStopcast = true;
    else if (cmd.type === "abandon") this.abandonRequested = true;
  }

  /** Fixed-timestep loop with capped catch-up (scheduler.ts); `pullCommands`, when given, is
   * called once per simulated tick so input sampling stays tick-synced. Stops itself once the run
   * ends (hud/run-end.ts's terminal state). */
  start(onSnapshot: (snapshot: Snapshot) => void, pullCommands?: () => AnyCommand[]): () => void {
    this.lastNowMs = Date.now();
    this.intervalHandle = setInterval(() => {
      const now = Date.now();
      const elapsed = now - this.lastNowMs;
      this.lastNowMs = now;
      const plan = computeTicksToRun(this.accumulatedMs, elapsed, SIM_TICK_MS);
      this.accumulatedMs = plan.nextAccumulatedMs;
      for (let i = 0; i < plan.ticks; i++) {
        if (pullCommands) for (const cmd of pullCommands()) this.applyCommand(cmd);
        onSnapshot(this.step());
        if (this.runEndState.info) {
          this.stop();
          return;
        }
      }
    }, SIM_TICK_MS);
    return () => this.stop();
  }

  private stop(): void {
    if (this.intervalHandle !== null) clearInterval(this.intervalHandle);
    this.intervalHandle = null;
  }

  private step(): Snapshot {
    const tick = this.run.state.tick;
    const commands: Command[] = [...this.playerCommands(tick), ...this.botCommands()];
    if (this.pendingStopcast) {
      commands.push({ tick, actorId: this.setup.humanActorId, kind: "stopcast" });
      this.pendingStopcast = false;
    }

    const stepped = stepRun(this.run, commands);
    this.run = stepped.run;
    this.fixupTargeting();
    this.applyDeathsAndWipe(stepped.events, tick);
    this.updateRunEnd(tick);

    return this.buildSnapshot();
  }

  /** See file docstring: enemies and damage bots never acquire targets on their own. */
  private fixupTargeting(): void {
    const state = this.run.state;
    const tank = state.actors[this.setup.tankActorId];
    const fallbackTargetId = tank && tank.alive ? tank.id : this.setup.humanActorId;
    const enemies = Object.values(state.actors).filter((a) => a.kind === "enemy" && a.alive);
    for (const enemy of enemies) {
      if (enemy.targetId === null) enemy.targetId = fallbackTargetId;
    }
    const firstEnemy = enemies[0];
    if (!firstEnemy) return;
    for (const member of this.setup.party) {
      if (member.role !== "damage") continue;
      const actor = state.actors[member.id];
      if (!actor || !actor.alive) continue;
      const current = actor.targetId !== null ? state.actors[actor.targetId] : undefined;
      const currentIsLivingEnemy = current !== undefined && current.kind === "enemy" && current.alive;
      if (!currentIsLivingEnemy) actor.targetId = firstEnemy.id;
    }
  }

  private applyDeathsAndWipe(events: SimEvent[], tick: Tick): void {
    const anyPlayerDeath = events.some(
      (e) => e.type === "death" && this.run.state.actors[e.actorId as ActorId]?.kind === "player",
    );
    if (!anyPlayerDeath) return;
    const noop = (): void => {};
    this.run = { ...this.run, encounter: spendSlackOnDeath(this.run.encounter, tick, noop) };
    const players = Object.values(this.run.state.actors).filter((a) => a.kind === "player");
    if (players.length > 0 && players.every((a) => !a.alive)) {
      this.run = { ...this.run, encounter: spendSlackOnWipe(this.run.encounter, tick, noop) };
    }
  }

  private updateRunEnd(tick: Tick): void {
    const boss = this.run.state.actors[this.setup.bossActorId];
    const players = Object.values(this.run.state.actors).filter((a) => a.kind === "player");
    const signals: RunEndSignals = {
      tick,
      bossDead: boss !== undefined && !boss.alive,
      depleted: this.run.encounter.depleted,
      allPlayersDead: players.length > 0 && players.every((a) => !a.alive),
      abandonPressed: this.abandonRequested,
    };
    this.runEndState = stepRunEnd(this.runEndState, signals);
    this.abandonRequested = false;
  }

  private playerCommands(tick: Tick): Command[] {
    const commands: Command[] = [];
    const humanId = this.setup.humanActorId;
    const player = this.run.state.actors[humanId];

    for (const move of this.pendingMoves) {
      if (move.dx === 0 && move.dz === 0) continue;
      const dir = { x: move.dx, z: move.dz };
      commands.push({ tick, actorId: humanId, kind: "move", dir, yaw: yawFromComponents(dir.x, dir.z) });
    }
    this.pendingMoves = [];

    if (this.pendingTargetCycles > 0 && player) {
      const enemies = Object.values(this.run.state.actors).filter((a) => a.kind === "enemy");
      let targetId = player.targetId;
      for (let i = 0; i < this.pendingTargetCycles; i++) targetId = cycleTarget(enemies, targetId);
      commands.push({ tick, actorId: humanId, kind: "target", targetId });
    }
    this.pendingTargetCycles = 0;

    for (const abilityId of this.pendingCasts) {
      const targetId = player?.targetId ?? humanId;
      commands.push({ tick, actorId: humanId, kind: "cast", abilityId, targetId });
    }
    this.pendingCasts = [];

    return commands;
  }

  private botCommands(): Command[] {
    const telegraphs = this.run.encounter.telegraphs.map(toBotTelegraph);
    const idsByClass = new Map<string, ActorId[]>();
    for (const [idStr, classId] of Object.entries(this.setup.actorClassId)) {
      const id = Number(idStr);
      if (!this.bots[id]) continue;
      idsByClass.set(classId, [...(idsByClass.get(classId) ?? []), id]);
    }

    let commands: Command[] = [];
    let prng = this.prng;
    for (const [classId, ids] of idsByClass) {
      const classKit = this.setup.classKits[classId];
      if (!classKit) continue;
      const view: BotView = { state: this.run.state, telegraphs, classKit };
      const botsForClass: Record<number, Bot> = {};
      const memoriesForClass: Record<number, BotMemory> = {};
      for (const id of ids) {
        const bot = this.bots[id];
        if (!bot) continue;
        botsForClass[id] = bot;
        memoriesForClass[id] = this.memories[id] ?? defaultMemory();
      }
      const result = runBots(view, botsForClass, memoriesForClass, prng);
      commands = commands.concat(result.commands);
      this.memories = { ...this.memories, ...result.memories };
      prng = result.prng;
    }
    this.prng = prng;
    return commands;
  }

  private buildSnapshot(): Snapshot {
    const state = this.run.state;
    const actors: ActorSnapshot[] = Object.values(state.actors).map((actor) => toActorSnapshot(actor, state.tick));
    const telegraphs = this.run.encounter.telegraphs.map(toTelegraphSnapshot);

    return {
      tick: state.tick,
      actors,
      telegraphs,
      runTimerTicks: state.runTimerTicks,
      timerLimitTicks: this.setup.parTicks,
      slackTicks: this.run.encounter.slackTicks,
      maxSlackTicks: this.maxSlackTicks,
      depleted: this.run.encounter.depleted,
      parTicks: this.run.encounter.parTicks,
      pullIndex: this.run.encounter.pullIndex,
      pullCount: this.setup.dungeon.pulls.length,
      runEnd: this.runEndState.info,
    };
  }
}
