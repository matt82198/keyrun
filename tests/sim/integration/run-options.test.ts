/**
 * tests/sim/integration/run-options.test.ts — P0 regression guard for run-options-wiring:
 * sim/index.ts's `createRun(seed, party, dungeon, content, options?)` previously accepted no
 * clause and no loadout at all, and no host passed them -- the screen's pick-of-three Clause
 * chooser (`?clause=`), the talent forks, and gear Facets never reached a real run. This file
 * pins the sim-side half of the fix: `options.clauseId` threads into `encounter.rules` (the exact
 * hook sim/encounter/clauses.ts's WIRING NOTE asks the integration lane for) and
 * `options.loadouts` (already-applyLoadout-resolved abilities -- see sim/run-options.ts's file
 * docstring for why sim receives applyLoadout's OUTPUT, not raw talentChoices/gear) merges onto
 * `content.abilities` so a cast actually uses the loadout-patched ability, not the base kit.
 *
 * SCOPE NOTE: `applyClause`'s four per-Clause pure functions (driftTelegraphOrigin, shouldSilence,
 * applyPactDamageBp, pendingEchoSpawns -- sim/encounter/clauses.ts) are proven here to compute
 * real, different output once `run.encounter.rules` carries the picked Clause -- that IS this
 * lane's WIRING NOTE-scoped deliverable (thread EncounterRules into EncounterState). Actually
 * CALLING those four functions every tick (telegraph registration, cast-start silence gating, the
 * damage pipeline, and pull spawning) lives in sim/encounter/telegraphs.ts + sim/index.ts,
 * sim/combat/casting.ts, sim/combat/damage.ts, sim/encounter/pulls.ts -- none of them read
 * `encounter.rules` today (verified: only telegraphs.ts imports the EncounterRules *type*, for
 * EncounterState's own field, nothing reads it). Those files are sim/combat/** and
 * sim/encounter/**, outside this lane's ownership boundary -- a second, deeper defect, flagged to
 * the orchestrator rather than silently expanded into here.
 */
import { describe, expect, it } from "vitest";
import {
  createRun,
  stepRun,
  UnknownClauseError,
  UnknownLoadoutActorError,
  UnknownLoadoutAbilityError,
  type PartyMemberSpec,
  type RunContent,
} from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { AbilityDef } from "@sim/combat/abilities";
import type { DungeonDef } from "@sim/encounter/pulls";
import {
  applyPactDamageBp,
  ClauseAlreadyActiveError,
  defaultEncounterRules,
  driftTelegraphOrigin,
  hashEncounterRules,
  pendingEchoSpawns,
  shouldSilence,
  type ClauseDef,
} from "@sim/encounter/clauses";
import type { Telegraph } from "@sim/encounter/telegraphs";
import { BASE_PARTY, CONTENT, DUNGEON, PLAYER_ID, DUMMY_ID, SEED } from "./fixtures";

const ECHO: ClauseDef = { id: "echo", name: "Echo", kind: "echo", params: { addMaxHealth: 600, spawnDelayTicks: 40 }, description: "d" };
const TIDE: ClauseDef = { id: "tide", name: "Tide", kind: "tide", params: { driftMmPerTick: 50 }, description: "d" };
const HUSH: ClauseDef = { id: "hush", name: "Hush", kind: "hush", params: { intervalTicks: 100, silenceDurationTicks: 20 }, description: "d" };
const PACT: ClauseDef = { id: "pact", name: "Pact", kind: "pact", params: { comboDamageBp: 2000, soloDamageBp: -1000 }, description: "d" };
const CLAUSES: Record<string, ClauseDef> = { echo: ECHO, tide: TIDE, hush: HUSH, pact: PACT };

function contentWithClauses(): RunContent {
  return { ...CONTENT, clauses: CLAUSES };
}

