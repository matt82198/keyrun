/**
 * tests/sim/integration/bots-move.test.ts
 *
 * P0 regression guard: a real bot, driven through createRun/stepRun and bots/src/bot.ts's actual
 * runBots() (not a hand-rolled stand-in), must be able to move at all. bots/src previously built
 * `move` commands with a float Math.atan2 yaw and a float unit-vector `dir` (see bots/src/bot.ts's
 * old dangerAvoidCommand): sim/core/commands.ts's validateCommand requires integer dir components
 * in [-1000,1000] and an integer yaw in [0,1023], so every one of those commands failed
 * validation and sim/core/tick.ts's step() silently dropped it (fail-closed on untrusted input)
 * before it could ever move an actor. Every command a bot emits over the run is asserted to pass
 * the SAME validateCommand() the real step() gate uses.
 *
 * Both real telegraph producers are exercised via bots/src/view.ts's buildBotView -- the one
 * shared place that merges sim/encounter's boss telegraphs with sim/combat/floor-telegraphs.ts's
 * deriveFloorTelegraphs (PR #47's producer, previously never wired into any bot's actual view):
 * one test places an encounter (boss) telegraph directly under a bot, the other has a boss open a
 * real hostile floor Window under a bot via a real cast. Both assert the bot's position changes.
 *
 * Usage: `npx vitest run tests/sim/integration/bots-move.test.ts`
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import { validateCommand } from "@sim/core/commands";
import type { AbilityDef } from "@sim/combat/abilities";
import type { DungeonDef } from "@sim/encounter/pulls";
import { makeBot, runBots, defaultMemory, type Bot, type BotMemory } from "@bots/src/bot";
import { buildBotView } from "@bots/src/view";
import { seedPrng, type PrngState } from "@bots/src/skill";
import { BOSS_ID, BOT_CLASS_KIT, BOT_IDS, CONTENT, DUNGEON, FULL_PARTY, SEED, mapTelegraphsForBots } from "./fixtures";

const RUN_TICKS = 200;
const TELEGRAPH_RESOLVES_TICK = 1000; // well past RUN_TICKS: stays "dangerous" for the whole run
const TELEGRAPH_RADIUS_MM = 3000;

interface Harness {
  run: Run;
  bots: Record<number, Bot>;
  memories: Record<number, BotMemory>;
  prng: PrngState;
}

/** Advances one bot loop tick: build the merged view (buildBotView), run every bot's decide(),
 * validate every emitted command against the SAME validateCommand() the real sim gate uses, then
 * step the real sim forward with those commands. Returns the updated harness and this tick's
 * commands (for the caller's own rejection bookkeeping). */
function advanceOneTick(harness: Harness): { harness: Harness; commands: ReturnType<typeof runBots>["commands"] } {
  const view = buildBotView(harness.run.state, mapTelegraphsForBots(harness.run.encounter.telegraphs), BOT_CLASS_KIT);
  const botResult = runBots(view, harness.bots, harness.memories, harness.prng);
  const stepped = stepRun(harness.run, botResult.commands);
  return {
    harness: { run: stepped.run, bots: harness.bots, memories: botResult.memories, prng: botResult.prng },
    commands: botResult.commands,
  };
}

function freshBots(ids: readonly number[]): { bots: Record<number, Bot>; memories: Record<number, BotMemory> } {
  const bots: Record<number, Bot> = {};
  const memories: Record<number, BotMemory> = {};
  for (const id of ids) {
    bots[id] = makeBot();
    memories[id] = defaultMemory(100); // perfect skill dial: no reaction delay, no mistake roll
  }
  return { bots, memories };
}

function runAndCollect(harness: Harness, ticks: number): { harness: Harness; rejected: unknown[]; anyMove: boolean } {
  const rejected: unknown[] = [];
  let anyMove = false;
  let current = harness;
  for (let i = 0; i < ticks; i++) {
    const step = advanceOneTick(current);
    current = step.harness;
    for (const cmd of step.commands) {
      if (cmd.kind === "move") anyMove = true;
      if (validateCommand(cmd) === null) rejected.push(cmd);
    }
  }
  return { harness: current, rejected, anyMove };
}

describe("bots move through the real sim (P0 integer-command regression)", () => {
  it("moves a bot out from under an encounter telegraph, emitting zero commands the sim's own validator rejects", () => {
    const run = createRun(SEED, FULL_PARTY, DUNGEON, CONTENT);
    const telegraphedBotId = BOT_IDS[0]!;
    const bot = run.state.actors[telegraphedBotId]!;
    run.encounter.telegraphs.push({
      id: 9001,
      sourceId: BOSS_ID,
      abilityId: "test-puddle",
      shape: "circle",
      origin: { ...bot.pos },
      yaw: 0,
      radius: TELEGRAPH_RADIUS_MM,
      resolvesTick: TELEGRAPH_RESOLVES_TICK,
    });
    const startPos = { ...bot.pos };

    const { bots, memories } = freshBots(BOT_IDS);
    const { harness, rejected, anyMove } = runAndCollect({ run, bots, memories, prng: seedPrng(SEED) }, RUN_TICKS);

    expect(rejected).toEqual([]);
    expect(anyMove).toBe(true);
    expect(harness.run.state.actors[telegraphedBotId]!.pos).not.toEqual(startPos);
  });

  it("moves a bot out of a real hostile floor patch a boss opened, via deriveFloorTelegraphs merged into the bot view", () => {
    const FLOOR_BOT_ID = BOT_IDS[0]!;
    const patchCenter = { x: 2000, y: 0, z: 500 };
    const floorPatchAbility: AbilityDef = {
      id: "boss.floorpatch",
      castTicks: 0,
      gcdTicks: 0,
      cooldownTicks: 0,
      charges: 1,
      cost: { resource: "none", amount: 0 },
      range: 0,
      shape: { kind: "circle", radius: TELEGRAPH_RADIUS_MM },
      interruptible: false,
      effects: [
        { op: "window", windowId: "test-hazard", durationTicks: 300, onTarget: false, setupClass: "boss", radiusMm: TELEGRAPH_RADIUS_MM },
      ],
    };
    const floorDungeon: DungeonDef = {
      id: "floor-patch-pull",
      pulls: [{ id: "boss", spawns: [{ actorId: BOSS_ID, classId: "boss", pos: patchCenter, yaw: 0, health: 5000 }] }],
    };
    const floorContent: RunContent = {
      ...CONTENT,
      abilities: { ...CONTENT.abilities, [floorPatchAbility.id]: floorPatchAbility },
      enemyScripts: {},
    };
    const floorParty: PartyMemberSpec[] = [
      { id: FLOOR_BOT_ID, classId: "mage", role: "damage", pos: { ...patchCenter }, maxHealth: 120, maxResource: 100 },
    ];

    let run = createRun(SEED, floorParty, floorDungeon, floorContent);
    const startPos = { ...run.state.actors[FLOOR_BOT_ID]!.pos };

    // The boss opens its hostile floor patch dead-center under the bot -- a real cast, not a
    // hand-built Telegraph fixture.
    const opened = stepRun(run, [{ tick: run.state.tick, actorId: BOSS_ID, kind: "cast", abilityId: "boss.floorpatch" }]);
    run = opened.run;

    const { bots, memories } = freshBots([FLOOR_BOT_ID]);
    const { harness, rejected, anyMove } = runAndCollect({ run, bots, memories, prng: seedPrng(SEED) }, RUN_TICKS);

    expect(rejected).toEqual([]);
    expect(anyMove).toBe(true);
    expect(harness.run.state.actors[FLOOR_BOT_ID]!.pos).not.toEqual(startPos);
  });
});
