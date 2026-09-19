/**
 * server/src/room.ts
 *
 * One authoritative Run: the fixed-timestep tick loop (sim/core/tick.ts's TICK_HZ, the single
 * source of truth), per-tick command buffering with late re-stamping, bot decision-making for
 * every bot-controlled slot, and per-tick broadcast to whichever sockets are currently bound to a
 * human-controlled actor. Party composition is `roster.ts`'s fixed ROLE_ROSTER -- slot -> actorId
 * never depends on join order, so `this.run` is bit-identical regardless of who (human or bot)
 * ends up controlling which slot (see server/CLAUDE.md's determinism invariant).
 *
 * DEVIATION (scope): no run-end detection (boss death / timer expiry / full-wipe auto-abandon --
 * client/src/sim-host/hud/run-end.ts's job) -- a room ticks until externally stopped. Out of this
 * lane's minimum slice; noted, not silently skipped.
 *
 * Usage: `const room = new Room("room-1", serverContent); room.attachHuman(1, ws); room.start();`
 */
import { createRun, stepRun, type Run } from "../../sim/index";
import { validateCommand, type Command } from "../../sim/core/commands";
import type { ActorId } from "../../sim/core/state";
import type { SimEvent } from "../../sim/core/events";
import { TICK_MS } from "../../sim/core/tick";
import { initialSlackTicks } from "../../sim/encounter/timer";
import { makeBot, defaultMemory, runBots, type Bot, type BotMemory } from "../../bots/src/bot.js";
import type { BotView } from "../../bots/src/types.js";
import { seedPrng, type PrngState } from "../../bots/src/skill.js";
import { toBotTelegraph } from "./telegraph.js";
import { buildSnapshot, type Snapshot } from "./snapshot.js";
import { ROLE_ROSTER, actorIdForSlot, buildPartySpec, type PartyRole } from "./roster.js";
import { fixupTargeting, applyDeathsAndWipe } from "./targeting-policy.js";
import type { ServerContent } from "./content-setup.js";
import type { ServerMessage } from "./protocol.js";
import type { WebSocket } from "ws";

export const SEED = 1;
const BOT_SKILL_DIAL = 80;
const EMPTY_CLASS_KIT: BotView["classKit"] = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

export interface EnqueueOk {
  ok: true;
  late?: { originalTick: number; newTick: number };
}
export interface EnqueueErr {
  ok: false;
  reason: string;
}

export class Room {
  readonly id: string;
  run: Run;
  readonly tankActorId: ActorId;
  private readonly parTicks: number;
  private readonly maxSlackTicks: number;
  private readonly pullCount: number;
  private bots: Record<ActorId, Bot> = {};
  private botMemories: Record<ActorId, BotMemory> = {};
  private prng: PrngState;
  private readonly humans = new Map<ActorId, WebSocket>();
  private pending: Command[] = [];
  private timer: ReturnType<typeof setInterval> | null = null;

  constructor(id: string, sc: ServerContent) {
    this.id = id;
    const party = buildPartySpec(sc.registry);
    this.run = createRun(SEED, party, sc.dungeon, sc.content);
    this.tankActorId = actorIdForSlot(ROLE_ROSTER.findIndex((s) => s.role === "tank"));
    this.parTicks = sc.parTicks;
    this.pullCount = sc.dungeon.pulls.length;
    this.maxSlackTicks = initialSlackTicks(1);
    this.prng = seedPrng(SEED);
    ROLE_ROSTER.forEach((_slot, i) => this.ensureBot(actorIdForSlot(i)));
    fixupTargeting(this.run, this.tankActorId);
  }

  private ensureBot(actorId: ActorId): void {
    this.bots[actorId] = makeBot();
    this.botMemories[actorId] = defaultMemory(BOT_SKILL_DIAL);
  }

  roleForActor(actorId: ActorId): PartyRole | undefined {
    return ROLE_ROSTER[actorId - 1]?.role;
  }

