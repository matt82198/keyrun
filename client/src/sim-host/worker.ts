// Fake sim host: produces a moving snapshot stream at 20Hz for the POC
// (a player capsule driven by local commands, four idle party capsules,
// six enemy capsules in a pack, a rotating cone telegraph and a circle
// telegraph). An integration lane swaps this for the real sim later.
//
// Deviation from the dispatch brief: this runs on the main thread via
// setInterval rather than inside an actual Worker thread. The shared
// esbuild config (scripts/build.mjs, owned by the tooling lane) only
// bundles fixed entry points (client/src/main.ts, screen/src/main.ts) with
// no worker-splitting support, so a literal `new Worker(...)` would have no
// buildable script to load. The class below keeps the same push-snapshot
// interface a real Worker would use (onSnapshot callback), so swapping in
// an actual Worker/postMessage transport later is a small, local change.
import type { ActorSnapshot, Snapshot, TelegraphSnapshot } from "./snapshot.js";
import { SIM_TICK_MS } from "./snapshot.js";
import type { AnyCommand } from "../input/commands.js";

const PLAYER_ID = 1;
const PARTY_IDS = [2, 3, 4, 5];
const ENEMY_IDS = [11, 12, 13, 14, 15, 16];
const ABILITY_IDS = ["1", "2", "3", "4", "5", "Q", "E", "R", "F"];
const ABILITY_COOLDOWN_TICKS: Record<string, number> = {
  "1": 20,
  "2": 60,
  "3": 40,
  "4": 30,
  "5": 100,
  Q: 30,
  E: 40,
  R: 80,
  F: 24,
};
const CAST_DURATION_TICKS: Record<string, number> = { "2": 30, "5": 40 };
const RESOURCE_MAX = 100;
const RESOURCE_REGEN_PER_TICK = 0.6;
const RESOURCE_COST = 15;
const ENEMY_RESPAWN_DELAY_TICKS = 60;
const TELEGRAPH_CYCLE_TICKS = 80;

function freshCooldowns(): Record<string, number> {
  return Object.fromEntries(ABILITY_IDS.map((id) => [id, 0]));
}

function makeActor(overrides: Partial<ActorSnapshot> & { id: number }): ActorSnapshot {
  return {
    kind: "player",
    role: "damage",
    classId: "generic",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 100,
    maxHealth: 100,
    resource: 0,
    maxResource: 0,
    alive: true,
    targetId: null,
    cast: null,
    cooldowns: {},
    ...overrides,
  };
}

export class SimHost {
  private tick = 0;
  private actors = new Map<number, ActorSnapshot>();
  private telegraphs: TelegraphSnapshot[] = [];
  private pendingMoves: { dx: number; dz: number }[] = [];
  private pendingCasts: string[] = [];
  private pendingTargetCycles = 0;
  private enemyWipeTick: number | null = null;
  private intervalHandle: ReturnType<typeof setInterval> | null = null;
  private readonly timerLimitTicks = 20 * 60 * 8; // 8 minute POC timer

