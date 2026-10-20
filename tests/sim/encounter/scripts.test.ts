import { describe, expect, it } from "vitest";
import { stepEnemyScripts, type EnemyScript } from "../../../sim/encounter/scripts";
import type { AbilityDef } from "../../../sim/combat/abilities";
import type { ActorState, SimState } from "../../../sim/encounter/types";

function enemyActor(id: number): ActorState {
  return {
    id,
    kind: "enemy",
    classId: "boss",
    role: "none",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 1000,
    maxHealth: 1000,
    resource: 0,
    maxResource: 0,
    alive: true,
    targetId: null,
    castId: null,
    castEndsTick: null,
    castStartTick: null,
    cooldowns: {},
    gcdReadyTick: 0,
    effects: [],
    threat: {},
  };
}

function playerActor(id: number, pos: { x: number; y: number; z: number } = { x: 0, y: 0, z: 0 }): ActorState {
  return { ...enemyActor(id), kind: "player", classId: "warden", role: "tank", pos };
}

function stateAt(tick: number): SimState {
  return {
    tick,
    seed: 1,
    prng: [1, 2, 3, 4],
    actors: { 1: enemyActor(1) },
    runTimerTicks: tick,
    phase: "combat",
    log: [],
  };
}

describe("stepEnemyScripts", () => {
  const script: EnemyScript = [
    {
      at: "loop",
      every: 100,
      cast: "boss_cleave",
      telegraph: { shape: "cone", angle1024: 170, radius: 6000, leadTicks: 40 },
    },
  ];
  const scripts = { 1: script };

  it("fires the loop cast at every multiple of `every`, never at tick 0", () => {
    expect(stepEnemyScripts(stateAt(0), scripts).commands).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(99), scripts).commands).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(100), scripts).commands).toEqual([
      { tick: 100, actorId: 1, kind: "cast", abilityId: "boss_cleave" },
    ]);
    expect(stepEnemyScripts(stateAt(200), scripts).commands).toHaveLength(1);
  });

  it("emits the telegraph exactly leadTicks before the cast it precedes, with matching resolvesTick", () => {
    const before = stepEnemyScripts(stateAt(60), scripts); // 100 - 40
    expect(before.telegraphEvents).toHaveLength(1);
    expect(before.telegraphEvents[0]).toMatchObject({
      tick: 60,
      type: "telegraph",
      cast: "boss_cleave",
      resolvesTick: 100,
    });
    expect(before.commands).toHaveLength(0);

    const second = stepEnemyScripts(stateAt(160), scripts); // 200 - 40
    expect(second.telegraphEvents).toHaveLength(1);
    expect(second.telegraphEvents[0]?.resolvesTick).toBe(200);

    // one tick off in either direction: no telegraph
    expect(stepEnemyScripts(stateAt(59), scripts).telegraphEvents).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(61), scripts).telegraphEvents).toHaveLength(0);
  });

  it("never casts for a dead or missing actor", () => {
    const dead = stateAt(100);
    const actor = dead.actors[1];
    if (actor) actor.alive = false;
    expect(stepEnemyScripts(dead, scripts).commands).toHaveLength(0);

    const missing: SimState = { ...stateAt(100), actors: {} };
    expect(stepEnemyScripts(missing, scripts).commands).toHaveLength(0);
  });

  it("fires a one-shot numeric `at` step exactly once, at that tick", () => {
    const oneShot: EnemyScript = [{ at: 250, cast: "intro_roar" }];
    const scripts2 = { 1: oneShot };
    expect(stepEnemyScripts(stateAt(249), scripts2).commands).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(250), scripts2).commands).toHaveLength(1);
    expect(stepEnemyScripts(stateAt(251), scripts2).commands).toHaveLength(0);
  });

  it("fires a numeric `at` + `every` step at at, at+every, at+2*every, ...", () => {
    const offsetLoop: EnemyScript = [{ at: 600, every: 400, cast: "pair.stackCall" }];
    const scripts3 = { 1: offsetLoop };
    expect(stepEnemyScripts(stateAt(200), scripts3).commands).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(400), scripts3).commands).toHaveLength(0); // every alone would fire here; offset must suppress it
    expect(stepEnemyScripts(stateAt(600), scripts3).commands).toEqual([
      { tick: 600, actorId: 1, kind: "cast", abilityId: "pair.stackCall" },
    ]);
    expect(stepEnemyScripts(stateAt(1000), scripts3).commands).toHaveLength(1);
    expect(stepEnemyScripts(stateAt(1400), scripts3).commands).toHaveLength(1);
    expect(stepEnemyScripts(stateAt(800), scripts3).commands).toHaveLength(0);
  });

  describe("onDeath", () => {
    const deathScript: EnemyScript = [{ at: "onDeath", cast: "seed.deathBloom" }];
    const deathScripts = { 1: deathScript };

    it("never fires while the actor is alive", () => {
      const alive = stateAt(500);
      expect(stepEnemyScripts(alive, deathScripts).commands).toHaveLength(0);
    });

    it("fires once the actor is dead", () => {
      const dead = stateAt(500);
      const actor = dead.actors[1];
      if (actor) actor.alive = false;
      const result = stepEnemyScripts(dead, deathScripts);
      expect(result.commands).toEqual([{ tick: 500, actorId: 1, kind: "cast", abilityId: "seed.deathBloom" }]);
      expect(result.firedOnDeath[1]).toEqual(["seed.deathBloom"]);
    });

    it("does not re-fire once `firedOnDeath` records it (the caller's fire-once contract)", () => {
      const dead = stateAt(600);
      const actor = dead.actors[1];
      if (actor) actor.alive = false;
      const result = stepEnemyScripts(dead, deathScripts, { 1: ["seed.deathBloom"] });
      expect(result.commands).toHaveLength(0);
      expect(result.firedOnDeath[1]).toEqual(["seed.deathBloom"]);
    });

    it("emits the telegraph on the same tick the onDeath cast fires, resolving leadTicks later", () => {
      const withTelegraph: EnemyScript = [
        {
          at: "onDeath",
          cast: "seed.deathBloom",
          telegraph: { shape: "circle", radius: 2000, leadTicks: 20 },
        },
      ];
      const dead = stateAt(700);
      const actor = dead.actors[1];
      if (actor) actor.alive = false;
      const result = stepEnemyScripts(dead, { 1: withTelegraph });
      expect(result.telegraphEvents).toHaveLength(1);
      expect(result.telegraphEvents[0]).toMatchObject({
        tick: 700,
        type: "telegraph",
        cast: "seed.deathBloom",
        resolvesTick: 720,
      });
    });

    it("leaves loop/numeric steps for the same actor unaffected by a dead actor's onDeath step", () => {
      const mixed: EnemyScript = [
        { at: "loop", every: 100, cast: "seed.slam" },
        { at: "onDeath", cast: "seed.deathBloom" },
      ];
      const dead = stateAt(500);
      const actor = dead.actors[1];
      if (actor) actor.alive = false;
      const result = stepEnemyScripts(dead, { 1: mixed });
      expect(result.commands).toEqual([{ tick: 500, actorId: 1, kind: "cast", abilityId: "seed.deathBloom" }]);
    });
  });

  describe("targeting (P0 fix, sim/encounter/script-target.ts)", () => {
    const meleeScript: EnemyScript = [{ at: "loop", every: 40, cast: "melee" }];
    const MELEE: AbilityDef = {
      id: "melee",
      castTicks: 0,
      gcdTicks: 0,
      cooldownTicks: 0,
      charges: 1,
      cost: { resource: "none", amount: 0 },
      range: 3000,
      shape: { kind: "target" },
      interruptible: false,
      effects: [{ op: "damage", amount: 10, school: "physical" }],
    };

    it("with no abilities registry passed (the real, unwired call path), still resolves a hostile default target when a player is present", () => {
      const state = stateAt(40);
      state.actors[2] = playerActor(2, { x: 0, y: 0, z: 500 });
      const result = stepEnemyScripts(state, { 1: meleeScript });
      expect(result.commands).toEqual([{ tick: 40, actorId: 1, kind: "cast", abilityId: "melee", targetId: 2 }]);
    });

    it("with an abilities registry: a nothing-in-range target-shaped cast is skipped entirely (no spam)", () => {
      const state = stateAt(40);
      state.actors[2] = playerActor(2, { x: 0, y: 0, z: 9000 }); // outside MELEE's 3000mm range
      const result = stepEnemyScripts(state, { 1: meleeScript }, {}, { melee: MELEE });
      expect(result.commands.filter((c) => c.kind === "cast")).toHaveLength(0);
      // ...but the enemy closes the gap instead (sim/encounter/chase.ts): one move straight at +z.
      expect(result.commands).toEqual([{ tick: 40, actorId: 1, kind: "move", dir: { x: 0, z: 1000 }, yaw: 256 }]);
    });

    it("a blocked onDeath step emits neither the cast command nor its telegraph", () => {
      const withTelegraph: EnemyScript = [
        { at: "onDeath", cast: "melee", telegraph: { shape: "circle", radius: 2000, leadTicks: 20 } },
      ];
      const state = stateAt(500);
      const actor = state.actors[1];
      if (actor) actor.alive = false;
      state.actors[2] = playerActor(2, { x: 0, y: 0, z: 9000 }); // outside MELEE's range
      const result = stepEnemyScripts(state, { 1: withTelegraph }, {}, { melee: MELEE });
      expect(result.commands).toHaveLength(0);
      expect(result.telegraphEvents).toHaveLength(0);
      expect(result.firedOnDeath[1]).toEqual(["melee"]); // still counted as fired -- the fire-once contract
    });

    it("a step's `target: \"self\"` override reaches the emitted command via stepEnemyScripts", () => {
      const selfScript: EnemyScript = [{ at: "loop", every: 40, cast: "melee", target: "self" }];
      const state = stateAt(40);
      state.actors[2] = playerActor(2, { x: 0, y: 0, z: 500 });
      const result = stepEnemyScripts(state, { 1: selfScript });
      expect(result.commands).toEqual([{ tick: 40, actorId: 1, kind: "cast", abilityId: "melee", targetId: 1 }]);
    });
  });

  // STARTCAST-BUSY: startCast refuses a caster with a cast in flight, so a scripted cast that fires while its
  // caster is busy is held and issued the first tick the caster is free (BIBLE s9: the Lock still happens).
  describe("busy caster: a scripted cast is deferred, never dropped", () => {
    const selfAbility = (id: string, castTicks: number): AbilityDef => ({
      id, castTicks, gcdTicks: 0, cooldownTicks: 0, charges: 1, cost: { resource: "none", amount: 0 },
      range: 0, shape: { kind: "self" }, interruptible: false, effects: [],
    });
    const ABILITIES = { slam: selfAbility("slam", 10), lock: selfAbility("lock", 0) };
    const casts = (cmds: { kind: string }[]) => cmds.filter((c) => c.kind === "cast");
    const busy = (tick: number, castId: string, castEndsTick: number): SimState => {
      const s = stateAt(tick);
      const a = s.actors[1]!;
      a.castId = castId;
      a.castEndsTick = castEndsTick;
      return s;
    };

    it("same tick: the cast-time step is issued, the instant behind it is held, then issued when that cast ends", () => {
      const script: EnemyScript = [{ at: "loop", every: 300, cast: "slam" }, { at: "loop", every: 600, cast: "lock" }];
      const at600 = stepEnemyScripts(stateAt(600), { 1: script }, {}, ABILITIES);
      expect(casts(at600.commands)).toEqual([{ tick: 600, actorId: 1, kind: "cast", abilityId: "slam", targetId: 1 }]);
      expect(at600.deferredCasts).toEqual([{ actorId: 1, step: script[1] }]);

      const mid = stepEnemyScripts(busy(605, "slam", 610), { 1: script }, {}, ABILITIES, at600.deferredCasts);
      expect(casts(mid.commands)).toEqual([]);
      expect(mid.deferredCasts).toEqual(at600.deferredCasts);

      // rules.ts completes the slam (castEndsTick === tick) before it runs commands, so the caster is free.
      const end = stepEnemyScripts(busy(610, "slam", 610), { 1: script }, {}, ABILITIES, mid.deferredCasts);
      expect(casts(end.commands)).toEqual([{ tick: 610, actorId: 1, kind: "cast", abilityId: "lock", targetId: 1 }]);
      expect(end.deferredCasts).toEqual([]);
    });

    it("an instant ahead of a cast-time step in the same tick leaves the caster free: both are issued", () => {
      const script: EnemyScript = [{ at: "loop", every: 600, cast: "lock" }, { at: "loop", every: 300, cast: "slam" }];
      const r = stepEnemyScripts(stateAt(600), { 1: script }, {}, ABILITIES);
      expect(r.commands.flatMap((c) => (c.kind === "cast" ? [c.abilityId] : []))).toEqual(["lock", "slam"]);
      expect(r.deferredCasts).toEqual([]);
    });

    it("a held telegraphed step re-draws its telegraph on the tick it is issued (BIBLE s3)", () => {
      const tg = { shape: "circle" as const, radius: 3000, leadTicks: 10 };
      const script: EnemyScript = [{ at: "loop", every: 300, cast: "slam", telegraph: tg }];
      const held = stepEnemyScripts(busy(300, "other", 320), { 1: script }, {}, ABILITIES);
      expect(casts(held.commands)).toEqual([]);
      const issued = stepEnemyScripts(busy(320, "other", 320), { 1: script }, {}, ABILITIES, held.deferredCasts);
      expect(casts(issued.commands)).toEqual([{ tick: 320, actorId: 1, kind: "cast", abilityId: "slam", targetId: 1 }]);
      expect(issued.telegraphEvents).toEqual([expect.objectContaining({ tick: 320, cast: "slam", resolvesTick: 330 })]);
    });

    it("a step that fires again while still held is held once; a dead caster's held cast is dropped", () => {
      const script: EnemyScript = [{ at: "loop", every: 100, cast: "lock" }];
      const first = stepEnemyScripts(busy(100, "long", 500), { 1: script }, {}, ABILITIES);
      const again = stepEnemyScripts(busy(200, "long", 500), { 1: script }, {}, ABILITIES, first.deferredCasts);
      expect(again.deferredCasts).toEqual([{ actorId: 1, step: script[0] }]);
      const dead = busy(250, "long", 500);
      dead.actors[1]!.alive = false;
      const gone = stepEnemyScripts(dead, { 1: script }, {}, ABILITIES, again.deferredCasts);
      expect(gone.commands).toEqual([]);
      expect(gone.deferredCasts).toEqual([]);
    });
  });
});