  classIdForActor(actorId: ActorId): string | undefined {
    return ROLE_ROSTER[actorId - 1]?.classId;
  }

  /** First bot-controlled slot matching `role`, or null if every matching slot is human-controlled
   * (the room is full for that role). Same lookup serves initial room-fill and mid-run backfill. */
  findOpenSlot(role: PartyRole): ActorId | null {
    for (let i = 0; i < ROLE_ROSTER.length; i++) {
      const slot = ROLE_ROSTER[i];
      const actorId = actorIdForSlot(i);
      if (slot && slot.role === role && !this.humans.has(actorId)) return actorId;
    }
    return null;
  }

  attachHuman(actorId: ActorId, ws: WebSocket): void {
    delete this.bots[actorId];
    delete this.botMemories[actorId];
    this.humans.set(actorId, ws);
  }

  /** The disconnecting actor is handed back to a fresh bot -- takes effect starting the next
   * tick() call, since bot decisions are recomputed from `this.bots`/`this.humans` every tick. */
  detachHuman(actorId: ActorId): void {
    this.humans.delete(actorId);
    this.ensureBot(actorId);
  }

  humanActorIds(): ActorId[] {
    return [...this.humans.keys()];
  }

  enqueueCommand(actorId: ActorId, rawCmd: unknown): EnqueueOk | EnqueueErr {
    if (!isRecord(rawCmd)) return { ok: false, reason: "malformed_command" };
    const cmd = validateCommand({ ...rawCmd, actorId });
    if (!cmd) return { ok: false, reason: "malformed_command" };

    const currentTick = this.run.state.tick;
    if (cmd.tick < currentTick) {
      this.pending.push({ ...cmd, tick: currentTick });
      return { ok: true, late: { originalTick: cmd.tick, newTick: currentTick } };
    }
    this.pending.push(cmd);
    return { ok: true };
  }

  /** Advances the run by exactly one tick. Public so tests can drive it directly, same reason
   * client/src/sim-host/worker.ts's `step()` is public. */
  tick(): { snapshot: Snapshot; events: SimEvent[] } {
    const currentTick = this.run.state.tick;
    const due: Command[] = [];
    const stillPending: Command[] = [];
    for (const c of this.pending) (c.tick === currentTick ? due : stillPending).push(c);
    this.pending = stillPending;

    const view: BotView = { state: this.run.state, telegraphs: this.run.encounter.telegraphs.map(toBotTelegraph), classKit: EMPTY_CLASS_KIT };
    const botResult = runBots(view, this.bots, this.botMemories, this.prng, []);
    this.botMemories = botResult.memories;
    this.prng = botResult.prng;

    const stepped = stepRun(this.run, [...due, ...botResult.commands]);
    this.run = stepped.run;
    fixupTargeting(this.run, this.tankActorId);
    this.run = applyDeathsAndWipe(this.run, stepped.events, currentTick);

    const snapshot = buildSnapshot(this.run, this.parTicks, this.maxSlackTicks, this.pullCount, new Set(this.humans.keys()));
    return { snapshot, events: stepped.events };
  }

  broadcast(msg: ServerMessage): void {
    const text = JSON.stringify(msg);
    const OPEN = 1; // WebSocket spec readyState (ws.OPEN) -- avoids importing the "ws" runtime value here.
    for (const ws of this.humans.values()) {
      if (ws.readyState === OPEN) ws.send(text);
    }
  }

  /** Fixed-timestep tick loop; broadcasts a `snapshot` (and an `event` batch, if any) to every
   * currently human-controlled socket each tick. Returns a stop function. */
  start(): () => void {
    this.timer = setInterval(() => {
      const { snapshot, events } = this.tick();
      this.broadcast({ v: 1, type: "snapshot", snapshot });
      if (events.length > 0) this.broadcast({ v: 1, type: "event", events });
    }, TICK_MS);
    return () => this.stop();
  }

  stop(): void {
    if (this.timer !== null) clearInterval(this.timer);
    this.timer = null;
  }
}
