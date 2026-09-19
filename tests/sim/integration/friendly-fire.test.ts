/**
 * tests/sim/integration/friendly-fire.test.ts
 * P0 regression: sim/combat/targeting.ts's resolveTargets resolved circle/cone/line shapes
 * against every actor in range with no faction filter, so a damage/debuff ability could burn the
 * caster's own party (or a heal/buff ability could land on enemies). Drives the REAL public path
 * -- sim/index.ts's createRun/stepRun with the real content registry (loadContent) -- exactly
 * like tests/sim/integration/combos.test.ts, so a regression here can't hide behind a hand-built
 * RulesConfig.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import type { SimState } from "@sim/core/state";
import type { DungeonDef as SimDungeonDef } from "@sim/encounter/index";
import { loadContent, toComboTables, toSimDungeon } from "@content/index";
import { createActor } from "@sim/combat/actors";
import { makeRules, type RulesConfig } from "@sim/combat/rules";

const SEED = 90210;

const WARDEN_A = 501;
const CINDER_A = 502;
const CHANTER_A = 503;

// toSimDungeon assigns actor ids sequentially starting at 1000, in the "choir" pull's group order
// (content/data/dungeons/hollow-vault.json): 2x choir.melee (spawn {x:0,z:1000}) then 1x
// choir.lieutenant (spawn {x:0,z:-1000}).
const ENEMY_MELEE_1 = 1000;
const ENEMY_MELEE_2 = 1001;
const ENEMY_LIEUTENANT = 1002;

/** Loads the real registry and wires a run against Hollow Vault's first ("choir") pull, with the
 * given party layout. Mirrors tests/sim/integration/combos.test.ts's buildRun. */
function buildRun(party: PartyMemberSpec[]): { run: Run } {
  const reg = loadContent();
  const abilities = Object.fromEntries(reg.abilities);
  const effects = Object.fromEntries(reg.effects);
  const { combos, fieldCombos } = toComboTables(reg.combos, [...reg.classes.keys()]);

  const dungeonDef = reg.dungeons.get("hollow-vault")!;
  const expanded = toSimDungeon(dungeonDef, reg.enemies, 1000);
  const dungeon: SimDungeonDef = { id: expanded.dungeon.id, pulls: [expanded.dungeon.pulls[0]!] };

  const bounds = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };

  const content: RunContent = {
    abilities,
    effects,
    lockoutTicks: 80,
    baseSpeedMmPerTick: 180,
    actorRadiusMm: 400,
    bounds,
    enemyScripts: expanded.enemyScripts,
    combos,
    fieldCombos,
  };

  const run = createRun(SEED, party, dungeon, content);
  return { run };
}

function runTicks(run: Run, commands: Command[], ticks: number): SimEvent[] {
  const events: SimEvent[] = [];
  let current = run;
  while (current.state.tick < ticks) {
    const tick = current.state.tick;
    const thisTick = commands.filter((c) => c.tick === tick);
    const stepped = stepRun(current, thisTick);
    current = stepped.run;
    events.push(...stepped.events);
  }
  return events;
}

describe("cinderfield (circle shape) respects faction", () => {
  it("burns the enemy pull, never the caster's own party", () => {
    // The whole party clustered directly on top of the melee pair's spawn point (0, 1000), well
    // within cinderfield's 2500mm radius of BOTH melee enemies (distance 0) and the lieutenant at
    // (0, -1000) (distance 2000mm < 2500mm) -- every actor in the encounter is in range.
    const party: PartyMemberSpec[] = [
      { id: WARDEN_A, classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 1000 }, maxHealth: 2000, maxResource: 150 },
      { id: CINDER_A, classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 1000 }, maxHealth: 2000, maxResource: 150 },
      { id: CHANTER_A, classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 1000 }, maxHealth: 2000, maxResource: 1 },
    ];
    const { run } = buildRun(party);
    const commands: Command[] = [
      // cinder.cinderfield: castTicks 20, circle radius 2500 centred on CINDER_A. No targetId
      // needed -- the shape is not "target".
      { tick: 5, actorId: CINDER_A, kind: "cast", abilityId: "cinder.cinderfield" },
    ];

    const events = runTicks(run, commands, 100);

    const playerIds = new Set([WARDEN_A, CINDER_A, CHANTER_A]);
    const enemyIds = new Set([ENEMY_MELEE_1, ENEMY_MELEE_2, ENEMY_LIEUTENANT]);

    // Scoped to damage the PARTY caused (sourceId is a party member): the choir pull fights back
    // with its own choir.meleeSwing once in range (a real, unrelated combat mechanic, not this
    // defect), so "any damage event targeting a player" is not itself proof of friendly fire --
    // only cinderfield's own burning DoT landing back on its casters would be. cinderfield's
    // "burning" effect has no other source, so every party-sourced damage event here is either
    // that DoT or (post-fix) nothing at all.
    const partyDamage = events.filter((e) => e.type === "damage" && playerIds.has(e.sourceId as number));
    const partyDamageToPlayers = partyDamage.filter((e) => playerIds.has(e.targetId as number));
    const partyDamageToEnemies = partyDamage.filter((e) => enemyIds.has(e.targetId as number));

    expect(partyDamageToPlayers).toHaveLength(0);
    expect(partyDamageToEnemies.length).toBeGreaterThan(0);
  });
});

