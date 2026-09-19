/**
 * tests/sim/integration/floor-window.test.ts
 * Drives a FLOOR Window (design/BIBLE.md §4/§5, a "window" op with onTarget:false -- e.g.
 * warden.bulwark) through the REAL public composition root -- sim/index.ts's createRun/stepRun --
 * across multiple separate stepRun calls, the way the client's sim-host worker and the server
 * actually drive a run (one stepRun call per 50ms tick, never one big batched call).
 *
 * This exists because tests/sim/integration/combos.test.ts and tests/sim/combat/windows.test.ts
 * both only prove TARGET Windows survive across ticks through stepRun (combos.test.ts) or floor
 * Windows survive within a SINGLE makeRules()/StepRules closure invocation (windows.test.ts, which
 * builds `rules` once via makeRules and calls it directly per tick -- never rebuilding it). Neither
 * proves a floor Window survives stepRun's own per-call `makeRules(...)` rebuild (sim/index.ts's
 * stepRun, "const rules = makeRules(rulesConfigOf(run.content));" runs on EVERY stepRun call): the
 * floor-patch registry (`FloorWindowRegistry`) is built via `createFloorRegistry()` as a
 * closure-local inside `makeRules`, not stored in `SimState` (see sim/combat/window-state.ts's file
 * docstring), so a fresh empty registry is built on every stepRun call and any floor Window opened
 * during one stepRun call is gone by the very next one.
 *
 * Scenario: a warden opens the floor "bulwark" Window with warden.bulwark (a floor Setup,
 * onTarget:false, radiusMm 2500, durationTicks 160) which completes its 20-tick cast at tick 40. A
 * cinder ally standing inside radiusMm of the warden's cast position then casts cinder.detonate (an
 * instant Payoff) at tick 50 -- 10 ticks after the Window opened and comfortably inside its 160-tick
 * duration -- targeting the enemy (whose own lack of a target-Window makes tryClosePayoff fall
 * through to the floor). content/data/combos.json's "bulwark-field" entry (setupClass: warden,
 * payoffClass: "*", windowId: "bulwark") should fire through separate stepRun calls at tick 20..50.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import type { DungeonDef as SimDungeonDef } from "@sim/encounter/index";
import { loadContent, toComboTables, toSimDungeon } from "@content/index";

const SEED = 24681357;
const TICKS = 60;

const WARDEN_A = 301; // opens the floor "bulwark" Window via warden.bulwark (Setup, floor patch)
const CINDER_A = 302; // closes it via cinder.detonate (Payoff) standing inside radiusMm
const CHANTER_A = 303; // present so the run exercises all three real starter classes

// toSimDungeon assigns actor ids sequentially starting at 1000, in the "choir" pull's group order
// (content/data/dungeons/hollow-vault.json): 2x choir.melee then 1x choir.lieutenant.
const ENEMY_1 = 1000;

function buildRun(party: PartyMemberSpec[]): { run: Run; dungeon: SimDungeonDef } {
  const reg = loadContent();
  const abilities = Object.fromEntries(reg.abilities);
  const effects = Object.fromEntries(reg.effects);
  const { combos, fieldCombos } = toComboTables(reg.combos, [...reg.classes.keys()]);
  const bounds = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };

  const dungeonDef = reg.dungeons.get("hollow-vault")!;
  const expanded = toSimDungeon(dungeonDef, reg.enemies, 1000);
  // Keep only the first ("choir") pull so createRun spawns exactly ENEMY_1 and nothing later is
  // ever reachable during this scenario's short window.
  const dungeon: SimDungeonDef = { id: expanded.dungeon.id, pulls: [expanded.dungeon.pulls[0]!] };

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
  return { run, dungeon };
}

// Drives stepRun ONE TICK PER CALL (never a batched multi-tick call) -- this is the part
// combos.test.ts's runTicks helper also does, and it is load-bearing here: it is exactly what
// reproduces `makeRules` being rebuilt (and the floor registry reset) between the Window's open
// and its payoff.
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

describe("Floor Windows through the real composition root (createRun/stepRun)", () => {
  it("survive across separate stepRun calls so a Payoff several ticks later still closes them", () => {
    const party: PartyMemberSpec[] = [
      { id: WARDEN_A, classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 200 }, maxHealth: 2000, maxResource: 150 },
      // Within warden.bulwark's radiusMm (2500) of the warden's cast position.
      { id: CINDER_A, classId: "cinder", role: "damage", pos: { x: 1000, y: 0, z: 200 }, maxHealth: 2000, maxResource: 150 },
      { id: CHANTER_A, classId: "chanter", role: "healer", pos: { x: 250, y: 0, z: -200 }, maxHealth: 2000, maxResource: 1 },
    ];
    const { run } = buildRun(party);

    const commands: Command[] = [
      // warden.bulwark: castTicks 20 -> completes at tick 40, opening the floor "bulwark" Window
      // (durationTicks 160, radiusMm 2500) at the warden's tick-40 position. No targetId needed
      // (shape: circle, onTarget:false).
      { tick: 20, actorId: WARDEN_A, kind: "cast", abilityId: "warden.bulwark" },
      // cinder.detonate: castTicks 0, Payoff -- 10 ticks after the Window opened, via a SEPARATE
      // stepRun call (runTicks below never batches ticks).
      { tick: 50, actorId: CINDER_A, kind: "cast", abilityId: "cinder.detonate", targetId: ENEMY_1 },
    ];

    const events = runTicks(run, commands, TICKS);

    const opened = events.filter((e) => e.type === "window_opened" && e.windowId === "bulwark");
    expect(opened).toHaveLength(1);

    const comboEvents = events.filter((e) => e.type === "combo");
    expect(comboEvents).toHaveLength(1);
    expect(comboEvents[0]).toMatchObject({
      comboId: "bulwark-field",
      setupClass: "warden",
      payoffClass: "cinder",
      field: true,
    });
  });
});
