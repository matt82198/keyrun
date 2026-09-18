/**
 * tests/sim/encounter/break-bar-wiring.test.ts — unit coverage for the content->sim break-bar
 * wiring added in sim/encounter/breakbar.ts (deriveBreakBarInputs/breakBarEffectPatches) and the
 * spawn/enemy-setup hook added in sim/encounter/index.ts's stepEncounter (backlog item
 * break-bar-wiring, BIBLE.md Sec 9 "The Sundered Twins"). Existing tests/sim/encounter/
 * breakbar.test.ts is left unchanged -- this file only covers the NEW pieces.
 */
import { describe, expect, it } from "vitest";
import {
  deriveBreakBarInputs,
  breakBarEffectPatches,
  type BreakBarSource,
} from "../../../sim/encounter/breakbar";
import { stepEncounter, type BreakBarWiring } from "../../../sim/encounter/index";
import { defaultEncounterRules } from "../../../sim/encounter/clauses";
import type { EncounterState } from "../../../sim/encounter/telegraphs";
import type { ActorState, Command, SimEvent, SimState } from "../../../sim/encounter/types";
import type { DungeonDef } from "../../../sim/encounter/pulls";

const TWINS: BreakBarSource = {
  id: "twinsBreak",
  actorIds: [10, 11],
  max: 100,
  drainPerInterrupt: 34,
  drainPerControl: 20,
  onEmpty: [{ effectId: "stunned", durationTicks: 100, stacks: 1 }],
};

const TAGS: Record<string, string[] | undefined> = {
  "warden.shieldBash": ["interrupt", "control"],
  "cinder.quench": ["interrupt"],
  "cinder.coldSnap": ["control"],
  "warden.strike": undefined,
};

describe("deriveBreakBarInputs", () => {
  it("drains on an interrupt-and-control-tagged ability targeting a bar actor, preferring interrupt", () => {
    const cmds: Command[] = [{ tick: 5, actorId: 1, kind: "cast", abilityId: "warden.shieldBash", targetId: 10 }];
    const inputs = deriveBreakBarInputs(cmds, {}, TAGS, TWINS);
    expect(inputs).toEqual([{ tick: 5, actorId: 1, tag: "interrupt", amount: 34 }]);
  });

  it("drains on a control-only ability using drainPerControl", () => {
    const cmds: Command[] = [{ tick: 5, actorId: 1, kind: "cast", abilityId: "cinder.coldSnap", targetId: 11 }];
    const inputs = deriveBreakBarInputs(cmds, {}, TAGS, TWINS);
    expect(inputs).toEqual([{ tick: 5, actorId: 1, tag: "control", amount: 20 }]);
  });

  it("falls back to the caster's current targetId when the command carries none", () => {
    const cmds: Command[] = [{ tick: 5, actorId: 1, kind: "cast", abilityId: "cinder.quench" }];
    const inputs = deriveBreakBarInputs(cmds, { 1: { targetId: 10 } }, TAGS, TWINS);
    expect(inputs).toEqual([{ tick: 5, actorId: 1, tag: "interrupt", amount: 34 }]);
  });

  it("ignores non-cast commands, untagged abilities, and casts aimed off the bar", () => {
    const cmds: Command[] = [
      { tick: 5, actorId: 1, kind: "stopcast" },
      { tick: 5, actorId: 2, kind: "cast", abilityId: "warden.strike", targetId: 10 },
      { tick: 5, actorId: 3, kind: "cast", abilityId: "cinder.quench", targetId: 999 },
    ];
    expect(deriveBreakBarInputs(cmds, {}, TAGS, TWINS)).toEqual([]);
  });
});

describe("breakBarEffectPatches", () => {
  it("returns one patch per actorId x onEmpty entry only when justStaggered", () => {
    expect(breakBarEffectPatches(TWINS, false)).toEqual([]);
    expect(breakBarEffectPatches(TWINS, true)).toEqual([
      { actorId: 10, effectId: "stunned", durationTicks: 100, stacks: 1 },
      { actorId: 11, effectId: "stunned", durationTicks: 100, stacks: 1 },
    ]);
  });
});

