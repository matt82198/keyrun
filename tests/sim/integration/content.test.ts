/**
 * tests/sim/integration/content.test.ts
 * Loads ALL of content/data through the real sim, end to end: every ability/effect/combo content
 * defines, a 5-actor party spanning the three real classes (warden/cinder/chanter -- BIBLE.md
 * names three classes, not four; see this lane's final report), and one real enemy pull from the
 * Hollow Vault dungeon (the "choir" pull, content/data/dungeons/hollow-vault.json's first entry).
 *
 * `sim/index.ts`'s `RunContent`/`createRun` do not carry a `combos`/`fieldCombos` field through to
 * `sim/combat/rules.ts`'s `RulesConfig` (see `rulesConfigOf` in sim/index.ts -- it reads only
 * abilities/effects/lockoutTicks/baseSpeedMmPerTick/actorRadiusMm/bounds/enemyScripts/keyLevel/
 * parTicks off `RunContent`), so a run driven through `stepRun` can never fire a Combo today. This
 * is a real sim gap (flagged in this lane's final report, not fixed here -- sim/** is out of
 * scope): `createRun` is still used for its party/first-pull spawning convenience, but ticks are
 * advanced with a small local `advanceTick` that mirrors `stepRun`'s own documented tick order
 * (stepEncounter -> merge enemy + scripted commands -> sim/core's `step` with a combos-aware
 * `RulesConfig` -> spawn the next pull if one just cleared) using a `makeRules` config built with
 * `content/index.ts`'s `toComboTables`.
 *
 * The scripted command stream below casts each of the three classes' Setup ability, then another
 * class's Payoff ability on the same target within the Window's duration, so all three of a
 * Setup/Payoff pair's shapes exercise real content data end to end: a damage+control combo
 * ("shatter": warden Setup -> cinder Payoff), a damage+resource combo ("forge": cinder Setup ->
 * warden Payoff), and a buff-only combo ("anthem": chanter Setup -> warden Payoff).
 */
import { describe, expect, it } from "vitest";
import { createRun, type Run, type RunContent } from "@sim/index";
import type { PartyMemberSpec } from "@sim/index";
import { hashState, type ActorId, type SimState } from "@sim/core/state";
import { step as coreStep } from "@sim/core/tick";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import { makeRules, type RulesConfig } from "@sim/combat/rules";
import type { StepRules } from "@sim/core/tick";
import {
  stepEncounter,
  spawnPull,
  type EncounterState,
  type DungeonDef as SimDungeonDef,
  type EnemyScript,
} from "@sim/encounter/index";
import { loadContent, toComboTables, toSimDungeon } from "@content/index";

const SEED = 909090;
const TICKS = 220;

// Party actor ids.
const WARDEN_A = 101; // casts warden.strike (Setup:sundered), then warden.reckoning (Payoff)
const CINDER_A = 102; // casts cinder.detonate (Payoff)
const CINDER_B = 103; // casts cinder.flare (Setup:kindled)
const WARDEN_B = 104; // casts warden.reckoning (Payoff)
const CHANTER_A = 105; // casts chanter.cadence (Setup:attuned)

function buildRun(): { run: Run; rules: StepRules; dungeon: SimDungeonDef; enemyScripts: Record<ActorId, EnemyScript> } {
  const reg = loadContent();
  const abilities = Object.fromEntries(reg.abilities);
  const effects = Object.fromEntries(reg.effects);
  const { combos, fieldCombos } = toComboTables(reg.combos, [...reg.classes.keys()]);
  const bounds = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };

  const dungeonDef = reg.dungeons.get("hollow-vault")!;
  const expanded = toSimDungeon(dungeonDef, reg.enemies, 1000);
  // "one enemy pull from Hollow Vault": keep only the first pull (the "choir" pull) so createRun
  // spawns exactly that, and no later pull is ever reachable during this scenario.
  const dungeon: SimDungeonDef = { id: expanded.dungeon.id, pulls: [expanded.dungeon.pulls[0]!] };

  const party: PartyMemberSpec[] = [
    { id: WARDEN_A, classId: "warden", role: "tank", pos: { x: -500, y: 0, z: 200 }, maxHealth: 2000, maxResource: 150 },
    { id: CINDER_A, classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 200 }, maxHealth: 2000, maxResource: 150 },
    { id: CINDER_B, classId: "cinder", role: "damage", pos: { x: 500, y: 0, z: 200 }, maxHealth: 2000, maxResource: 150 },
    { id: WARDEN_B, classId: "warden", role: "damage", pos: { x: -250, y: 0, z: -200 }, maxHealth: 2000, maxResource: 150 },
    { id: CHANTER_A, classId: "chanter", role: "healer", pos: { x: 250, y: 0, z: -200 }, maxHealth: 2000, maxResource: 1 },
  ];

  const runContent: RunContent = {
    abilities,
    effects,
    lockoutTicks: 80,
    baseSpeedMmPerTick: 180,
    actorRadiusMm: 400,
    bounds,
    enemyScripts: expanded.enemyScripts,
  };

  const run = createRun(SEED, party, dungeon, runContent);

  const rulesConfig: RulesConfig = {
    abilities,
    effects,
    lockoutTicks: 80,
    baseSpeedMmPerTick: 180,
    actorRadiusMm: 400,
    bounds,
    combos,
    fieldCombos,
  };
  const rules = makeRules(rulesConfig);

  return { run, rules, dungeon, enemyScripts: expanded.enemyScripts };
}

