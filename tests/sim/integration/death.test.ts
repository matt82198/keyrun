/**
 * tests/sim/integration/death.test.ts — backlog item death-trigger-wiring (P1).
 *
 * Two scenarios, both driven through the REAL public path (`createRun`/`stepRun`, no local
 * `advanceTick`) and the REAL content registry (`loadContent()`), proving the two gaps left open
 * by PR #28 (sim-death-trigger) are actually wired end to end:
 *
 * 1. seed.elite's onDeath step (`content/data/enemies/seed.json`) fires `seed.deathBloom` exactly
 *    once, resolving through sim/combat's ordinary cast pipeline (not dropped as a dead-actor
 *    command) even though scripts.ts's ONE-TICK LAG means it fires the tick AFTER the "death"
 *    SimEvent's own tick, and its FIRE-ONCE CONTRACT requires `firedOnDeath` to survive being
 *    threaded through `EncounterState` across ticks. DEVIATION: this codebase has no "cast_start"
 *    SimEvent (sim/combat/casting.ts emits only "cast_complete"/"cast_stopped"/"interrupt" --
 *    startCast itself emits nothing); `seed.deathBloom` is instant (castTicks: 0) so its
 *    cast_complete IS its cast, and is the strongest available signal that rules.ts actually ran
 *    the ability's effects (proven further by the "window_opened" floor-Window event its "window"
 *    EffectOp produces) rather than just accepting a command it then no-ops.
 * 2. `pair.melee`'s `spreadMark`/`stackCall` (`content/data/enemies/pair.json`) fire on their own
 *    offset schedules (BIBLE.md Sec 9's "Pair" pull: spread, then ten seconds later stack) instead
 *    of colliding on the same `every: 400` loop.
 *
 * A third scenario (PR #35 follow-up, its own WIRING NOTE in sim/encounter/script-target.ts):
 * `stepRun` now passes `content.abilities` into `stepEncounter`/`stepEnemyScripts`, so an enemy's
 * target-shaped ability is range-gated for real against a live player, instead of always resolving
 * a best-effort target regardless of distance (the safe fallback script-target.ts uses when no
 * `abilities` registry is passed at all).
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type Run, type RunContent, type PartyMemberSpec } from "@sim/index";
import type { DungeonDef as SimDungeonDef } from "@sim/encounter/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import { loadContent } from "@content/index";

const SEED = 121212;
const PLAYER_ID = 1;
const ELITE_ID = 3000;
const PAIR_ID = 4000;

function eventsOf(kind: string, events: SimEvent[]): SimEvent[] {
  return events.filter((e) => e.type === kind);
}

function runTicks(run: Run, ticks: number, events: SimEvent[], commandsByTick: (t: number) => Command[]): Run {
  let current = run;
  while (current.state.tick < ticks) {
    const tick = current.state.tick;
    const { run: next, events: tickEvents } = stepRun(current, commandsByTick(tick));
    events.push(...tickEvents);
    current = next;
  }
  return current;
}

describe("onDeath trigger fires once and resolves (integration)", () => {
  function buildEliteRun(): Run {
    const reg = loadContent();
    const abilities = Object.fromEntries(reg.abilities);
    const effects = Object.fromEntries(reg.effects);
    const bounds = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };
    const eliteDef = reg.enemies.get("seed.elite")!;

    const dungeon: SimDungeonDef = {
      id: "seed-solo",
      pulls: [
        {
          id: "seed",
          spawns: [{ actorId: ELITE_ID, classId: eliteDef.id, pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 50 }],
        },
      ],
    };

    const party: PartyMemberSpec[] = [
      { id: PLAYER_ID, classId: "warden", role: "tank", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 2000, maxResource: 150 },
    ];

    const runContent: RunContent = {
      abilities,
      effects,
      lockoutTicks: 80,
      baseSpeedMmPerTick: 180,
      actorRadiusMm: 400,
      bounds,
      enemyScripts: { [ELITE_ID]: eliteDef.script },
    };

    return createRun(SEED, party, dungeon, runContent);
  }

  it("kills the elite at tick 10, fires seed.deathBloom exactly once, and the cast resolves", () => {
    const events: SimEvent[] = [];
    const run = buildEliteRun();
    const killCommand = { tick: 10, actorId: PLAYER_ID, kind: "cast" as const, abilityId: "warden.strike", targetId: ELITE_ID };

    const final = runTicks(run, 61, events, (t) => (t === 10 ? [killCommand] : []));

    const deathEvents = eventsOf("death", events).filter((e) => e.actorId === ELITE_ID);
    expect(deathEvents).toHaveLength(1);
    expect(deathEvents[0]!.tick).toBe(10);

    const bloomCasts = eventsOf("cast_complete", events).filter((e) => e.abilityId === "seed.deathBloom");
    expect(bloomCasts).toHaveLength(1);
    // ONE-TICK LAG (sim/encounter/scripts.ts): fires the tick after the death event's own tick,
    // never the same tick.
    expect(bloomCasts[0]!.tick).toBe(11);

    // Proves rules.ts did not silently drop the dead actor's cast command: the ability's "window"
    // EffectOp actually ran through sim/combat/windows.ts and opened a floor Window.
    const windowEvents = eventsOf("window_opened", events).filter((e) => e.windowId === "seedbloom");
    expect(windowEvents).toHaveLength(1);
    expect(windowEvents[0]!.onTarget).toBe(false);
    expect(windowEvents[0]!.setupActorId).toBe(ELITE_ID);

    expect(final.state.actors[ELITE_ID]!.alive).toBe(false);
  });

  it("never re-fires seed.deathBloom on later ticks (fire-once contract survives EncounterState)", () => {
    const events: SimEvent[] = [];
    const run = buildEliteRun();
    const killCommand = { tick: 10, actorId: PLAYER_ID, kind: "cast" as const, abilityId: "warden.strike", targetId: ELITE_ID };

    runTicks(run, 200, events, (t) => (t === 10 ? [killCommand] : []));

    const bloomCasts = eventsOf("cast_complete", events).filter((e) => e.abilityId === "seed.deathBloom");
    expect(bloomCasts).toHaveLength(1);
  });
});

describe("pair.melee's spreadMark/stackCall fire on offset schedules (integration)", () => {
  function buildPairRun(): Run {
    const reg = loadContent();
    const abilities = Object.fromEntries(reg.abilities);
    const effects = Object.fromEntries(reg.effects);
    const bounds = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };
    const pairDef = reg.enemies.get("pair.melee")!;

    const dungeon: SimDungeonDef = {
      id: "pair-solo",
      pulls: [
        {
          id: "pair",
          spawns: [{ actorId: PAIR_ID, classId: pairDef.id, pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: pairDef.maxHealth }],
        },
      ],
    };

    const runContent: RunContent = {
      abilities,
      effects,
      lockoutTicks: 80,
      baseSpeedMmPerTick: 180,
      actorRadiusMm: 400,
      bounds,
      enemyScripts: { [PAIR_ID]: pairDef.script },
    };

    return createRun(SEED, [], dungeon, runContent);
  }

  it("spreadMark starts its loop at 400 and stackCall starts ten seconds later, at 600", () => {
    // Scheduling is asserted off "telegraph" SimEvents (resolvesTick = the tick the scripted cast
    // actually fires), not "cast_complete": pair.spreadMark's ability shape is "target" and no
    // targetId is ever assigned here on purpose -- assigning one is sim/encounter/script-target.ts's
    // job (the concurrent enemy-targeting lane this PR must not touch, per its brief), and whether
    // that cast then completes is orthogonal to whether it is SCHEDULED on the right tick, which is
    // this backlog item's entire scope. pair.stackCall's shape is "circle" (no target needed), so
    // its cast_complete is asserted too, as an extra, targeting-independent confirmation.
    const events: SimEvent[] = [];
    const run = buildPairRun();
    runTicks(run, 1050, events, () => []);

    const spreadTelegraphs = eventsOf("telegraph", events)
      .filter((e) => e.cast === "pair.spreadMark")
      .map((e) => e.resolvesTick);
    const stackTelegraphs = eventsOf("telegraph", events)
      .filter((e) => e.cast === "pair.stackCall")
      .map((e) => e.resolvesTick);

    expect(spreadTelegraphs).toEqual([400, 800]);
    expect(stackTelegraphs).toEqual([600, 1000]);

    // The two never land on the same tick -- the gap content/CLAUDE.md flagged (both on the same
    // every:400 loop) is closed.
    const overlap = spreadTelegraphs.filter((t) => stackTelegraphs.includes(t));
    expect(overlap).toEqual([]);

    // castTicks: 40, so a step fired at tick T completes at T+40. stackCall needs no target, so
    // its cast always resolves once scheduled.
    const stackCompletes = eventsOf("cast_complete", events)
      .filter((e) => e.abilityId === "pair.stackCall")
      .map((e) => e.tick);
    expect(stackCompletes).toEqual([640, 1040]);
  });
});

describe("stepRun threads content.abilities into enemy targeting (range gating, integration)", () => {
  function buildRangeRun(playerPos: { x: number; y: number; z: number }): Run {
    const reg = loadContent();
    const abilities = Object.fromEntries(reg.abilities);
    const effects = Object.fromEntries(reg.effects);
    const bounds = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };
    const pairDef = reg.enemies.get("pair.melee")!;

    const dungeon: SimDungeonDef = {
      id: "pair-range",
      pulls: [
        {
          id: "pair",
          spawns: [{ actorId: PAIR_ID, classId: pairDef.id, pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: pairDef.maxHealth }],
        },
      ],
    };

    const party: PartyMemberSpec[] = [
      { id: PLAYER_ID, classId: "warden", role: "tank", pos: playerPos, maxHealth: 2000, maxResource: 150 },
    ];

    const runContent: RunContent = {
      abilities,
      effects,
      lockoutTicks: 80,
      baseSpeedMmPerTick: 180,
      actorRadiusMm: 400,
      bounds,
      enemyScripts: { [PAIR_ID]: pairDef.script },
    };

    return createRun(SEED, party, dungeon, runContent);
  }

  // pair.meleeSwing: shape "target", range 3000mm, loops every 40 ticks, instant (castTicks: 0).

  it("an in-range player takes meleeSwing damage (positive control)", () => {
    const events: SimEvent[] = [];
    const initial = buildRangeRun({ x: 1000, y: 0, z: 0 }); // 1000mm: well inside range 3000
    const final = runTicks(initial, 80, events, () => []);

    const swings = eventsOf("cast_complete", events).filter((e) => e.abilityId === "pair.meleeSwing");
    expect(swings.length).toBeGreaterThan(0);
    expect(final.state.actors[PLAYER_ID]!.health).toBeLessThan(2000);
  });

  it("an out-of-range player is never targeted and never takes meleeSwing damage", () => {
    const events: SimEvent[] = [];
    const initial = buildRangeRun({ x: 9000, y: 0, z: 0 }); // 9000mm: well outside range 3000
    const final = runTicks(initial, 80, events, () => []);

    const swings = eventsOf("cast_complete", events).filter((e) => e.abilityId === "pair.meleeSwing");
    expect(swings).toEqual([]);
    expect(eventsOf("damage", events).filter((e) => e.targetId === PLAYER_ID)).toEqual([]);
    expect(final.state.actors[PLAYER_ID]!.health).toBe(2000);
  });
});
