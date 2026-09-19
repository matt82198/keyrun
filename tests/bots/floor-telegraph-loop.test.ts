/**
 * tests/bots/floor-telegraph-loop.test.ts
 *
 * floor-patch-visibility, the missing link PR #40's merge grep flagged: not a hand-built Telegraph
 * fixture (tests/bots/floor-escape.test.ts already covers that shape), but the REAL producer
 * (sim/combat/floor-telegraphs.ts) driven off REAL sim state that a REAL sim/combat/rules.ts
 * StepRules call produced -- a boss-style ability opens a hostile floor Window (design/BIBLE.md
 * Sec 9, the seed.deathBloom pattern: a "window" op, onTarget:false, setupClass "boss", cast by an
 * enemy actor) -- and the REAL bots/src/bot.ts makeBot().decide() loop, proving a damage bot
 * standing in the patch decides to walk itself clear within a bounded number of ticks.
 *
 * Position/radius scale: real content mm-scale throughout (e.g. tests/sim/combat/floor-telegraphs.
 * test.ts and tests/sim/integration/floor-telegraph.test.ts's radius 1800-2500mm), now that
 * bots/src/shared/positioning.ts's toMM() -- which used to re-multiply already-mm positions and
 * Telegraph fields by 1000 -- is deleted (bots/CLAUDE.md). Escape movement is applied via
 * tests/bots/apply-move.ts, the same integer `dir`-to-mm-step math sim/combat/movement.ts's real
 * applyMovement performs, since a bot's emitted `dir` is now an integer unit vector scaled to
 * +-1000 (sim/core/commands.ts's validated Command schema), not a raw float.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "@sim/combat/actors";
import type { AbilityDef } from "@sim/combat/abilities";
import { makeRules, type RulesConfig } from "@sim/combat/rules";
import type { Command, SimEvent, SimState } from "@sim/combat/types";
import { deriveFloorTelegraphs, FLOOR_PATCH_PREFIX } from "@sim/combat/floor-telegraphs";
import { makeBot, defaultMemory } from "@bots/src/bot.js";
import { seedPrng } from "@bots/src/skill.js";
import { isInAnyDanger } from "@bots/src/shared/positioning.js";
import { applyMove } from "./apply-move.js";
import type { BotView } from "@bots/src/types.js";

const BOSS_ID = 9;
const DAMAGE_BOT_ID = 1;
const MAX_ESCAPE_TICKS = 15;
const STEP_MM = 2000; // sim-space mm/tick
const PATCH_RADIUS = 2000; // real content mm-scale (see tests/sim/combat/floor-telegraphs.test.ts)

// Mirrors content/data/abilities/enemies.json's real seed.deathBloom: instant, circle shape, a
// single "window" op with onTarget:false and setupClass "boss" -- a hostile floor patch.
const bossFloorPatchAbility: AbilityDef = {
  id: "boss.deathbloom",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "none", amount: 0 },
  range: 0,
  shape: { kind: "circle", radius: PATCH_RADIUS },
  interruptible: false,
  tags: ["setup:seedbloom"],
  effects: [{ op: "window", windowId: "seedbloom", durationTicks: 300, onTarget: false, setupClass: "boss", radiusMm: PATCH_RADIUS }],
};

describe("hostile floor patch, real producer + real StepRules + real bot decide loop", () => {
  it("moves a damage bot standing inside a boss-opened floor Window clear of it", () => {
    const boss = createActor({ id: BOSS_ID, kind: "enemy", classId: "seed", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 5000, maxResource: 0 });
    const damageBot = createActor({ id: DAMAGE_BOT_ID, kind: "player", classId: "warrior", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1000, maxResource: 100 });

    const cfg: RulesConfig = {
      abilities: { [bossFloorPatchAbility.id]: bossFloorPatchAbility },
      effects: {},
      lockoutTicks: 80,
      baseSpeedMmPerTick: 300,
      actorRadiusMm: 1,
      bounds: { minX: -5000, maxX: 5000, minZ: -5000, maxZ: 5000 },
    };
    const rules = makeRules(cfg);

    const state: SimState = {
      tick: 0,
      seed: 1,
      prng: [0, 0, 0, 0],
      actors: { [boss.id]: boss, [damageBot.id]: damageBot },
      runTimerTicks: 0,
      phase: "combat",
      log: [],
      floorWindows: {},
    };
    const events: SimEvent[] = [];
    const emit = (e: SimEvent): void => {
      events.push(e);
    };

    // The boss opens its hostile floor patch centred on its own position (0,0), dead-center under
    // the damage bot -- a REAL StepRules call, exercising the REAL windows.ts open path and this
    // lane's emission hook (floorWindowOpenedTelegraph) on the emitted "window_opened" event.
    const castCmd: Command = { tick: state.tick, actorId: BOSS_ID, kind: "cast", abilityId: "boss.deathbloom" };
    rules(state, [castCmd], emit);
    state.tick += 1;
    expect(events.some((e) => e.type === "window_opened" && e.windowId === "seedbloom" && e.onTarget === false)).toBe(true);
    expect(events.some((e) => e.type === "window_opened" && (e as { telegraph?: { id?: string } }).telegraph?.id === `${FLOOR_PATCH_PREFIX}seedbloom`)).toBe(true);

    // The producer under test: SimState.floorWindows -> a hostile "floor:<windowId>" Telegraph (no
    // nested "window:" segment -- that encoding is reserved for player-sourced patches).
    let telegraphs = deriveFloorTelegraphs(state);
    expect(telegraphs).toHaveLength(1);
    expect(telegraphs[0]?.id).toBe(`${FLOOR_PATCH_PREFIX}seedbloom`);
    expect(telegraphs[0]?.danger).toBe(false);

    const bot = makeBot();
    let memory = defaultMemory(100);
    let prng = seedPrng(1);
    const classKit = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

    let ticks = 0;
    for (; ticks < MAX_ESCAPE_TICKS; ticks++) {
      telegraphs = deriveFloorTelegraphs(state);
      const self = state.actors[DAMAGE_BOT_ID]!;
      if (!isInAnyDanger(self.pos, telegraphs.map((t) => ({ ...t, danger: true })))) break;

      const view: BotView = { state, telegraphs, classKit };
      const result = bot.decide(view, memory, DAMAGE_BOT_ID, prng);
      memory = result.memory;
      prng = result.prng;

      const move = result.commands.find((c) => c.kind === "move");
      expect(move).toBeDefined();
      self.pos = { ...self.pos, ...applyMove(self.pos, move, STEP_MM) };
      state.tick += 1;
    }

    expect(ticks).toBeLessThan(MAX_ESCAPE_TICKS);
    const finalSelf = state.actors[DAMAGE_BOT_ID]!;
    expect(isInAnyDanger(finalSelf.pos, deriveFloorTelegraphs(state).map((t) => ({ ...t, danger: true })))).toBe(false);
  });
});