function actor(id: number, targetId: number | null = null): ActorState {
  return {
    id,
    kind: id < 10 ? "player" : "enemy",
    classId: id < 10 ? "warden" : "boss",
    role: id < 10 ? "tank" : "none",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 100,
    maxHealth: 100,
    resource: 0,
    maxResource: 0,
    alive: true,
    targetId,
    castId: null,
    castEndsTick: null,
    castStartTick: null,
    cooldowns: {},
    gcdReadyTick: 0,
    effects: [],
    threat: {},
  };
}

const dungeon: DungeonDef = { id: "hollow-vault", pulls: [{ id: "boss", spawns: [] }] };

function baseEncounter(): EncounterState {
  return { telegraphs: [], breakBars: {}, pullIndex: 0, slackTicks: 100, parTicks: 1000, keyLevel: 1, depleted: false, rules: defaultEncounterRules(1) };
}

function stateAt(tick: number, actors: Record<number, ActorState>): SimState {
  return { tick, seed: 1, prng: [1, 2, 3, 4], actors, runTimerTicks: tick, phase: "combat", log: [] };
}

describe("stepEncounter break-bar wiring", () => {
  it("does nothing when breakBarWiring is omitted (unchanged behaviour for existing callers)", () => {
    const state = stateAt(0, { 1: actor(1), 10: actor(10), 11: actor(11) });
    const result = stepEncounter(state, baseEncounter(), dungeon, {}, () => {});
    expect(result.encounter.breakBars).toEqual({});
    expect(result.effectPatches).toEqual([]);
  });

  it("lazily creates the bar only once every actorId is alive, then drains it and reports stagger effect patches on empty", () => {
    const events: SimEvent[] = [];
    const emit = (e: SimEvent): void => {
      events.push(e);
    };
    const wiring = (commands: Command[]): BreakBarWiring => ({ sources: { twinsBreak: TWINS }, commands, abilityTags: TAGS });

    // Tick 0: only one twin alive yet -- bar must not be created.
    let state = stateAt(0, { 10: actor(10) });
    let result = stepEncounter(state, baseEncounter(), dungeon, {}, emit, wiring([]));
    expect(result.encounter.breakBars.twinsBreak).toBeUndefined();

    // Tick 1: both twins alive -- bar is created at max, and this tick's tagged casts drain it.
    state = stateAt(1, { 1: actor(1), 10: actor(10), 11: actor(11) });
    const cmds1: Command[] = [{ tick: 1, actorId: 1, kind: "cast", abilityId: "warden.shieldBash", targetId: 10 }];
    result = stepEncounter(state, result.encounter, dungeon, {}, emit, wiring(cmds1));
    expect(result.encounter.breakBars.twinsBreak?.current).toBe(66); // 100 - 34
    expect(result.effectPatches).toEqual([]);

    // Ticks 2-3: two more interrupt-tagged casts empty it (66 - 34 - 34 <= 0) -> stagger patches.
    state = stateAt(2, state.actors);
    const cmds2: Command[] = [{ tick: 2, actorId: 1, kind: "cast", abilityId: "warden.shieldBash", targetId: 11 }];
    result = stepEncounter(state, result.encounter, dungeon, {}, emit, wiring(cmds2));
    expect(result.encounter.breakBars.twinsBreak?.current).toBe(32);

    state = stateAt(3, state.actors);
    const cmds3: Command[] = [{ tick: 3, actorId: 1, kind: "cast", abilityId: "warden.shieldBash", targetId: 10 }];
    result = stepEncounter(state, result.encounter, dungeon, {}, emit, wiring(cmds3));
    expect(result.encounter.breakBars.twinsBreak?.current).toBe(0);
    expect(result.effectPatches).toEqual([
      { actorId: 10, effectId: "stunned", durationTicks: 100, stacks: 1 },
      { actorId: 11, effectId: "stunned", durationTicks: 100, stacks: 1 },
    ]);
    expect(events.some((e) => e.type === "stagger" && e.barId === "twinsBreak")).toBe(true);

    // Tick 4: already staggered -- no repeat effect patches even if more tagged casts land.
    state = stateAt(4, state.actors);
    const cmds4: Command[] = [{ tick: 4, actorId: 1, kind: "cast", abilityId: "warden.shieldBash", targetId: 10 }];
    result = stepEncounter(state, result.encounter, dungeon, {}, emit, wiring(cmds4));
    expect(result.effectPatches).toEqual([]);
  });
});
