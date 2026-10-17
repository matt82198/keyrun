/**
 * server/src/room.ts
 *
 * One authoritative Run: the fixed-timestep tick loop (sim/core/tick.ts's TICK_HZ), per-tick
 * command buffering with late re-stamping, bot decision-making for every bot-controlled slot,
 * and per-tick broadcast to every human-controlled socket. Party composition is `roster.ts`'s
 * fixed ROLE_ROSTER -- slot -> actorId never depends on join order, so `this.run` is bit-identical
 * regardless of who (human or bot) ends up controlling which slot (server/CLAUDE.md's determinism
 * invariant).
 *
 * v2 (server-run-lifecycle, P1): once the run is over, this stops its own tick loop, broadcasts
 * `run_end`, and calls `onEnd` (lobby.ts's release hook) exactly once -- fills the v0/v1 DEVIATION
 * this file used to carry ("a room ticks until externally stopped").
 *
 * P2 (policy-inside-stepRun): targeting fixup, Slack spend on death/wipe, and run-end DETECTION
 * (timed/depleted/abandoned, WHEN it happens) all now live inside sim/index.ts's stepRun itself --
 * this file no longer calls sim/run-wiring.ts's helpers, and no longer tracks its own RunEndState;
 * it just reads `this.run.runEnd` after each `stepRun` call and forwards an explicit human abandon
 * (the one signal sim cannot derive) in as `stepRun`'s `abandonRequested` option. run-end.ts is left
 * owning only the wire-payload concern (the reward roll), not the decision.
 *
 * RUN OPTIONS (run-options-wiring): an optional 4th `firstQueuer` arg carries the Clause pick and
 * loadout of the ONE queue request that opened this room ("fixed at room creation", like
 * `keyLevel`), resolved into a real `RunOptions` by room-options.ts's `firstQueuerOptions` (split
 * out for the 190-LOC cap -- see that file for the full contract incl. the backfill limitation). A
 * bad clauseId/loadout throws out of this constructor; lobby.ts/main.ts turn that into `error`.
 *
 * Usage: `const room = new Room("room-1", serverContent, onEnd); room.attachHuman(1, ws); room.start();`
 */
import { createRun, type Run } from "../../sim/index";
import type { Command } from "../../sim/core/commands";
import type { ActorId } from "../../sim/core/state";
import type { SimEvent } from "../../sim/core/events";
import { TICK_MS } from "../../sim/core/tick";
import { initialSlackTicks } from "../../sim/encounter/timer";
import { makeBot, defaultMemory, runBots, type Bot, type BotMemory } from "../../bots/src/bot.js";
import type { BotView } from "../../bots/src/types.js";
import { buildBotView } from "../../bots/src/view.js";
import { seedPrng, type PrngState } from "../../bots/src/skill.js";
import { toBotTelegraph } from "./telegraph.js";
import { buildSnapshot, type Snapshot } from "./snapshot.js";
import { stepWithHumans } from "./human-casts.js";
import { ROLE_ROSTER, actorIdForSlot, buildPartySpec, type PartyRole } from "./roster.js";
import { buildRunEndPayload } from "./run-end.js";
import { resolveCommand, type EnqueueOk, type EnqueueErr } from "./command-queue.js";
import type { ServerContent } from "./content-setup.js";
import { firstQueuerOptions, type RoomFirstQueuer } from "./room-options.js";
import type { ServerMessage } from "./protocol.js";
import type { WebSocket } from "ws";

export type { RoomFirstQueuer } from "./room-options.js";

export const SEED = 1;
const BOT_SKILL_DIAL = 80;
const EMPTY_CLASS_KIT: BotView["classKit"] = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

export class Room {
  readonly id: string;
  run: Run;
  private readonly keyLevel: number;
  private readonly parTicks: number;
  private readonly maxSlackTicks: number;
  private readonly pullCount: number;
  private bots: Record<ActorId, Bot> = {};
  private botMemories: Record<ActorId, BotMemory> = {};
  private prng: PrngState;
  private readonly humans = new Map<ActorId, WebSocket>();
  private pending: Command[] = [];
  private timer: ReturnType<typeof setInterval> | null = null;
  private abandonRequested = false;
  private readonly onEnd: ((roomId: string) => void) | undefined;

