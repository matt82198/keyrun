/**
 * sim/encounter/clauses.ts — the four Clauses (design/BIBLE.md §8) as run modifiers.
 *
 * Exactly one Clause runs per key, never stacked; a Clause changes plans (pacing, telegraphs,
 * damage weighting), never health pools directly. `ClauseDef` is a local, structural mirror of
 * `content/schema/clause.ts` — same mirroring convention content's own schema/*.ts already uses
 * on sim-combat (content/CLAUDE.md) — because sim never imports content (sim/CLAUDE.md).
 *
 * `applyClause(rules, clause)` is pure: given a base `EncounterRules` (encounter-lane tunables,
 * defaulted from tempo.ts/timer.ts) it returns a NEW `EncounterRules` with that Clause's `params`
 * folded in and `activeClauseId` set. Calling it again on rules that already carry a Clause throws
 * `ClauseAlreadyActiveError` (fail-closed per sim/CLAUDE.md invariant 4, over a silent no-op) so a
 * wiring bug that lets two picks reach the sim is loud, not dropped.
 *
 * The rest are pure per-Clause effects usable against a stepped pull's real data, without
 * touching sim/combat/** or sim/core/** (out of this lane's scope): Tide drifts a `Telegraph`'s
 * origin (telegraphs.ts's `pointInCircle` proves the hit-test changes), Hush is a silence-window
 * predicate, Pact is a bp damage weighting, Echo reduces "interrupt" SimEvents to spawn requests.
 *
 * WIRING NOTE for the integration lane (sim/index.ts / PR #15, out of scope here): thread one
 * `EncounterRules` through `RunContent` -> `EncounterState` (`encounter.rules = content.encounterRules
 * ?? defaultEncounterRules(keyLevel)` in `createRun`) so `activeClauseId` rides inside the
 * encounter lane's durable state and a future hash over `encounter` reflects the active Clause,
 * exactly as it already does for `pullIndex`/`slackTicks`.
 *
 * Usage: `const rules = applyClause(defaultEncounterRules(keyLevel), clauseDef);`
 */
import type { ActorId, SimEvent, Tick } from "./types";
import type { Telegraph } from "./telegraphs";
import {
  BP_DENOM,
  TEMPO_BP_PER_KEY,
  LEAD_SHRINK_BP_PER_KEY,
  LEAD_FLOOR_TICKS,
  EVERY_FLOOR_TICKS,
} from "./tempo";
import { SLACK_BASE_TICKS, SLACK_PER_KEY_TICKS, DEATH_SLACK_COST_TICKS, WIPE_SLACK_COST_TICKS } from "./timer";

export type ClauseKind = "echo" | "tide" | "hush" | "pact";
/** Structural mirror of content/schema/clause.ts's ClauseDef; sim never imports content. */
export interface ClauseDef {
  id: string;
  name: string;
  kind: ClauseKind;
  params: Record<string, number>;
  description: string;
}

export interface EncounterRules {
  keyLevel: number;
  tempoBpPerKey: number;
  leadShrinkBpPerKey: number;
  leadFloorTicks: number;
  everyFloorTicks: number;
  slackBaseTicks: number;
  slackPerKeyTicks: number;
  deathSlackCostTicks: number;
  wipeSlackCostTicks: number;
  driftMmPerTick: number;
  silenceIntervalTicks: number;
  silenceDurationTicks: number;
  comboDamageBp: number;
  soloDamageBp: number;
  echoAddMaxHealth: number;
  echoSpawnDelayTicks: number;
  activeClauseId: string | null;
  activeClauseKind: ClauseKind | null;
}

export function defaultEncounterRules(keyLevel: number): EncounterRules {
  return {
    keyLevel,
    tempoBpPerKey: TEMPO_BP_PER_KEY,
    leadShrinkBpPerKey: LEAD_SHRINK_BP_PER_KEY,
    leadFloorTicks: LEAD_FLOOR_TICKS,
    everyFloorTicks: EVERY_FLOOR_TICKS,
    slackBaseTicks: SLACK_BASE_TICKS,
    slackPerKeyTicks: SLACK_PER_KEY_TICKS,
    deathSlackCostTicks: DEATH_SLACK_COST_TICKS,
    wipeSlackCostTicks: WIPE_SLACK_COST_TICKS,
    driftMmPerTick: 0,
    silenceIntervalTicks: 0,
    silenceDurationTicks: 0,
    comboDamageBp: 0,
    soloDamageBp: 0,
    echoAddMaxHealth: 0,
    echoSpawnDelayTicks: 0,
    activeClauseId: null,
    activeClauseKind: null,
  };
}

export class ClauseAlreadyActiveError extends Error {
  constructor(activeId: string, attemptedId: string) {
    super(`clause "${activeId}" already active; cannot also apply "${attemptedId}" (exactly one Clause per key)`);
    this.name = "ClauseAlreadyActiveError";
  }
}

