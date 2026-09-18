/**
 * tests/sim/integration/combos.test.ts
 * Drives Windows and Combos (design/BIBLE.md §4, §5) through the REAL public composition root --
 * `sim/index.ts`'s `createRun`/`stepRun` -- with the real content registry (all three starter
 * classes: warden/cinder/chanter). This is deliberately narrower than
 * `tests/sim/integration/content.test.ts` (which proves content data itself is combo-shaped, via a
 * hand-built `RulesConfig` that bypasses `createRun`/`stepRun`'s own rules construction): this
 * suite exists to prove the composition root itself threads `combos`/`fieldCombos` from
 * `RunContent` into `sim/combat/rules.ts`'s `RulesConfig` (see `rulesConfigOf` in sim/index.ts),
 * which is a distinct thing from content being combo-shaped -- a caller can have perfect content
 * and still never see a Combo if `createRun`/`stepRun` drop the tables on the floor.
 *
 * Scenario: a warden opens the "sundered" Window on an enemy with `warden.strike` (a Setup, tags:
 * ["setup:sundered"], instant), then a cinder closes it with `cinder.detonate` (a Payoff, tags:
 * ["payoff"], instant) a few ticks later, well inside `sundered`'s 60-tick duration -- content/data/
 * combos.json's "shatter" entry (setupClass: warden, payoffClass: cinder) should fire.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import type { DungeonDef as SimDungeonDef } from "@sim/encounter/index";
import { loadContent, toComboTables, toSimDungeon } from "@content/index";

const SEED = 13371337;
const TICKS = 60;

const WARDEN_A = 201; // opens "sundered" on ENEMY_1 via warden.strike (Setup)
const WARDEN_B = 202; // second warden, used only by the self-combo-denied scenario
const CINDER_A = 203; // closes "sundered" via cinder.detonate (Payoff) -> "shatter"
const CHANTER_A = 204; // present so the run exercises all three real starter classes

// toSimDungeon assigns actor ids sequentially starting at 1000, in the "choir" pull's group
// order (content/data/dungeons/hollow-vault.json): 2x choir.melee then 1x choir.lieutenant.
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
  // ever reachable during this scenario's short 40-tick window.
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

describe("Windows and Combos through the real composition root (createRun/stepRun)", () => {
  it("fires a Combo when a Setup Window is closed by another class's Payoff", () => {
    const party: PartyMemberSpec[] = [
      { id: WARDEN_A, classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 200 }, maxHealth: 2000, maxResource: 150 },
      { id: CINDER_A, classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 200 }, maxHealth: 2000, maxResource: 150 },
      { id: CHANTER_A, classId: "chanter", role: "healer", pos: { x: 250, y: 0, z: -200 }, maxHealth: 2000, maxResource: 1 },
    ];
    const { run } = buildRun(party);

    // Tick values below are even by necessity, not style: sim/combat/rules.ts's makeRules also
    // advances state.tick (on top of sim/core/tick.ts's own increment), so a real stepRun() call
    // currently advances state.tick by 2, not 1 -- a separate pre-existing defect outside this
    // lane's scope (sim/combat/** is out of bounds here; see the PR body). Every existing content
    // constant (castTicks, cooldowns, window durationTicks) is already a multiple of 10 for
    // unrelated reasons, which is why this has never surfaced before.
    const commands: Command[] = [
      // warden.strike: castTicks 0, opens "sundered" (durationTicks 60) on ENEMY_1.
      { tick: 20, actorId: WARDEN_A, kind: "cast", abilityId: "warden.strike", targetId: ENEMY_1 },
      // cinder.detonate: castTicks 0, Payoff -- well inside sundered's window.
      { tick: 40, actorId: CINDER_A, kind: "cast", abilityId: "cinder.detonate", targetId: ENEMY_1 },
    ];

    const events = runTicks(run, commands, TICKS);

    const comboEvents = events.filter((e) => e.type === "combo");
    expect(comboEvents).toHaveLength(1);
    expect(comboEvents[0]).toMatchObject({
      comboId: "shatter",
      setupClass: "warden",
      payoffClass: "cinder",
      targetId: ENEMY_1,
    });
  });

  it("refuses a Payoff from the SAME class that opened the Window (self-combo denied)", () => {
    const party: PartyMemberSpec[] = [
      { id: WARDEN_A, classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 200 }, maxHealth: 2000, maxResource: 150 },
      { id: WARDEN_B, classId: "warden", role: "damage", pos: { x: 0, y: 0, z: 200 }, maxHealth: 2000, maxResource: 150 },
    ];
    const { run } = buildRun(party);

    // See the tick-value comment in the previous test: even ticks only (state.tick currently
    // advances by 2 per stepRun() call, a pre-existing sim/combat/** defect out of this lane's
    // scope).
    const commands: Command[] = [
      // warden.strike: opens "sundered" on ENEMY_1.
      { tick: 20, actorId: WARDEN_A, kind: "cast", abilityId: "warden.strike", targetId: ENEMY_1 },
      // warden.reckoning: another warden's Payoff, same setupClass -- must be denied, not closed.
      { tick: 40, actorId: WARDEN_B, kind: "cast", abilityId: "warden.reckoning", targetId: ENEMY_1 },
    ];

    const events = runTicks(run, commands, TICKS);

    expect(events.filter((e) => e.type === "combo")).toHaveLength(0);
    const denied = events.filter((e) => e.type === "combo_denied");
    expect(denied).toHaveLength(1);
    expect(denied[0]).toMatchObject({
      reason: "self_combo",
      targetId: ENEMY_1,
      payoffActorId: WARDEN_B,
    });
  });
});