  constructor(id: string, sc: ServerContent, onEnd?: (roomId: string) => void, firstQueuer?: RoomFirstQueuer) {
    this.id = id;
    this.onEnd = onEnd;
    const party = buildPartySpec(sc.registry);
    this.run = createRun(SEED, party, sc.dungeon, sc.content, firstQueuerOptions(sc, firstQueuer)); // targeting already resolved: see sim/index.ts's createRun
    this.keyLevel = sc.keyLevel;
    this.parTicks = sc.parTicks;
    this.pullCount = sc.dungeon.pulls.length;
    this.maxSlackTicks = initialSlackTicks(sc.keyLevel);
    this.prng = seedPrng(SEED);
    ROLE_ROSTER.forEach((_slot, i) => this.ensureBot(actorIdForSlot(i)));
  }

  /** Any connected human can end the run early -- takes effect on the next tick() call (same
   * apply-next-tick discipline as detachHuman's bot-takeover). */
  requestAbandon(): void {
    this.abandonRequested = true;
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
    const result = resolveCommand(rawCmd, actorId, this.run.state.tick);
    if (result.ok) this.pending.push(result.cmd);
    return result;
  }

  /** Advances the run by exactly one tick. Public so tests can drive it directly, same reason
   * client/src/sim-host/worker.ts's `step()` is public. */
  tick(): { snapshot: Snapshot; events: SimEvent[] } {
    const currentTick = this.run.state.tick;
    const due: Command[] = [];
    const stillPending: Command[] = [];
    for (const c of this.pending) (c.tick === currentTick ? due : stillPending).push(c);
    this.pending = stillPending;

    const view = buildBotView(this.run.state, this.run.encounter.telegraphs.map(toBotTelegraph), EMPTY_CLASS_KIT);
    const botResult = runBots(view, this.bots, this.botMemories, this.prng, []);
    this.botMemories = botResult.memories;
    this.prng = botResult.prng;

    const { stepped, failed } = stepWithHumans(this.run, due, botResult.commands, (id) => this.humans.has(id), { abandonRequested: this.abandonRequested });
    this.run = stepped.run;
    const snapshot = buildSnapshot(this.run, this.parTicks, this.maxSlackTicks, this.pullCount, new Set(this.humans.keys()), stepped.events, failed);
    return { snapshot, events: stepped.events };
  }

  broadcast(msg: ServerMessage): void {
    const text = JSON.stringify(msg);
    const OPEN = 1; // WebSocket spec readyState (ws.OPEN) -- avoids importing the "ws" runtime value here.
    for (const ws of this.humans.values()) {
      if (ws.readyState === OPEN) ws.send(text);
    }
  }

  /** Fixed-timestep tick loop; broadcasts `snapshot` (and `event`, if any) every tick. Once
   * sim/index.ts's stepRun decides the run is over (`this.run.runEnd`), also broadcasts `run_end`,
   * stops (before the next tick could fire), and releases the room via `onEnd` -- all exactly once.
   * Returns a stop function. */
  start(): () => void {
    this.timer = setInterval(() => {
      const { snapshot, events } = this.tick();
      this.broadcast({ v: 3, type: "snapshot", snapshot });
      if (events.length > 0) this.broadcast({ v: 3, type: "event", events });
      const payload = buildRunEndPayload(this.run.runEnd, SEED, this.keyLevel);
      if (payload) {
        this.broadcast({ v: 3, type: "run_end", roomId: this.id, ...payload });
        this.stop();
        this.onEnd?.(this.id);
      }
    }, TICK_MS);
    return () => this.stop();
  }

  stop(): void {
    if (this.timer !== null) clearInterval(this.timer);
    this.timer = null;
  }
}