  constructor() {
    this.actors.set(
      PLAYER_ID,
      makeActor({
        id: PLAYER_ID,
        role: "tank",
        classId: "warrior",
        pos: { x: 0, y: 0, z: 6000 },
        resource: RESOURCE_MAX,
        maxResource: RESOURCE_MAX,
        cooldowns: freshCooldowns(),
      }),
    );

    const partyRoles: Array<{ role: string; classId: string }> = [
      { role: "healer", classId: "priest" },
      { role: "damage", classId: "mage" },
      { role: "damage", classId: "rogue" },
      { role: "damage", classId: "hunter" },
    ];
    PARTY_IDS.forEach((id, i) => {
      const cfg = partyRoles[i]!;
      this.actors.set(
        id,
        makeActor({
          id,
          role: cfg.role,
          classId: cfg.classId,
          pos: { x: -2100 + i * 1400, y: 0, z: 7500 },
          resource: RESOURCE_MAX,
          maxResource: RESOURCE_MAX,
        }),
      );
    });

    ENEMY_IDS.forEach((id, i) => {
      const angle = (i / ENEMY_IDS.length) * Math.PI * 2;
      this.actors.set(
        id,
        makeActor({
          id,
          kind: "enemy",
          role: "enemy",
          classId: "mob",
          pos: { x: Math.round(Math.cos(angle) * 2500), y: 0, z: -6000 + Math.round(Math.sin(angle) * 2500) },
          health: 60,
          maxHealth: 60,
        }),
      );
    });

    this.telegraphs = [
      {
        id: "cone-1",
        shape: "cone",
        center: { x: 0, z: -6000 },
        radius: 4500,
        angle1024: 170,
        yaw: 0,
        resolvesTick: TELEGRAPH_CYCLE_TICKS,
        danger: true,
      },
      {
        id: "circle-1",
        shape: "circle",
        center: { x: 2500, z: -3000 },
        radius: 1800,
        resolvesTick: Math.round(TELEGRAPH_CYCLE_TICKS * 1.4),
        danger: true,
      },
    ];
  }

  /** Queues a command produced by the input layer; applied on the next tick. */
  applyCommand(cmd: AnyCommand): void {
    if (cmd.type === "move") this.pendingMoves.push({ dx: cmd.dx, dz: cmd.dz });
    else if (cmd.type === "cast") this.pendingCasts.push(cmd.abilityId);
    else if (cmd.type === "targetCycle") this.pendingTargetCycles += 1;
  }

  /**
   * Starts the tick loop. `pullCommands`, when given, is called once at the
   * start of every tick so input sampling stays exactly tick-synced instead
   * of racing the interval from outside.
   */
  start(onSnapshot: (snapshot: Snapshot) => void, pullCommands?: () => AnyCommand[]): () => void {
    this.intervalHandle = setInterval(() => {
      if (pullCommands) for (const cmd of pullCommands()) this.applyCommand(cmd);
      onSnapshot(this.step());
    }, SIM_TICK_MS);
    return () => {
      if (this.intervalHandle !== null) clearInterval(this.intervalHandle);
      this.intervalHandle = null;
    };
  }

  private step(): Snapshot {
    this.tick += 1;
    this.applyPendingMoves();
    this.applyPendingTargetCycles();
    this.applyPendingCasts();
    this.tickCooldownsAndCasts();
    this.tickResources();
    this.driftEnemyPack();
    this.tickTelegraphs();
    this.maybeRespawnEnemies();

    return {
      tick: this.tick,
      actors: [...this.actors.values()].map((a) => ({ ...a, pos: { ...a.pos }, cooldowns: { ...a.cooldowns } })),
      telegraphs: this.telegraphs.map((t) => ({ ...t })),
      runTimerTicks: this.tick,
      timerLimitTicks: this.timerLimitTicks,
    };
  }

  private applyPendingMoves(): void {
    if (this.pendingMoves.length === 0) return;
    const player = this.actors.get(PLAYER_ID);
    if (!player) return;
    for (const move of this.pendingMoves) {
      player.pos.x += move.dx;
      player.pos.z += move.dz;
      if (move.dx !== 0 || move.dz !== 0) {
        player.yaw = Math.round(((Math.atan2(move.dx, move.dz) / (Math.PI * 2)) * 1024 + 1024) % 1024);
      }
    }
    this.pendingMoves = [];
  }

  private applyPendingTargetCycles(): void {
    if (this.pendingTargetCycles === 0) return;
    const player = this.actors.get(PLAYER_ID);
    if (!player) {
      this.pendingTargetCycles = 0;
      return;
    }
    const alive = ENEMY_IDS.filter((id) => this.actors.get(id)?.alive);
    if (alive.length > 0) {
      for (let i = 0; i < this.pendingTargetCycles; i++) {
        const currentIndex = player.targetId === null ? -1 : alive.indexOf(player.targetId);
        player.targetId = alive[(currentIndex + 1) % alive.length]!;
      }
    }
    this.pendingTargetCycles = 0;
  }

