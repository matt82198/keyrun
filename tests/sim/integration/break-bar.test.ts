/**
 * tests/sim/integration/break-bar.test.ts — backlog item break-bar-wiring (P1).
 *
 * Proves the Sundered Twins' shared Break bar (BIBLE.md Sec 9 -- the boss's add pair,
 * `content/data/enemies/hollow-warden.json`'s `hollowWarden.boss`/`twinA`/`twinB`, NOT
 * content/data/enemies/pair.json's unrelated "Pair" trash pull) works end to end through the REAL
 * content registry (`loadContent()`) and REAL sim: `createRun` spawns the party plus a hand-built
 * Twins encounter pull (content's `DungeonDef` has no "boss" entry in its `pulls` list -- `boss`
 * is a separate top-level field `toSimDungeon` never expands -- so this pull is authored directly
 * from the real `EnemyDef`s, exactly as content.test.ts already authors its own local
 * `advanceTick` for a similar sim/index.ts gap).
 *
 * `sim/index.ts`'s `stepRun` does not yet thread commands/abilityTags into `stepEncounter`'s new
 * `breakBarWiring` argument or apply `effectPatches` after combat resolves (that one-line hook is
 * out of this lane's scope -- see sim/encounter/index.ts's file docstring), so this test mirrors
 * `stepRun`'s own documented tick order with a local `advanceTick`, same as
 * tests/sim/integration/content.test.ts does for its own sim/index.ts gap.
 */
import { describe, expect, it } from "vitest";
import { createRun, type Run, type RunContent, type PartyMemberSpec } from "@sim/index";
import type { ActorId, ActorState, SimState } from "@sim/core/state";
import { step as coreStep } from "@sim/core/tick";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import { applyEffectInstance } from "@sim/combat/effects";
import { makeRules, type RulesConfig } from "@sim/combat/rules";
import type { StepRules } from "@sim/core/tick";
import {
  stepEncounter,
  spawnPull,
  type EncounterState,
  type DungeonDef as SimDungeonDef,
  type EnemyScript,
  type BreakBarWiring,
} from "@sim/encounter/index";
import type { BreakBarSource } from "@sim/encounter/breakbar";
import { loadContent } from "@content/index";

const SEED = 424242;
const TICKS = 30;
const WARDEN_A = 1; // warden.shieldBash (interrupt + control) at twinA
const CHANTER_A = 2; // chanter.rebuke (interrupt) at twinB
const CINDER_A = 3; // cinder.quench (interrupt) at twinA
const BOSS = 2000;
const TWIN_A = 2001;
const TWIN_B = 2002;

interface Ctx {
  rules: StepRules;
  dungeon: SimDungeonDef;
  enemyScripts: Record<ActorId, EnemyScript>;
  breakBarSources: Record<string, BreakBarSource>;
  abilityTags: Record<string, string[] | undefined>;
  effects: RunContent["effects"];
}

function buildRun(): { run: Run; ctx: Ctx } {
  const reg = loadContent();
  const abilities = Object.fromEntries(reg.abilities);
  const effects = Object.fromEntries(reg.effects);
  const bounds = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };

  const bossDef = reg.enemies.get("hollowWarden.boss")!;
  const twinADef = reg.enemies.get("hollowWarden.twinA")!;
  const twinBDef = reg.enemies.get("hollowWarden.twinB")!;
  const barDef = bossDef.breakBars!.find((b) => b.id === "twinsBreak")!;

  const dungeon: SimDungeonDef = {
    id: "hollow-vault",
    pulls: [
      {
        id: "sundered-twins",
        spawns: [
          { actorId: BOSS, classId: bossDef.id, pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: bossDef.maxHealth },
          { actorId: TWIN_A, classId: twinADef.id, pos: { x: 500, y: 0, z: 0 }, yaw: 0, health: twinADef.maxHealth },
          { actorId: TWIN_B, classId: twinBDef.id, pos: { x: -500, y: 0, z: 0 }, yaw: 0, health: twinBDef.maxHealth },
        ],
      },
    ],
  };
  const enemyScripts: Record<ActorId, EnemyScript> = { [BOSS]: bossDef.script, [TWIN_A]: twinADef.script, [TWIN_B]: twinBDef.script };

  const breakBarSources: Record<string, BreakBarSource> = {
    [barDef.id]: {
      id: barDef.id,
      actorIds: [TWIN_A, TWIN_B],
      max: barDef.max,
      drainPerInterrupt: barDef.drainPerInterrupt,
      drainPerControl: barDef.drainPerControl,
      onEmpty: barDef.onEmpty.flatMap((op) =>
        op.op === "applyEffect" ? [{ effectId: op.effectId, durationTicks: op.durationTicks, stacks: op.stacks }] : []
      ),
    },
  };

  const abilityTags: Record<string, string[] | undefined> = {};
  for (const [id, def] of reg.abilities) abilityTags[id] = def.tags;

  const party: PartyMemberSpec[] = [
    { id: WARDEN_A, classId: "warden", role: "tank", pos: { x: 400, y: 0, z: 100 }, maxHealth: 2000, maxResource: 150 },
    { id: CHANTER_A, classId: "chanter", role: "healer", pos: { x: -400, y: 0, z: 100 }, maxHealth: 2000, maxResource: 1 },
    { id: CINDER_A, classId: "cinder", role: "damage", pos: { x: 400, y: 0, z: -100 }, maxHealth: 2000, maxResource: 150 },
  ];

  const runContent: RunContent = { abilities, effects, lockoutTicks: 80, baseSpeedMmPerTick: 180, actorRadiusMm: 400, bounds, enemyScripts };
  const run = createRun(SEED, party, dungeon, runContent);
  const rulesConfig: RulesConfig = { abilities, effects, lockoutTicks: 80, baseSpeedMmPerTick: 180, actorRadiusMm: 400, bounds };
  const rules = makeRules(rulesConfig);

  return { run, ctx: { rules, dungeon, enemyScripts, breakBarSources, abilityTags, effects } };
}

