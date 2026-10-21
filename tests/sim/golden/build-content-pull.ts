/**
 * tests/sim/golden/build-content-pull.ts -- builds and (when run directly) records
 * tests/sim/golden/content-pull.json: a golden replay through the REAL content/ registry
 * (content/index.ts's loadContent/toComboTables/toSimDungeon) and the real composition root
 * (sim/index.ts's createRun/stepRun), unlike scripted-pull.json which never touches content/ --
 * see sim/CLAUDE.md's "Replays are golden tests".
 *
 * Scenario: 2x warden, 2x cinder, 1x chanter (content.test.ts's roster shape), Hollow Vault's
 * first ("choir") pull, spawned ~3600mm short of ability range so the stream must move in first,
 * then each class's Setup closed by another class's Payoff (content.test.ts's three pairs:
 * "shatter" warden->cinder, "forge" cinder->warden, "anthem" chanter->warden), run 400 ticks.
 *
 * Usage: `npx tsx tests/sim/golden/build-content-pull.ts` re-records the fixture from scratch
 * (never hand-edit it). golden.test.ts imports this file's exports to recompute the same scenario.
 */
import { writeFileSync } from "node:fs";
import { fileURLToPath, pathToFileURL } from "node:url";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import { hashState } from "@sim/core/state";
import type { DungeonDef as SimDungeonDef } from "@sim/encounter/index";
import { loadContent, toComboTables, toSimDungeon, type ContentRegistry } from "@content/index";
import { sortedHashMap, type HashMap } from "./hash-map";

export const SEED = 24681357;
export const TICKS = 400;

// Two wardens, two cinders, one chanter -- BIBLE.md's three starter classes, content.test.ts's roster shape.
export const WARDEN_A = 301; // warden.strike (Setup:sundered), then warden.reckoning (closes "anthem")
export const CINDER_A = 302; // cinder.detonate (Payoff, closes "shatter")
export const CINDER_B = 303; // cinder.flare (Setup:kindled)
export const WARDEN_B = 304; // warden.reckoning (Payoff, closes "forge")
export const CHANTER_A = 305; // chanter.cadence (Setup:attuned)

// toSimDungeon assigns actor ids sequentially from 1000 in the "choir" pull's group order
// (content/data/dungeons/hollow-vault.json): 2x choir.melee then 1x choir.lieutenant.
export const ENEMY_1 = 1000;
export const ENEMY_2 = 1001;

const BOUNDS = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };
const MOVE_TICKS = 20; // ticks 0..19 at baseSpeedMmPerTick 180 close a 3600mm gap, see buildScenario.

export type Scenario = { run: Run; dungeon: SimDungeonDef };

/** Loads the real registry and wires a run against Hollow Vault's first pull only. */
export function buildScenario(): Scenario {
  const reg = loadContent();
  const abilities = Object.fromEntries(reg.abilities);
  const effects = Object.fromEntries(reg.effects);
  const { combos, fieldCombos } = toComboTables(reg.combos, [...reg.classes.keys()]);

  const dungeonDef = reg.dungeons.get("hollow-vault")!;
  const expanded = toSimDungeon(dungeonDef, reg.enemies, 1000);
  // Hollow Vault first pull only ("choir"): createRun spawns exactly ENEMY_1/ENEMY_2 + the lieutenant.
  const dungeon: SimDungeonDef = { id: expanded.dungeon.id, pulls: [expanded.dungeon.pulls[0]!] };

  // Positions are ~3600mm short (z) of content.test.ts's cast positions -- buildCommands moves in first.
  const party: PartyMemberSpec[] = [
    { id: WARDEN_A, classId: "warden", role: "tank", pos: { x: -500, y: 0, z: -3400 }, maxHealth: 2000, maxResource: 150 },
    { id: CINDER_A, classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: -3400 }, maxHealth: 2000, maxResource: 150 },
    { id: CINDER_B, classId: "cinder", role: "damage", pos: { x: 500, y: 0, z: -3400 }, maxHealth: 2000, maxResource: 150 },
    { id: WARDEN_B, classId: "warden", role: "damage", pos: { x: -250, y: 0, z: -3800 }, maxHealth: 2000, maxResource: 150 },
    { id: CHANTER_A, classId: "chanter", role: "healer", pos: { x: 250, y: 0, z: -3800 }, maxHealth: 2000, maxResource: 1 },
  ];

  const content: RunContent = {
    abilities,
    effects,
    lockoutTicks: 80,
    baseSpeedMmPerTick: 180,
    actorRadiusMm: 400,
    bounds: BOUNDS,
    enemyScripts: expanded.enemyScripts,
    combos,
    fieldCombos,
  };

  const run = createRun(SEED, party, dungeon, content);
  return { run, dungeon };
}

/** Moves every party member ~3600mm north into cast range (ticks 0-19), then casts each class's
 * Setup and closes it with another class's Payoff. */