  private applyPendingCasts(): void {
    if (this.pendingCasts.length === 0) return;
    const player = this.actors.get(PLAYER_ID);
    if (!player) {
      this.pendingCasts = [];
      return;
    }
    for (const abilityId of this.pendingCasts) {
      if ((player.cooldowns[abilityId] ?? 0) > 0) continue;
      if (player.resource < RESOURCE_COST) continue;

      player.cooldowns[abilityId] = ABILITY_COOLDOWN_TICKS[abilityId] ?? 20;
      player.resource = Math.max(0, player.resource - RESOURCE_COST);

      const castTicks = CAST_DURATION_TICKS[abilityId];
      if (castTicks) {
        player.cast = { abilityId, startTick: this.tick, endTick: this.tick + castTicks };
      }

      const target = player.targetId !== null ? this.actors.get(player.targetId) : null;
      if (target?.alive) {
        target.health = Math.max(0, target.health - (10 + Math.floor(Math.random() * 8)));
        if (target.health === 0) {
          target.alive = false;
          if (player.targetId === target.id) player.targetId = null;
        }
      }
    }
    this.pendingCasts = [];
  }

  private tickCooldownsAndCasts(): void {
    for (const actor of this.actors.values()) {
      for (const id of Object.keys(actor.cooldowns)) {
        if (actor.cooldowns[id]! > 0) actor.cooldowns[id] = actor.cooldowns[id]! - 1;
      }
      if (actor.cast && this.tick >= actor.cast.endTick) actor.cast = null;
    }
  }

  private tickResources(): void {
    for (const actor of this.actors.values()) {
      if (actor.maxResource > 0) {
        actor.resource = Math.min(actor.maxResource, actor.resource + RESOURCE_REGEN_PER_TICK);
      }
    }
  }

  private driftEnemyPack(): void {
    const t = this.tick / 200;
    ENEMY_IDS.forEach((id, i) => {
      const actor = this.actors.get(id);
      if (!actor || !actor.alive) return;
      const angle = (i / ENEMY_IDS.length) * Math.PI * 2 + t;
      actor.pos.x = Math.round(Math.cos(angle) * 2500);
      actor.pos.z = -6000 + Math.round(Math.sin(angle) * 2500);
      actor.yaw = Math.round((((angle + Math.PI / 2) / (Math.PI * 2)) * 1024 + 1024) % 1024);
    });
  }

  private tickTelegraphs(): void {
    for (const telegraph of this.telegraphs) {
      if (telegraph.shape === "cone") {
        const spinTicks = this.tick % (TELEGRAPH_CYCLE_TICKS * 4);
        telegraph.yaw = Math.round((spinTicks / (TELEGRAPH_CYCLE_TICKS * 4)) * 1024);
      }
      if (this.tick >= telegraph.resolvesTick) {
        telegraph.resolvesTick = this.tick + TELEGRAPH_CYCLE_TICKS;
      }
    }
  }

  private maybeRespawnEnemies(): void {
    const anyAlive = ENEMY_IDS.some((id) => this.actors.get(id)?.alive);
    if (anyAlive) {
      this.enemyWipeTick = null;
      return;
    }
    if (this.enemyWipeTick === null) {
      this.enemyWipeTick = this.tick;
      return;
    }
    if (this.tick - this.enemyWipeTick >= ENEMY_RESPAWN_DELAY_TICKS) {
      for (const id of ENEMY_IDS) {
        const actor = this.actors.get(id);
        if (!actor) continue;
        actor.alive = true;
        actor.health = actor.maxHealth;
      }
      this.enemyWipeTick = null;
    }
  }
}
