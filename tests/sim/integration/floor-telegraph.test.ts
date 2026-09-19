/**
 * tests/sim/integration/floor-telegraph.test.ts
 *
 * floor-patch-visibility, public path: sim/index.ts's real createRun/stepRun, the real content
 * registry (loadContent), and sim/combat/floor-telegraphs.ts's deriveFloorTelegraphs -- proving a
 * real cast produces a real, correctly-encoded bots Telegraph, not just a unit-level shape check.
 * Mirrors tests/sim/integration/friendly-fire.test.ts's buildRun/runTicks harness.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimState } from "@sim/core/state";
import type { DungeonDef as SimDungeonDef } from "@sim/encounter/index";
import { loadContent, toComboTables, toSimDungeon } from "@content/index";
import { deriveFloorTelegraphs, FLOOR_PATCH_PREFIX } from "../../../sim/combat/floor-telegraphs";

const SEED = 13579;
const WARDEN_A = 601;
const CINDER_A = 602;
const CHANTER_A = 603;

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

function runTicksCollectingState(run: Run, commands: Command[], ticks: number): SimState[] {
  const states: SimState[] = [];
  let current = run;
  while (current.state.tick < ticks) {
    const tick = current.state.tick;
    const thisTick = commands.filter((c) => c.tick === tick);
    const stepped = stepRun(current, thisTick);
    current = stepped.run;
    states.push(current.state);
  }
  return states;
}

const party: PartyMemberSpec[] = [
  { id: WARDEN_A, classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 2000, maxResource: 150 },
  { id: CINDER_A, classId: "cinder", role: "damage", pos: { x: 800, y: 0, z: 0 }, maxHealth: 2000, maxResource: 150 },
  { id: CHANTER_A, classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 800 }, maxHealth: 2000, maxResource: 1 },
];

describe("cinder casts cinderfield -> a friendly floor: telegraph appears with the right radius/faction", () => {
  it("shows up in SimState.floorWindows and deriveFloorTelegraphs once the cast completes", () => {
    const { run } = buildRun(party);
    // cinder.cinderfield: castTicks 20, floor Window radiusMm 1800, setupClass "cinder".
    const commands: Command[] = [{ tick: 5, actorId: CINDER_A, kind: "cast", abilityId: "cinder.cinderfield" }];

    const states = runTicksCollectingState(run, commands, 40);
    const afterCast = states[states.length - 1]!;

    const telegraphs = deriveFloorTelegraphs(afterCast);
    const cinderfield = telegraphs.find((t) => t.id.startsWith(`${FLOOR_PATCH_PREFIX}window:cinderfield:`));
    expect(cinderfield).toBeDefined();
    expect(cinderfield?.id).toBe("floor:window:cinderfield:cinder");
    expect(cinderfield?.radius).toBe(1800);
    expect(cinderfield?.danger).toBe(false);
    expect(cinderfield?.shape).toBe("circle");
  });
});

describe("warden opens bulwark -> a friendly floor: telegraph appears with the right radius/faction", () => {
  it("shows up in SimState.floorWindows and deriveFloorTelegraphs once the cast completes", () => {
    const { run } = buildRun(party);
    // warden.bulwark: castTicks 20, floor Window radiusMm 2500, setupClass "warden".
    const commands: Command[] = [{ tick: 5, actorId: WARDEN_A, kind: "cast", abilityId: "warden.bulwark" }];

    const states = runTicksCollectingState(run, commands, 40);
    const afterCast = states[states.length - 1]!;

    const telegraphs = deriveFloorTelegraphs(afterCast);
    const bulwark = telegraphs.find((t) => t.id.startsWith(`${FLOOR_PATCH_PREFIX}window:bulwark:`));
    expect(bulwark).toBeDefined();
    expect(bulwark?.id).toBe("floor:window:bulwark:warden");
    expect(bulwark?.radius).toBe(2500);
    expect(bulwark?.danger).toBe(false);
  });

  it("survives across ticks (PR #44's SimState.floorWindows fix) until it expires", () => {
    const { run } = buildRun(party);
    const commands: Command[] = [{ tick: 5, actorId: WARDEN_A, kind: "cast", abilityId: "warden.bulwark" }];

    // bulwark opens at tick 25 (5 + castTicks 20) with durationTicks 160 -> endsTick 185.
    const states = runTicksCollectingState(run, commands, 60);
    const stillOpen = states[states.length - 1]!;
    expect(deriveFloorTelegraphs(stillOpen).some((t) => t.id === "floor:window:bulwark:warden")).toBe(true);
  });
});
