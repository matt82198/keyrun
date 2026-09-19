/**
 * resolveTargets (sim/combat/targeting.ts): the single shape-to-actor-list resolver shared by
 * casting.ts's completeCast and windows.ts's applyWindowsAndCombos. Covers the four shapes an
 * ability can carry (self, target, cone, circle-at-a-floor-point i.e. centered on the caster),
 * then proves the cast pipeline and the window pipeline resolve an identical target set for the
 * same ability + state, since both now call this one function instead of two hand-duplicated
 * copies.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef, AbilityShape, EffectOp } from "../../../sim/combat/abilities";
import { completeCast, type CastContext } from "../../../sim/combat/casting";
import { applyWindowsAndCombos, createWindowsContext } from "../../../sim/combat/windows";
import { resolveTargets } from "../../../sim/combat/targeting";
import type { SimEvent, SimState } from "../../../sim/combat/types";

function makeState(actors: ReturnType<typeof createActor>[]): SimState {
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

function collector(): { events: SimEvent[]; emit: (e: SimEvent) => void } {
  const events: SimEvent[] = [];
  return { events, emit: (e) => events.push(e) };
}

function defWithShape(shape: AbilityShape, effects: EffectOp[]): AbilityDef {
  return {
    id: "test_ability",
    castTicks: 0,
    gcdTicks: 0,
    cooldownTicks: 0,
    charges: 1,
    cost: { resource: "energy", amount: 0 },
    range: 5000,
    shape,
    interruptible: false,
    effects,
  };
}

const dmgOp: EffectOp = { op: "damage", amount: 10, school: "physical" };

describe("resolveTargets shapes", () => {
  it("self resolves to the caster only", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const def = defWithShape({ kind: "self" }, [dmgOp]);
    const state = makeState([caster]);
    expect(resolveTargets(state, caster, def)).toEqual([caster]);
  });

  it("target resolves to the caster's current targetId", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 100, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    caster.targetId = 2;
    const def = defWithShape({ kind: "target" }, [dmgOp]);
    const state = makeState([caster, target]);
    expect(resolveTargets(state, caster, def)).toEqual([target]);
  });

  it("target resolves to nothing when no targetId is set", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const def = defWithShape({ kind: "target" }, [dmgOp]);
    const state = makeState([caster]);
    expect(resolveTargets(state, caster, def)).toEqual([]);
  });

  it("cone resolves to actors ahead within the cone and excludes the caster and actors to the side", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, yaw: 0, maxHealth: 100, maxResource: 100 });
    const ahead = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 100, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const side = createActor({ id: 3, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 100 }, maxHealth: 100, maxResource: 0 });
    const def = defWithShape({ kind: "cone", angle1024: 256, radius: 200 }, [dmgOp]);
    const state = makeState([caster, ahead, side]);
    expect(resolveTargets(state, caster, def)).toEqual([ahead]);
  });

  it("circle resolves around the caster's own position (a floor point) and, for a hostile (damage) ability, excludes the caster and allies", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 500, y: 0, z: 500 }, maxHealth: 100, maxResource: 100 });
    const inRange = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 550, y: 0, z: 500 }, maxHealth: 100, maxResource: 0 });
    const allyInRange = createActor({ id: 4, kind: "player", classId: "y", role: "none", pos: { x: 540, y: 0, z: 500 }, maxHealth: 100, maxResource: 0 });
    const outOfRange = createActor({ id: 3, kind: "enemy", classId: "x", role: "none", pos: { x: 5000, y: 0, z: 5000 }, maxHealth: 100, maxResource: 0 });
    const def = defWithShape({ kind: "circle", radius: 100 }, [dmgOp]);
    const state = makeState([caster, inRange, allyInRange, outOfRange]);
    expect(resolveTargets(state, caster, def)).toEqual([inRange]);
  });

  it("circle resolves a friendly (heal) ability to the caster's own kind, including the caster, excluding enemies", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "chanter", role: "healer", pos: { x: 500, y: 0, z: 500 }, maxHealth: 100, maxResource: 100 });
    const allyInRange = createActor({ id: 2, kind: "player", classId: "y", role: "none", pos: { x: 550, y: 0, z: 500 }, maxHealth: 100, maxResource: 0 });
    const enemyInRange = createActor({ id: 3, kind: "enemy", classId: "x", role: "none", pos: { x: 540, y: 0, z: 500 }, maxHealth: 100, maxResource: 0 });
    const healOp: EffectOp = { op: "heal", amount: 10 };
    const def = defWithShape({ kind: "circle", radius: 100 }, [healOp]);
    const state = makeState([caster, allyInRange, enemyInRange]);
    expect(resolveTargets(state, caster, def)).toEqual([caster, allyInRange]);
  });
});

describe("cast pipeline and window pipeline resolve identical target sets", () => {
  it("completeCast (casting.ts) and applyWindowsAndCombos (windows.ts) hit the same cone-shaped targets", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "warden", role: "damage", pos: { x: 0, y: 0, z: 0 }, yaw: 0, maxHealth: 100, maxResource: 100 });
    const near = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 100, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const far = createActor({ id: 3, kind: "enemy", classId: "x", role: "none", pos: { x: 150, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const outsideCone = createActor({ id: 4, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 200 }, maxHealth: 100, maxResource: 0 });

    const shape: AbilityShape = { kind: "cone", angle1024: 256, radius: 200 };
    const def = defWithShape(shape, [
      dmgOp,
      { op: "window", windowId: "cleave-window", durationTicks: 60, onTarget: true, setupClass: "warden" },
    ]);

    const state = makeState([caster, near, far, outsideCone]);
    const expectedIds = resolveTargets(state, caster, def).map((a) => a.id);
    expect(expectedIds).toEqual([2, 3]);

    caster.castId = def.id;
    const castCtx: CastContext = { abilities: { [def.id]: def }, effects: {}, lockoutTicks: 0 };
    const cast = collector();
    completeCast(state, caster, castCtx, cast.emit);
    const damagedIds = cast.events
      .filter((e) => e.type === "damage")
      .map((e) => e.targetId as number)
      .sort((a, b) => a - b);
    expect(damagedIds).toEqual(expectedIds.slice().sort((a, b) => a - b));

    const winCtx = createWindowsContext({});
    const win = collector();
    applyWindowsAndCombos(state, caster, def, winCtx, win.emit);
    const opened = win.events.find((e) => e.type === "window_opened");
    expect(opened?.targetId).toBe(expectedIds[0]);
  });

  it("both pipelines agree the caster is the sole target for a self-shaped ability", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const def = defWithShape({ kind: "self" }, [
      { op: "heal", amount: 5 },
      { op: "window", windowId: "self-window", durationTicks: 60, onTarget: true, setupClass: "warden", self: true },
    ]);

    const state = makeState([caster]);
    caster.health = 90;
    caster.castId = def.id;
    const castCtx: CastContext = { abilities: { [def.id]: def }, effects: {}, lockoutTicks: 0 };
    const cast = collector();
    completeCast(state, caster, castCtx, cast.emit);
    const healed = cast.events.find((e) => e.type === "heal");
    expect(healed?.targetId).toBe(caster.id);

    const winCtx = createWindowsContext({});
    const win = collector();
    applyWindowsAndCombos(state, caster, def, winCtx, win.emit);
    const opened = win.events.find((e) => e.type === "window_opened");
    expect(opened?.targetId).toBe(caster.id);
  });
});