export function buildCommands(): Command[] {
  const commands: Command[] = [];
  const movers = [WARDEN_A, CINDER_A, CINDER_B, WARDEN_B, CHANTER_A];
  for (let t = 0; t < MOVE_TICKS; t += 1) {
    for (const actorId of movers) {
      commands.push({ tick: t, actorId, kind: "move", dir: { x: 0, z: 1000 }, yaw: 256 });
    }
  }
  commands.push(
    { tick: 20, actorId: WARDEN_A, kind: "cast", abilityId: "warden.strike", targetId: ENEMY_1 },
    { tick: 20, actorId: CINDER_B, kind: "cast", abilityId: "cinder.flare", targetId: ENEMY_2 },
    { tick: 20, actorId: CHANTER_A, kind: "cast", abilityId: "chanter.cadence", targetId: ENEMY_1 },
    { tick: 40, actorId: CINDER_A, kind: "cast", abilityId: "cinder.detonate", targetId: ENEMY_1 },
    { tick: 60, actorId: WARDEN_B, kind: "cast", abilityId: "warden.reckoning", targetId: ENEMY_2 },
    { tick: 90, actorId: WARDEN_A, kind: "cast", abilityId: "warden.reckoning", targetId: ENEMY_1 },
  );
  return commands;
}

export type ScenarioResult = { run: Run; events: SimEvent[] };

/** Drives buildScenario() through buildCommands() for TICKS ticks via the real createRun/stepRun. */
export function runScenario(): ScenarioResult {
  const { run } = buildScenario();
  const commands = buildCommands();
  const events: SimEvent[] = [];
  let current = run;
  for (let t = 0; t < TICKS; t += 1) {
    const tickCommands = commands.filter((c) => c.tick === t);
    const stepped = stepRun(current, tickCommands);
    current = stepped.run;
    events.push(...stepped.events);
  }
  return { run: current, events };
}

/** Event counts by `type` -- asserted alongside the hash so a silent behaviour change fails with a
 * readable diff instead of a coincidentally-stable hash. */
export function eventCounts(events: SimEvent[]): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const e of events) counts[e.type] = (counts[e.type] ?? 0) + 1;
  return counts;
}

function fnv1a(str: string): string {
  let hash = 0x811c9dc5;
  for (let i = 0; i < str.length; i += 1) hash = Math.imul(hash ^ str.charCodeAt(i), 0x01000193) >>> 0;
  return hash.toString(16).padStart(8, "0");
}

function canonicalStringify(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonicalStringify).join(",")}]`;
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  const obj = value as Record<string, unknown>;
  const body = Object.keys(obj).sort().map((k) => `${JSON.stringify(k)}:${canonicalStringify(obj[k])}`);
  return `{${body.join(",")}}`;
}

/** FNV-1a (same algorithm as sim/core/state.ts's hashState) over a canonical rendering of EVERY
 * loaded registry entry, keyed "<kind>/<id>", so ANY content edit changes at least one entry. One
 * entry per line (see ./hash-map.ts) keeps two content PRs that edit different entries mergeable. */
export function contentHashes(reg: ContentRegistry = loadContent()): HashMap {
  const kinds: Record<string, ReadonlyMap<string, unknown>> = {
    abilities: reg.abilities,
    effects: reg.effects,
    classes: reg.classes,
    combos: reg.combos,
    enemies: reg.enemies,
    dungeons: reg.dungeons,
    clauses: reg.clauses,
    facets: reg.facets,
  };
  const entries: Array<[string, string]> = [];
  for (const [kind, map] of Object.entries(kinds)) {
    for (const [id, def] of map) entries.push([`${kind}/${id}`, fnv1a(canonicalStringify(def))]);
  }
  return sortedHashMap(entries);
}

/** The exact text content-pull.json is written as (2-space JSON, trailing newline). */
export function renderContentPull(fixture: object): string {
  return `${JSON.stringify(fixture, null, 2)}\n`;
}

const WHY =
  "Any change to expectedHash is either a deliberate content/sim behaviour change (explain it in the PR body) " +
  "or a regression (fix the code -- never retune this fixture to match, see sim/CLAUDE.md's 'Replays are " +
  "golden tests'). eventCounts exists for the same reason: a silent behaviour change should fail with a " +
  "readable diff, not just a hash mismatch. NOTE: enemy swings DO execute (targeting + chase landed): " +
  "choir.meleeSwing hits the party from tick 40, guarded by tests/sim/encounter/golden-melee.test.ts. " +
  "contentHashes pins the content version per registry entry, so two content PRs touch different lines.";

function main(): void {
  const { run, events } = runScenario();
  const counts = eventCounts(events);
  const fixture = {
    seed: SEED,
    ticks: TICKS,
    commands: buildCommands(),
    expectedHash: hashState(run.state),
    eventCounts: counts,
    why: WHY,
    contentHashes: contentHashes(),
  };
  const outPath = fileURLToPath(new URL("./content-pull.json", import.meta.url));
  writeFileSync(outPath, renderContentPull(fixture), "utf8");
  console.log(`wrote ${outPath}`);
  console.log(`expectedHash=${fixture.expectedHash} eventCounts=${JSON.stringify(counts)}`);
}

const invokedDirectly = process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href;
if (invokedDirectly) main();