function mergeActors(state: SimState, spawned: ActorState[]): SimState {
  if (spawned.length === 0) return state;
  return { ...state, actors: { ...state.actors, ...Object.fromEntries(spawned.map((a) => [a.id, a])) } };
}

/** Mirrors sim/index.ts's stepRun tick order, plus the break-bar wiring hook it does not yet call
 * -- see file docstring. */
function advanceTick(run: Run, ctx: Ctx, commands: Command[], events: SimEvent[]): Run {
  const emit = (e: SimEvent): void => {
    events.push(e);
  };
  const wiring: BreakBarWiring = { sources: ctx.breakBarSources, commands, abilityTags: ctx.abilityTags };
  const encResult = stepEncounter(run.state, run.encounter, ctx.dungeon, ctx.enemyScripts, emit, wiring);
  const stepped = coreStep(run.state, [...commands, ...encResult.commands], ctx.rules);
  events.push(...stepped.events);

  let nextState = stepped.state;
  for (const patch of encResult.effectPatches) {
    const actor = nextState.actors[patch.actorId];
    const effDef = ctx.effects[patch.effectId];
    if (!actor || !effDef) continue;
    const patched: ActorState = { ...actor, effects: [...actor.effects] };
    applyEffectInstance(patched, effDef, BOSS, patch.durationTicks, patch.stacks, nextState.tick);
    nextState = { ...nextState, actors: { ...nextState.actors, [patch.actorId]: patched } };
  }

  let nextEncounter: EncounterState = encResult.encounter;
  if (nextEncounter.pullIndex !== run.encounter.pullIndex) {
    nextState = mergeActors(nextState, spawnPull(ctx.dungeon, nextEncounter.pullIndex, nextState.tick, emit));
  }
  return { state: nextState, encounter: nextEncounter, dungeon: run.dungeon, content: run.content };
}

function scriptedCommands(): Command[] {
  return [
    { tick: 2, actorId: WARDEN_A, kind: "cast", abilityId: "warden.shieldBash", targetId: TWIN_A },
    { tick: 4, actorId: CHANTER_A, kind: "cast", abilityId: "chanter.rebuke", targetId: TWIN_B },
    { tick: 6, actorId: CINDER_A, kind: "cast", abilityId: "cinder.quench", targetId: TWIN_A },
  ];
}

function runScenario(commands: Command[]): { run: Run; events: SimEvent[] } {
  const { run, ctx } = buildRun();
  const events: SimEvent[] = [];
  let current = run;
  while (current.state.tick < TICKS) {
    const tick = current.state.tick;
    current = advanceTick(current, ctx, commands.filter((c) => c.tick === tick), events);
  }
  return { run: current, events };
}

describe("Sundered Twins break bar (integration)", () => {
  it("drains the shared bar off interrupt/control-tagged casts and stuns both twins when it empties", () => {
    const { run, events } = runScenario(scriptedCommands());

    const staggerEvents = events.filter((e) => e.type === "stagger");
    expect(staggerEvents).toHaveLength(1);
    expect(staggerEvents[0]).toMatchObject({ barId: "twinsBreak", actorIds: [TWIN_A, TWIN_B] });
    expect(run.encounter.breakBars.twinsBreak?.current).toBe(0);

    const twinA = run.state.actors[TWIN_A]!;
    const twinB = run.state.actors[TWIN_B]!;
    expect(twinA.effects.some((e) => e.effectId === "stunned")).toBe(true);
    expect(twinB.effects.some((e) => e.effectId === "stunned")).toBe(true);
  });

  it("never drains the bar with zero tagged casts submitted (no false positives)", () => {
    const { run, events } = runScenario([]);
    expect(events.some((e) => e.type === "stagger")).toBe(false);
    expect(run.encounter.breakBars.twinsBreak?.current).toBe(100);
  });
});
