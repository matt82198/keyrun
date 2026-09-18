// Real sim host: runs sim/index.ts's createRun/stepRun against a minimal inline content set (two
// player abilities, one enemy script) and maps the resulting SimState + encounter telegraphs into
// the existing Snapshot shape. Replaces the earlier fake snapshot generator; no content/ package
// import (sim never imports content, per sim/CLAUDE.md) -- the ability/dungeon/script definitions
// below are this file's own minimal POC content, same as tests/sim/integration/fixtures.ts is for
// the sim test suite (a separate, deliberately duplicated copy -- client code must not depend on
// anything under tests/).
//
// Deviation from the dispatch brief: this runs on the main thread via setInterval rather than
// inside an actual Worker thread. The shared esbuild config (scripts/build.mjs, owned by the
// tooling lane) only bundles fixed entry points (client/src/main.ts, screen/src/main.ts) with no
// worker-splitting support, so a literal `new Worker(...)` would have no buildable script to load.
// The class below keeps the same push-snapshot interface a real Worker would use (onSnapshot
// callback), so swapping in an actual Worker/postMessage transport later is a small, local change.
//
// Deviation: imports below sim/ and bots/ use relative paths with an explicit ".js" specifier
// (resolving to the sibling ".ts" file, same convention every other client/ file already uses)
// rather than the tsconfig "@sim/*"/"@bots/*" path aliases the test suite uses. scripts/build.mjs
// is off-limits to this lane and has no esbuild alias plugin configured, so an "@sim/..." import
// here would typecheck but fail to bundle; relative ".js" imports work under both tsc and esbuild
// with no config changes.
import type { ActorSnapshot, Snapshot, TelegraphSnapshot } from "./snapshot.js";
import { SIM_TICK_MS } from "./snapshot.js";
import type { AnyCommand } from "../input/commands.js";

import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "../../../sim/index.js";
import type { ActorState } from "../../../sim/core/state.js";
import type { Command } from "../../../sim/core/commands.js";
import { yawFromComponents } from "../../../sim/core/atan.js";
import { cycleTarget } from "../../../sim/combat/targeting.js";
import type { AbilityDef } from "../../../sim/combat/abilities.js";
import type { DungeonDef } from "../../../sim/encounter/pulls.js";
import type { EncounterState } from "../../../sim/encounter/telegraphs.js";
import { makeBot, runBots, defaultMemory, type Bot, type BotMemory } from "../../../bots/src/bot.js";
import { seedPrng, type PrngState } from "../../../bots/src/skill.js";
import type { BotView, Telegraph as BotTelegraph } from "../../../bots/src/types.js";

const PLAYER_ID = 1;
const HEALER_ID = 2;
const DAMAGE_IDS = [3, 4, 5];
const BOSS_ID = 11;
const SEED = 1;

const ATTACK: AbilityDef = {
  id: "1",
  castTicks: 0,
  gcdTicks: 15,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 6000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "damage", amount: 8, school: "physical" }],
};

const HEAL: AbilityDef = {
  id: "2",
  castTicks: 0,
  gcdTicks: 15,
  cooldownTicks: 20,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 8000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "heal", amount: 20 }],
};

const CLEAVE: AbilityDef = {
  id: "cleave",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 0,
  shape: { kind: "cone", angle1024: 200, radius: 4500 },
  interruptible: false,
  effects: [{ op: "damage", amount: 10, school: "physical" }],
};

// Boss faces +z (yaw 256, sim/combat/targeting.ts's convention: yaw 0 = +x, 256 = +z) toward the
// party, which spawns around z=7500.
const DUNGEON: DungeonDef = {
  id: "poc-pull",
  pulls: [{ id: "boss-1", spawns: [{ actorId: BOSS_ID, classId: "mob", pos: { x: 0, y: 0, z: -6000 }, yaw: 256, health: 600 }] }],
};