/** Pure: returns a NEW EncounterRules. Throws ClauseAlreadyActiveError if one is already active. */
export function applyClause(rules: EncounterRules, clause: ClauseDef): EncounterRules {
  if (rules.activeClauseId !== null) {
    throw new ClauseAlreadyActiveError(rules.activeClauseId, clause.id);
  }
  const next: EncounterRules = { ...rules, activeClauseId: clause.id, activeClauseKind: clause.kind };
  if (clause.kind === "echo") {
    next.echoAddMaxHealth = clause.params.addMaxHealth ?? 0;
    next.echoSpawnDelayTicks = clause.params.spawnDelayTicks ?? 0;
  } else if (clause.kind === "tide") {
    next.driftMmPerTick = clause.params.driftMmPerTick ?? 0;
  } else if (clause.kind === "hush") {
    next.silenceIntervalTicks = clause.params.intervalTicks ?? 0;
    next.silenceDurationTicks = clause.params.silenceDurationTicks ?? 0;
  } else if (clause.kind === "pact") {
    next.comboDamageBp = clause.params.comboDamageBp ?? 0;
    next.soloDamageBp = clause.params.soloDamageBp ?? 0;
  } else {
    throw new Error(`unknown clause kind: ${String((clause as ClauseDef).kind)}`);
  }
  return next;
}

/** Tide: shifts a telegraph's origin along +x by `driftMmPerTick`/tick. No-op unless Tide is active. */
export function driftTelegraphOrigin(telegraph: Telegraph, rules: EncounterRules): Telegraph {
  if (rules.activeClauseKind !== "tide" || rules.driftMmPerTick === 0) return telegraph;
  return { ...telegraph, origin: { ...telegraph.origin, x: telegraph.origin.x + rules.driftMmPerTick } };
}
export function driftTelegraphs(telegraphs: Telegraph[], rules: EncounterRules): Telegraph[] {
  return telegraphs.map((t) => driftTelegraphOrigin(t, rules));
}

/** Hush: true inside a silence pulse. First pulse fires at `silenceIntervalTicks` (scripts.ts's loop
 * convention: first firing at tick N, not tick 0), each lasting `silenceDurationTicks`. */
export function shouldSilence(tick: Tick, rules: EncounterRules): boolean {
  if (rules.activeClauseKind !== "hush" || rules.silenceIntervalTicks <= 0 || tick <= 0) return false;
  const sincePulse = tick % rules.silenceIntervalTicks;
  const pulseIndex = Math.floor(tick / rules.silenceIntervalTicks);
  return pulseIndex >= 1 && sincePulse < rules.silenceDurationTicks;
}

/** Pact: bp-weights damage by whether the hit landed inside a Combo. No-op unless Pact is active. */
export function applyPactDamageBp(baseDamage: number, wasCombo: boolean, rules: EncounterRules): number {
  if (rules.activeClauseKind !== "pact") return baseDamage;
  const bp = wasCombo ? rules.comboDamageBp : rules.soloDamageBp;
  return Math.trunc((baseDamage * (BP_DENOM + bp)) / BP_DENOM);
}

export interface EchoSpawnRequest {
  spawnAtTick: Tick;
  sourceActorId: ActorId;
  maxHealth: number;
}
/** Echo: reduces events to weak-add spawn requests, one per "interrupt", delayed `echoSpawnDelayTicks`.
 * Pure — does not spawn actors (needs pulls.ts's spawn machinery, owned by the integration lane). */
export function pendingEchoSpawns(events: SimEvent[], rules: EncounterRules): EchoSpawnRequest[] {
  if (rules.activeClauseKind !== "echo") return [];
  const requests: EchoSpawnRequest[] = [];
  for (const event of events) {
    if (event.type !== "interrupt") continue;
    requests.push({
      spawnAtTick: (event.tick as Tick) + rules.echoSpawnDelayTicks,
      sourceActorId: event.targetId as ActorId,
      maxHealth: rules.echoAddMaxHealth,
    });
  }
  return requests;
}

/** FNV-1a (32-bit) over a flat, ordered field list — proves the active Clause (or its absence)
 * always changes the hash, the property the integration lane relies on (see WIRING NOTE above). */
export function hashEncounterRules(rules: EncounterRules): string {
  const fields = [
    rules.keyLevel,
    rules.driftMmPerTick,
    rules.silenceIntervalTicks,
    rules.silenceDurationTicks,
    rules.comboDamageBp,
    rules.soloDamageBp,
    rules.echoAddMaxHealth,
    rules.echoSpawnDelayTicks,
    rules.activeClauseId ?? "none",
    rules.activeClauseKind ?? "none",
  ].join("|");
  let hash = 0x811c9dc5;
  for (let i = 0; i < fields.length; i++) {
    hash ^= fields.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash.toString(16).padStart(8, "0");
}
