/**
 * tests/sim/integration/fixtures.ts — a minimal, hand-authored content set (no content/ package;
 * sim never imports content, per sim/CLAUDE.md) shared by the integration and golden-replay
 * suites: one player ability ("1", instant single-target damage), one enemy ability ("cleave", an
 * instant cone), one enemy script (a boss that cleaves on a 100-tick loop with a 40-tick telegraph
 * lead), and a single-pull dungeon.
 *
 * Layout: PLAYER_ID casts "1" on BOSS_ID every 20 ticks. DUMMY_ID sits inside the boss's cleave
 * cone and never receives a command all run -- it deliberately "fails to move" so every cleave
 * lands on it, exercising the full telegraph-announce -> real-cast-resolves -> damage path
 * end to end (sim/encounter's telegraph is informational; the actual damage comes from the
 * enemy's ordinary cast completing through sim/combat/casting.ts against its live facing/shape,
 * per sim/index.ts's stepRun doc comment).
 *
 * Facing (integrate/playable-dungeon: SLAM-CIRCLE-HITS #369 merged with ENEMY-ENGAGE): the cone cleave now counts
 * toward the boss's engage reach (chase.ts engageReachMm), so the boss is a chaser and, in reach, turns to face its
 * script target (BIBLE s3: "a slow frontal cone, readable from the boss's facing"). This content has no threat op, so
 * that target is the NEAREST living player. The dummy therefore stands nearest, at (1000, 0, 0) in front of the boss,
 * where the boss keeps facing it. At its old (3000, 0, 0) the party behind the boss was nearer, the boss turned to
 * face them, and the cleave hit the bots instead of the dummy.
 */
import type { AbilityDef } from "@sim/combat/abilities";
import type { Command } from "@sim/core/commands";
import type { DungeonDef } from "@sim/encounter/pulls";
import type { EncounterState } from "@sim/encounter/telegraphs";
import type { PartyMemberSpec, RunContent } from "@sim/index";
import type { BotView, Telegraph as BotTelegraph } from "@bots/src/types";

export const PLAYER_ID = 1;
export const DUMMY_ID = 2;
export const BOT_IDS = [3, 4, 5];
export const BOSS_ID = 11;

export const SEED = 42424242;
export const TICKS = 600;

export const ATTACK: AbilityDef = {
  id: "1",
  castTicks: 0,
  gcdTicks: 30,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "damage", amount: 5, school: "physical" }],
};

export const CLEAVE: AbilityDef = {
  id: "cleave",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 0,
  shape: { kind: "cone", angle1024: 200, radius: 4000 },
  interruptible: false,
  effects: [{ op: "damage", amount: 20, school: "physical" }],
};

/** Boss at the origin, facing +x (yaw 0); the dummy sits at (1000, 0, 0), well inside the cone and nearest the boss. */
export const DUNGEON: DungeonDef = {
  id: "golden-pull",
  pulls: [
    {
      id: "boss",
      spawns: [{ actorId: BOSS_ID, classId: "boss", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 5000 }],
    },
  ],
};

export const CONTENT: RunContent = {
  abilities: { "1": ATTACK, cleave: CLEAVE },
  effects: {},
  lockoutTicks: 40,
  baseSpeedMmPerTick: 180,
  actorRadiusMm: 400,
  bounds: { minX: -10000, maxX: 10000, minZ: -10000, maxZ: 10000 },
  enemyScripts: {
    [BOSS_ID]: [
      { at: "loop", every: 100, cast: "cleave", telegraph: { shape: "cone", angle1024: 200, radius: 4000, leadTicks: 40 } },
    ],
  },
};

/** Player behind the boss (outside the +x-facing cone), dummy inside it and nearer the boss than any party member. Both within ATTACK's range. */
export const BASE_PARTY: PartyMemberSpec[] = [
  { id: PLAYER_ID, classId: "warrior", role: "damage", pos: { x: -2000, y: 0, z: 0 }, maxHealth: 200, maxResource: 100 },
  { id: DUMMY_ID, classId: "target-dummy", role: "damage", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 300, maxResource: 0 },
];

/** Three real bots/src-controlled party members, positioned behind the boss like the player (so
 * the boss's -x-facing dot product is negative and they never land in its cone regardless of
 * their z spread -- see the inCone math in sim/combat/targeting.ts). */
export const BOT_PARTY: PartyMemberSpec[] = BOT_IDS.map((id, i) => ({
  id,
  classId: ["mage", "rogue", "hunter"][i]!,
  role: "damage" as const,
  pos: { x: -1500, y: 0, z: -800 + i * 800 },
  maxHealth: 120,
  maxResource: 100,
}));

export const FULL_PARTY: PartyMemberSpec[] = [...BASE_PARTY, ...BOT_PARTY];

/** Shared by every bot in this fixture: a one-ability rotation, no interrupt/defensive/heal. */
export const BOT_CLASS_KIT: BotView["classKit"] = {
  rotation: ["1"],
  interruptAbilityId: null,
  defensiveAbilityId: null,
  healAbilityId: null,
};

/** The player casts ability "1" on the boss every 20 ticks for the full 600-tick run. Nothing is
 * ever scripted for DUMMY_ID -- see the module doc comment. */
export function scriptedPlayerCommands(ticks: number = TICKS): Command[] {
  const commands: Command[] = [];
  for (let t = 20; t < ticks; t += 20) {
    commands.push({ tick: t, actorId: PLAYER_ID, kind: "cast", abilityId: "1", targetId: BOSS_ID });
  }
  return commands;
}

/** Maps sim/encounter's registered Telegraphs (origin/yaw frozen at announce time) onto bots/src's
 * own Telegraph shape for BotView. Every telegraph in this fixture is treated as dangerous. */
export function mapTelegraphsForBots(telegraphs: EncounterState["telegraphs"]): BotTelegraph[] {
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