const CONTENT: RunContent = {
  abilities: { "1": ATTACK, "2": HEAL, cleave: CLEAVE },
  effects: {},
  lockoutTicks: 40,
  baseSpeedMmPerTick: 180,
  actorRadiusMm: 400, // matches client/src/render/actors.ts's CAPSULE_RADIUS_M (0.4m)
  bounds: { minX: -9000, maxX: 9000, minZ: -9000, maxZ: 9000 },
  enemyScripts: {
    [BOSS_ID]: [
      { at: "loop", every: 100, cast: "cleave", telegraph: { shape: "cone", angle1024: 200, radius: 4500, leadTicks: 40 } },
    ],
  },
};

const PARTY: PartyMemberSpec[] = [
  { id: PLAYER_ID, classId: "warrior", role: "tank", pos: { x: 0, y: 0, z: 6000 }, maxHealth: 100, maxResource: 100 },
  { id: HEALER_ID, classId: "priest", role: "healer", pos: { x: -2100, y: 0, z: 7500 }, maxHealth: 80, maxResource: 100 },
  { id: DAMAGE_IDS[0]!, classId: "mage", role: "damage", pos: { x: -700, y: 0, z: 7500 }, maxHealth: 70, maxResource: 100 },
  { id: DAMAGE_IDS[1]!, classId: "rogue", role: "damage", pos: { x: 700, y: 0, z: 7500 }, maxHealth: 70, maxResource: 100 },
  { id: DAMAGE_IDS[2]!, classId: "hunter", role: "damage", pos: { x: 2100, y: 0, z: 7500 }, maxHealth: 70, maxResource: 100 },
];