describe("createRun options: Clause wiring", () => {
  it("defaults to no active Clause when options is omitted (unchanged behaviour)", () => {
    const run = createRun(SEED, BASE_PARTY, DUNGEON, CONTENT);
    expect(run.encounter.rules.activeClauseId).toBeNull();
    expect(run.encounter.rules.activeClauseKind).toBeNull();
  });

  it("threads options.clauseId into encounter.rules and changes hashEncounterRules", () => {
    const noClause = createRun(SEED, BASE_PARTY, DUNGEON, contentWithClauses());
    const withEcho = createRun(SEED, BASE_PARTY, DUNGEON, contentWithClauses(), { clauseId: "echo" });
    expect(withEcho.encounter.rules.activeClauseId).toBe("echo");
    expect(withEcho.encounter.rules.activeClauseKind).toBe("echo");
    expect(hashEncounterRules(withEcho.encounter.rules)).not.toBe(hashEncounterRules(noClause.encounter.rules));
  });

  it("throws a clear, named error for an unknown clauseId -- never a silent no-Clause default", () => {
    expect(() => createRun(SEED, BASE_PARTY, DUNGEON, contentWithClauses(), { clauseId: "nope" })).toThrow(
      UnknownClauseError,
    );
    expect(() => createRun(SEED, BASE_PARTY, DUNGEON, contentWithClauses(), { clauseId: "nope" })).toThrow(
      /options\.clauseId "nope" is not in content\.clauses/,
    );
  });

  it("propagates ClauseAlreadyActiveError when content.encounterRules already carries an active Clause", () => {
    const already = { ...defaultEncounterRules(5), activeClauseId: "tide", activeClauseKind: "tide" as const };
    const content: RunContent = { ...contentWithClauses(), encounterRules: already };
    expect(() => createRun(SEED, BASE_PARTY, DUNGEON, content, { clauseId: "pact" })).toThrow(ClauseAlreadyActiveError);
  });

  it.each([
    ["echo", ECHO],
    ["tide", TIDE],
    ["hush", HUSH],
    ["pact", PACT],
  ] as const)("%s: the real per-Clause functions compute genuinely different output once threaded", (id, def) => {
    const noClauseRun = createRun(SEED, BASE_PARTY, DUNGEON, contentWithClauses());
    const clauseRun = createRun(SEED, BASE_PARTY, DUNGEON, contentWithClauses(), { clauseId: id });
    const baseRules = noClauseRun.encounter.rules;
    const rules = clauseRun.encounter.rules;

    if (def.kind === "tide") {
      const tg: Telegraph = { id: 1, sourceId: 11, abilityId: "cleave", shape: "circle", origin: { x: 0, y: 0, z: 0 }, yaw: 0, radius: 1000, resolvesTick: 10 };
      expect(driftTelegraphOrigin(tg, rules).origin.x).not.toBe(driftTelegraphOrigin(tg, baseRules).origin.x);
    } else if (def.kind === "hush") {
      const silencedTicks = [...Array(200).keys()].filter((t) => shouldSilence(t, rules));
      const baseSilencedTicks = [...Array(200).keys()].filter((t) => shouldSilence(t, baseRules));
      expect(silencedTicks.length).toBeGreaterThan(0);
      expect(baseSilencedTicks.length).toBe(0);
    } else if (def.kind === "pact") {
      expect(applyPactDamageBp(1000, true, rules)).toBeGreaterThan(1000);
      expect(applyPactDamageBp(1000, false, rules)).toBeLessThan(1000);
      expect(applyPactDamageBp(1000, true, baseRules)).toBe(1000);
    } else {
      const interruptEvent = { tick: 5, type: "interrupt" as const, actorId: PLAYER_ID, targetId: DUMMY_ID };
      expect(pendingEchoSpawns([interruptEvent], rules)).toHaveLength(1);
      expect(pendingEchoSpawns([interruptEvent], baseRules)).toHaveLength(0);
    }
  });
});

describe("createRun options: Loadout wiring", () => {
  const BASE_ZAP: AbilityDef = {
    id: "zap",
    castTicks: 0,
    gcdTicks: 0,
    cooldownTicks: 100,
    charges: 1,
    cost: { resource: "energy", amount: 0 },
    range: 100000,
    shape: { kind: "target" },
    interruptible: false,
    effects: [{ op: "damage", amount: 1, school: "physical" }],
  };
  const TALENTED_ZAP: AbilityDef = { ...BASE_ZAP, cooldownTicks: 250 };
  const ZAP_DUNGEON: DungeonDef = { id: "empty", pulls: [{ id: "p", spawns: [{ actorId: 11, classId: "boss", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 1 }] }] };
  const ZAP_CONTENT: RunContent = { ...CONTENT, abilities: { zap: BASE_ZAP }, enemyScripts: {} };

  function runZapSchedule(options?: Parameters<typeof createRun>[4]): number {
    let run = createRun(SEED, BASE_PARTY, ZAP_DUNGEON, ZAP_CONTENT, options);
    let completions = 0;
    for (let t = 0; t < 300; t++) {
      const commands: Command[] = [];
      if (t === 0 || t === 150 || t === 260) {
        commands.push({ tick: t, actorId: PLAYER_ID, kind: "cast", abilityId: "zap", targetId: DUMMY_ID });
      }
      const stepped = stepRun(run, commands);
      run = stepped.run;
      completions += stepped.events.filter((e) => e.type === "damage").length;
    }
    return completions;
  }

  it("with no loadout: base cooldown (100 ticks) lets all three scheduled casts land", () => {
    expect(runZapSchedule()).toBe(3);
  });

  it("with a loadout patching the ability's cooldown: the SECOND cast (tick 150) is refused at the talented cooldown (250), not the base one (100)", () => {
    const completions = runZapSchedule({ loadouts: { [PLAYER_ID]: { zap: TALENTED_ZAP } } });
    expect(completions).toBe(2); // tick 0 and tick 260 land; tick 150 (>=100 but <250) is refused
  });

  it("throws a clear, named error for a loadout keyed to an actor not in the party", () => {
    expect(() =>
      createRun(SEED, BASE_PARTY, ZAP_DUNGEON, ZAP_CONTENT, { loadouts: { 999: { zap: TALENTED_ZAP } } }),
    ).toThrow(UnknownLoadoutActorError);
  });

  it("throws a clear, named error for a loadout patching an ability absent from content.abilities", () => {
    expect(() =>
      createRun(SEED, BASE_PARTY, ZAP_DUNGEON, ZAP_CONTENT, {
        loadouts: { [PLAYER_ID]: { "not-a-real-ability": TALENTED_ZAP } },
      }),
    ).toThrow(UnknownLoadoutAbilityError);
  });

  it("default options (no clauseId, no loadouts) is byte-identical to calling createRun with no 5th argument", () => {
    const withUndefinedOptions = createRun(SEED, BASE_PARTY, DUNGEON, CONTENT, undefined);
    const withNoArg = createRun(SEED, BASE_PARTY, DUNGEON, CONTENT);
    expect(withUndefinedOptions.content).toBe(CONTENT); // same reference: no rebuild when nothing to merge
    expect(withNoArg.content).toBe(CONTENT);
  });
});