describe("Bulwark (floor Window, friendly circle) still resolves to allies and a Payoff can still close it", () => {
  // Drives sim/combat/rules.ts's makeRules directly (one persistent StepRules closure reused
  // across ticks, exactly like tests/sim/combat/windows.test.ts's own floor-Window suite) instead
  // of sim/index.ts's createRun/stepRun: stepRun rebuilds a *fresh* RulesConfig/makeRules call
  // (and therefore a fresh, empty floor-patch registry) on every single tick, so a floor Window
  // opened on one tick can never survive to a later tick through that composition root -- a real,
  // pre-existing gap in sim/index.ts (out of this lane's edit scope; see the PR body) unrelated to
  // this defect. Real content (abilities/effects/combos via loadContent) is still used throughout.
  it("braces the allies standing in it, and a Payoff standing in the same patch still fires the field combo", () => {
    const reg = loadContent();
    const abilities = Object.fromEntries(reg.abilities);
    const effects = Object.fromEntries(reg.effects);
    const { combos, fieldCombos } = toComboTables(reg.combos, [...reg.classes.keys()]);

    const cfg: RulesConfig = {
      abilities,
      effects,
      lockoutTicks: 80,
      baseSpeedMmPerTick: 180,
      actorRadiusMm: 400,
      bounds: { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 },
      combos,
      fieldCombos,
    };
    const rules = makeRules(cfg);

    // Warden/Chanter/Cinder clustered ~800mm apart (clear of each other's 400mm actorRadiusMm) and
    // an enemy target for cinder.detonate's required "target" shape (unaffected by this fix).
    const warden = createActor({ id: WARDEN_A, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 2000, maxResource: 150 });
    const cinder = createActor({ id: CINDER_A, kind: "player", classId: "cinder", role: "damage", pos: { x: 800, y: 0, z: 0 }, maxHealth: 2000, maxResource: 150 });
    const chanter = createActor({ id: CHANTER_A, kind: "player", classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 800 }, maxHealth: 2000, maxResource: 1 });
    const enemy = createActor({ id: ENEMY_MELEE_1, kind: "enemy", classId: "choir.melee", role: "none", pos: { x: 5000, y: 0, z: 0 }, maxHealth: 2000, maxResource: 0 });
    const state: SimState = {
      tick: 0,
      seed: 1,
      prng: [0, 0, 0, 0],
      actors: { [warden.id]: warden, [cinder.id]: cinder, [chanter.id]: chanter, [enemy.id]: enemy },
      runTimerTicks: 0,
      phase: "combat",
      log: [],
    };
    const events: SimEvent[] = [];
    const emit = (e: SimEvent): void => {
      events.push(e);
    };
    function tick(cmds: Command[]): void {
      rules(state, cmds, emit);
      state.tick += 1;
    }

    // warden.bulwark: castTicks 20, circle radius 2500 centred on WARDEN_A at cast-completion time
    // -- covers Cinder (800mm) and Chanter (800mm), both allies.
    tick([{ tick: state.tick, actorId: WARDEN_A, kind: "cast", abilityId: "warden.bulwark" }]);
    for (let i = 0; i < 20; i += 1) tick([]); // castTicks 20 -- the 20th call lands on castEndsTick
    expect(events.some((e) => e.type === "window_opened" && e.windowId === "bulwark")).toBe(true);

    const hasEffect = (actorId: number, effectId: string): boolean =>
      state.actors[actorId]?.effects.some((e) => e.effectId === effectId) ?? false;

    // Bulwark's own applyEffect ("braced") reaches the caster and the allies standing with him...
    expect(hasEffect(WARDEN_A, "braced")).toBe(true);
    expect(hasEffect(CINDER_A, "braced")).toBe(true);
    expect(hasEffect(CHANTER_A, "braced")).toBe(true);
    // ...but never an enemy, even one that would have been in range.
    expect(hasEffect(ENEMY_MELEE_1, "braced")).toBe(false);

    // cinder.detonate (Payoff, "target" shape -- unaffected by this fix) targets the enemy, finds
    // no Window there, and falls back to the floor Window Cinder is still standing in.
    tick([{ tick: state.tick, actorId: CINDER_A, kind: "cast", abilityId: "cinder.detonate", targetId: ENEMY_MELEE_1 }]);

    // The floor Window itself still works: a Payoff from another class standing in the patch
    // still closes it and fires the "bulwark-field" combo (design/BIBLE.md §5's Floor Windows).
    const comboEvents = events.filter((e) => e.type === "combo");
    expect(comboEvents).toContainEqual(expect.objectContaining({ comboId: "bulwark-field", field: true }));
  });
});
