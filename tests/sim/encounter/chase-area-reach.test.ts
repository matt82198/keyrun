/**
 * tests/sim/encounter/chase-area-reach.test.ts
 *
 * MECHANICS-PUNISH G1 made hollowWarden.slam and twins.slam caster-centred 3000 mm damage circles (range 0). chase.ts
 * only counted target-shaped casts toward an enemy's engage range, so the Hollow Warden and the Twins were left with
 * no engage range at all and stood at their spawn for the whole fight: the slam drawn on the floor could only ever
 * hit a player who walked into it. A caster-centred damage area (circle or cone) is a swing that lands within its
 * radius, so it counts toward the engage range with reach = its radius. An area with no damage op (a stack call, a
 * death bloom Window) is not a swing and still does not count.
 *
 * Usage: `npx vitest run tests/sim/encounter/chase-area-reach.test.ts`
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import type { ActorState, SimState } from "../../../sim/encounter/types";
import type { EnemyScript } from "../../../sim/encounter/scripts";
import { CHASE_MARGIN_MM, chaseCommands, engageRangeMm } from "../../../sim/encounter/chase";

function ability(id: string, over: Partial<AbilityDef>): AbilityDef {
  return {
    id,
    castTicks: 10,
    gcdTicks: 0,
    cooldownTicks: 60,
    charges: 1,
    cost: { resource: "none", amount: 0 },
    range: 0,
    shape: { kind: "circle", radius: 3000 },
    interruptible: false,
    effects: [{ op: "damage", amount: 150, school: "physical" }],
    ...over,
  };
}

const SLAM = ability("slam", {});
const CLEAVE = ability("cleave", { range: 4000, shape: { kind: "cone", radius: 4000, angle1024: 200 } });
const CALL = ability("call", { shape: { kind: "circle", radius: 1500 }, effects: [{ op: "applyEffect", effectId: "mark", durationTicks: 60, stacks: 1 }] });
const SWING = ability("swing", { range: 3000, shape: { kind: "target" } });
const ABILITIES: Record<string, AbilityDef> = { slam: SLAM, cleave: CLEAVE, call: CALL, swing: SWING };

function stateWith(actors: ActorState[]): SimState {
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

function movesAt(distance: number, script: EnemyScript): number {
  const enemy = createActor({ id: 7, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
  const player = createActor({ id: 1, kind: "player", classId: "w", role: "tank", pos: { x: 0, y: 0, z: distance }, maxHealth: 1, maxResource: 0 });
  // Counts CHASE steps only (chase-tuning.test.ts's rule): since ENEMY-ENGAGE a chaser in reach still emits a
  // zero-direction move that only turns it to face its target, and that is not a step toward it.
  return chaseCommands(stateWith([enemy, player]), { 7: script }, ABILITIES)
    .filter((c) => c.kind === "move" && (c.dir.x !== 0 || c.dir.z !== 0)).length;
}

describe("chase.ts: a caster-centred damage area counts toward engage range", () => {
  it("a damage circle reaches its radius", () => {
    expect(engageRangeMm([{ at: "loop", every: 300, cast: "slam" }], ABILITIES)).toBe(3000);
  });
  it("a damage cone reaches its radius, and the shortest reach wins", () => {
    expect(engageRangeMm([{ at: "loop", every: 160, cast: "cleave" }], ABILITIES)).toBe(4000);
    expect(engageRangeMm([{ at: "loop", every: 160, cast: "cleave" }, { at: "loop", every: 300, cast: "slam" }], ABILITIES)).toBe(3000);
  });
  it("an area with no damage op does not count", () => {
    expect(engageRangeMm([{ at: "loop", every: 400, cast: "call" }], ABILITIES)).toBeNull();
    expect(engageRangeMm([{ at: "loop", every: 400, cast: "call" }, { at: "loop", every: 40, cast: "swing" }], ABILITIES)).toBe(3000);
  });
  it("an enemy whose only swing is a slam circle closes on its target", () => {
    const script: EnemyScript = [{ at: "loop", every: 300, cast: "slam" }];
    expect(movesAt(6000, script)).toBe(1);
    expect(movesAt(3000 - CHASE_MARGIN_MM, script)).toBe(0);
  });
});

describe("content: the Hollow Warden and the Twins chase to their slam reach", () => {
  const root = join(__dirname, "..", "..", "..", "content", "data");
  const defs = JSON.parse(readFileSync(join(root, "abilities", "enemies.json"), "utf8")) as AbilityDef[];
  const abilities: Record<string, AbilityDef> = Object.fromEntries(defs.map((d) => [d.id, d]));
  const enemies = JSON.parse(readFileSync(join(root, "enemies", "hollow-warden.json"), "utf8")) as { id: string; script?: EnemyScript }[];
  for (const id of ["hollowWarden.boss", "hollowWarden.twinA", "hollowWarden.twinB"]) {
    it(`${id} has a 3000 mm engage range`, () => {
      const def = enemies.find((e) => e.id === id);
      expect(def?.script).toBeDefined();
      expect(engageRangeMm(def!.script!, abilities)).toBe(3000);
    });
  }
});