const HEALER_KIT: BotView["classKit"] = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: "2" };
const DAMAGE_KIT: BotView["classKit"] = { rotation: ["1"], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

/** sim/encounter's registered Telegraphs (origin/yaw frozen at announce time) mapped onto
 * bots/src's own Telegraph shape. Every telegraph in this POC content is treated as dangerous. */
function mapTelegraphsForBots(telegraphs: EncounterState["telegraphs"]): BotTelegraph[] {
  return telegraphs.map((t) => ({
    id: String(t.id),
    shape: t.shape,
    center: { x: t.origin.x, z: t.origin.z },
    yaw: t.yaw,
    resolvesTick: t.resolvesTick,
    danger: true,
    ...(t.radius !== undefined ? { radius: t.radius } : {}),
    ...(t.angle1024 !== undefined ? { angle1024: t.angle1024 } : {}),
    ...(t.length !== undefined ? { length: t.length } : {}),
    ...(t.width !== undefined ? { width: t.width } : {}),
  }));
}

/** Ability cooldowns are stored as absolute ready-ticks (see sim/combat/cooldowns.ts); the HUD
 * (client/src/render/hud.ts) wants ticks-remaining. */
function remainingCooldowns(actor: ActorState, tick: number): Record<string, number> {
  const out: Record<string, number> = {};
  for (const abilityId of Object.keys(actor.cooldowns)) {
    out[abilityId] = Math.max(0, (actor.cooldowns[abilityId] ?? 0) - tick);
  }
  return out;
}

function toActorSnapshot(actor: ActorState, tick: number): ActorSnapshot {
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
  private readonly bots: Record<number, Bot> = {};
  private memories: Record<number, BotMemory> = {};
  private prng: PrngState;
  private pendingMoves: { dx: number; dz: number }[] = [];
  private pendingCasts: string[] = [];
  private pendingTargetCycles = 0;
  private intervalHandle: ReturnType<typeof setInterval> | null = null;
  private readonly timerLimitTicks = 20 * 60 * 8; // 8 minute POC timer

  constructor() {
    this.run = createRun(SEED, PARTY, DUNGEON, CONTENT);

    // Damage bots don't do their own target acquisition (see bots/src/roles.ts); point them at
    // the boss once, up front, so their rotation ability has a legal target from tick 0.
    for (const id of DAMAGE_IDS) {
      const actor = this.run.state.actors[id];
      if (actor) actor.targetId = BOSS_ID;
    }

    this.bots[HEALER_ID] = makeBot();
    this.memories[HEALER_ID] = defaultMemory(80);
    for (const id of DAMAGE_IDS) {
      this.bots[id] = makeBot();
      this.memories[id] = defaultMemory(80);
    }
    this.prng = seedPrng(SEED);
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
    const tick = this.run.state.tick;
    const commands: Command[] = [...this.playerCommands(tick), ...this.botCommands()];

    const stepped = stepRun(this.run, commands);
    this.run = stepped.run;

    return this.buildSnapshot();
  }

  private playerCommands(tick: number): Command[] {
    const commands: Command[] = [];
    const player = this.run.state.actors[PLAYER_ID];

    for (const move of this.pendingMoves) {
      if (move.dx === 0 && move.dz === 0) continue;
      const dir = { x: move.dx, z: move.dz };
      commands.push({ tick, actorId: PLAYER_ID, kind: "move", dir, yaw: yawFromComponents(dir.x, dir.z) });
    }
    this.pendingMoves = [];

    if (this.pendingTargetCycles > 0 && player) {
      const enemies = Object.values(this.run.state.actors).filter((a) => a.kind === "enemy");
      let targetId = player.targetId;
      for (let i = 0; i < this.pendingTargetCycles; i++) targetId = cycleTarget(enemies, targetId);
      commands.push({ tick, actorId: PLAYER_ID, kind: "target", targetId });
    }
    this.pendingTargetCycles = 0;

    for (const abilityId of this.pendingCasts) {
      // Ability shapes in this content are all "target"; fall back to self so a heal with no
      // explicit target (nothing tab-targeted yet) still has a legal target instead of no-op'ing.
      const targetId = player?.targetId ?? PLAYER_ID;
      commands.push({ tick, actorId: PLAYER_ID, kind: "cast", abilityId, targetId });
    }
    this.pendingCasts = [];

    return commands;
  }

  private botCommands(): Command[] {
    const telegraphs = mapTelegraphsForBots(this.run.encounter.telegraphs);

    const healerView: BotView = { state: this.run.state, telegraphs, classKit: HEALER_KIT };
    const healerBots: Record<number, Bot> = {};
    const healerMemories: Record<number, BotMemory> = {};
    const healerBot = this.bots[HEALER_ID];
    if (healerBot) {
      healerBots[HEALER_ID] = healerBot;
      healerMemories[HEALER_ID] = this.memories[HEALER_ID] ?? defaultMemory();
    }
    const healerResult = runBots(healerView, healerBots, healerMemories, this.prng);

    const damageView: BotView = { state: this.run.state, telegraphs, classKit: DAMAGE_KIT };
    const damageBots: Record<number, Bot> = {};
    const damageMemories: Record<number, BotMemory> = {};
    for (const id of DAMAGE_IDS) {
      const bot = this.bots[id];
      if (!bot) continue;
      damageBots[id] = bot;
      damageMemories[id] = this.memories[id] ?? defaultMemory();
    }
    const damageResult = runBots(damageView, damageBots, damageMemories, healerResult.prng);

    this.memories = { ...healerResult.memories, ...damageResult.memories };
    this.prng = damageResult.prng;

    return [...healerResult.commands, ...damageResult.commands];
  }

  private buildSnapshot(): Snapshot {
    const state = this.run.state;
    const actors: ActorSnapshot[] = Object.values(state.actors).map((actor) => toActorSnapshot(actor, state.tick));

    const telegraphs: TelegraphSnapshot[] = this.run.encounter.telegraphs.map((t) => ({
      id: String(t.id),
      shape: t.shape,
      center: { x: t.origin.x, z: t.origin.z },
      yaw: t.yaw,
      resolvesTick: t.resolvesTick,
      danger: true,
      ...(t.radius !== undefined ? { radius: t.radius } : {}),
      ...(t.angle1024 !== undefined ? { angle1024: t.angle1024 } : {}),
      ...(t.length !== undefined ? { length: t.length } : {}),
      ...(t.width !== undefined ? { width: t.width } : {}),
    }));

    return {
      tick: state.tick,
      actors,
      telegraphs,
      runTimerTicks: state.runTimerTicks,
      timerLimitTicks: this.timerLimitTicks,
    };
  }
}