function mergeActors(state: SimState, spawned: SimState["actors"][ActorId][]): SimState {
  if (spawned.length === 0) return state;
  const actors = { ...state.actors };
  for (const actor of spawned) actors[actor.id] = actor;
  return { ...state, actors };
}

/** Mirrors sim/index.ts's stepRun tick order exactly, but with a combos-aware RulesConfig -- see
 * the file docstring for why this test cannot use stepRun directly. */
function advanceTick(
  run: Run,
  rules: StepRules,
  dungeon: SimDungeonDef,
  enemyScripts: Record<ActorId, EnemyScript>,
  commands: Command[],
  events: SimEvent[],
): Run {
  const emit = (e: SimEvent): void => {
    events.push(e);
  };
  const encResult = stepEncounter(run.state, run.encounter, dungeon, enemyScripts, emit);
  const merged = [...commands, ...encResult.commands];
  const stepped = coreStep(run.state, merged, rules);
  events.push(...stepped.events);

  let nextState = stepped.state;
  let nextEncounter: EncounterState = encResult.encounter;
  if (nextEncounter.pullIndex !== run.encounter.pullIndex) {
    const spawned = spawnPull(dungeon, nextEncounter.pullIndex, nextState.tick, emit);
    nextState = mergeActors(nextState, spawned);
  }

  // This local mirror doesn't touch sim/index.ts's own run-end policy (see file docstring) --
  // runEnd just passes through unchanged.
  return { state: nextState, encounter: nextEncounter, dungeon: run.dungeon, content: run.content, runEnd: run.runEnd };
}

// The first choir.melee spawn (toSimDungeon assigns actor ids sequentially, group order matching
// content/data/dungeons/hollow-vault.json's "choir" pull: 2x choir.melee then 1x choir.lieutenant).
const ENEMY_1 = 1000;
const ENEMY_2 = 1001;

function scriptedCommands(): Command[] {
  return [
    // warden Setup -> cinder Payoff: "shatter" (damage + stun).
    { tick: 20, actorId: WARDEN_A, kind: "cast", abilityId: "warden.strike", targetId: ENEMY_1 },
    { tick: 40, actorId: CINDER_A, kind: "cast", abilityId: "cinder.detonate", targetId: ENEMY_1 },
    // cinder Setup -> warden Payoff: "forge" (damage + resource).
    { tick: 20, actorId: CINDER_B, kind: "cast", abilityId: "cinder.flare", targetId: ENEMY_2 },
    { tick: 60, actorId: WARDEN_B, kind: "cast", abilityId: "warden.reckoning", targetId: ENEMY_2 },
    // chanter Setup -> warden Payoff: "anthem" (buff only, no damage op). chanter.cadence's
    // castTicks:20 completes at tick 40, opening "attuned" for durationTicks:60 (endsTick 100,
    // exclusive -- see windows.ts's `findOpenWindow`'s `endsTick > now`); tick 90 closes it in time.
    { tick: 20, actorId: CHANTER_A, kind: "cast", abilityId: "chanter.cadence", targetId: ENEMY_1 },
    { tick: 90, actorId: WARDEN_A, kind: "cast", abilityId: "warden.reckoning", targetId: ENEMY_1 },
  ];
}

function runScenario(): { run: Run; events: SimEvent[] } {
  const { run, rules, dungeon, enemyScripts } = buildRun();
  const commands = scriptedCommands();
  const events: SimEvent[] = [];
  let current = run;
  while (current.state.tick < TICKS) {
    const tick = current.state.tick;
    const thisTick = commands.filter((c) => c.tick === tick);
    current = advanceTick(current, rules, dungeon, enemyScripts, thisTick, events);
  }
  return { run: current, events };
}

describe("content loaded through the real sim (integration)", () => {
  it("runs a full pull (>= 200 ticks) of real content without throwing", () => {
    expect(() => runScenario()).not.toThrow();
  });

  it("fires at least one combo event from the scripted Setup/Payoff pairs", () => {
    const { events } = runScenario();
    const comboEvents = events.filter((e) => e.type === "combo");
    expect(comboEvents.length).toBeGreaterThanOrEqual(1);
    const comboIds = comboEvents.map((e) => e.comboId);
    expect(comboIds).toEqual(expect.arrayContaining(["shatter", "forge", "anthem"]));
  });

  it("produces the same final state hash on two independent runs (determinism)", () => {
    const a = runScenario();
    const b = runScenario();
    expect(hashState(a.run.state)).toBe(hashState(b.run.state));
  });

  it("never produces NaN or out-of-range health on any actor", () => {
    const { run } = runScenario();
    for (const actor of Object.values(run.state.actors)) {
      expect(Number.isNaN(actor.health)).toBe(false);
      expect(actor.health).toBeGreaterThanOrEqual(0);
      expect(actor.health).toBeLessThanOrEqual(actor.maxHealth);
    }
  });
});
