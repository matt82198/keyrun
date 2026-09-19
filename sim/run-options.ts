/**
 * sim/run-options.ts — createRun's optional per-run knobs (Clause pick + per-actor loadout
 * ability overrides), split out of sim/index.ts to keep it under sim/CLAUDE.md's <=190-LOC-per-
 * file cap.
 *
 * CLAUSE (design/BIBLE.md sec 8): sim/encounter/clauses.ts's WIRING NOTE asks the integration
 * lane (createRun, via this file) to thread one EncounterRules into EncounterState so
 * `activeClauseId` rides inside the encounter lane's durable state, exactly as it already does
 * for `pullIndex`/`slackTicks`. `options.clauseId` is looked up in `content.clauses` (content
 * DATA, passed in like every other RunContent field -- sim/CLAUDE.md: "sim never imports
 * content") and folded onto the base rules via clauses.ts's `applyClause`, which itself throws
 * `ClauseAlreadyActiveError` if `content.encounterRules` already carries an active Clause. An
 * unknown clause id throws here too (sim/CLAUDE.md invariant 4: "a rule the sim cannot resolve
 * deterministically is a bug, never a coin flip" -- never a silent no-Clause default).
 *
 * LOADOUT (design/BIBLE.md sec 6/7): content/apply.ts's `applyLoadout(classDef, abilities,
 * facets, talentChoices, gear)` needs a ClassDef, which sim never has (sim never imports
 * content). Hosts that own a content registry (client/src/sim-host/run-setup.ts,
 * server/src/content-setup.ts, the screen) call `applyLoadout` THEMSELVES and hand
 * `options.loadouts` sim's own already-resolved shape -- `Record<ActorId,
 * Record<abilityId, AbilityDef>>`, exactly `RunContent.abilities`'s own value type -- so
 * `mergeLoadoutAbilities` below only ever merges plain AbilityDef data, never touches content/**
 * or a ClassDef. DEVIATION from the dispatch-specified `{talentChoices, gear}` options shape:
 * that shape needs a ClassDef to resolve, which would require sim to import content/apply.ts --
 * forbidden by sim/CLAUDE.md's "sim never imports content" (already documented as fact in
 * sim/index.ts's own file docstring). This keeps createRun a pure function of data, same as every
 * other RunContent field.
 *
 * A loadout keyed to an actorId absent from `party`, or an ability id absent from the run's base
 * `content.abilities`, throws (same fail-closed reasoning) instead of being silently dropped -- a
 * caller wiring bug (a typo'd actor id, a stale ability id) is loud, not a quietly-ignored no-op.
 * When two party members share a class and BOTH have loadouts, the later member (party array
 * order, so deterministic) wins that ability id's patched definition -- `RulesConfig.abilities`
 * (sim/combat/rules.ts) is one global ability-id map for the whole run, not per-actor, and making
 * ability resolution per-actor is a sim/combat/** change outside this lane's ownership; flagged
 * here as content/CLAUDE.md's own KNOWN DRIFT convention, not silently patched over.
 *
 * Usage: `const rules = resolveEncounterRules(content, keyLevel, options?.clauseId);`
 *        `const abilities = mergeLoadoutAbilities(content.abilities, party, options?.loadouts);`
 */
import type { ActorId } from "./core/state";
import type { AbilityDef } from "./combat/abilities";
import type { ClauseDef, EncounterRules } from "./encounter/clauses";
import { applyClause, defaultEncounterRules } from "./encounter/clauses";
import type { PartyMemberSpec, RunContent } from "./run-types";

/** Options a host may pass to `createRun` -- see file docstring for both fields' full contract. */
export interface RunOptions {
  /** A `content.clauses` key (content/data/clauses.json's id); throws if unknown, or if
   * `content.encounterRules` already carries an active Clause (clauses.ts's
   * `ClauseAlreadyActiveError`). Omit for no Clause -- the run's existing default behaviour. */
  clauseId?: string;
  /** [actorId] -> that actor's already-loadout-resolved abilities (content/apply.ts's
   * `applyLoadout(...).abilities`, called by the host). Omit (or omit one actor's entry) to leave
   * that actor on `content.abilities`'s base kit. */
  loadouts?: Record<ActorId, Record<string, AbilityDef>>;
}

export class UnknownClauseError extends Error {
  constructor(clauseId: string) {
    super(`createRun: options.clauseId "${clauseId}" is not in content.clauses`);
    this.name = "UnknownClauseError";
  }
}

export class UnknownLoadoutActorError extends Error {
  constructor(actorId: ActorId) {
    super(`createRun: options.loadouts has an entry for actorId ${actorId}, which is not in party`);
    this.name = "UnknownLoadoutActorError";
  }
}

export class UnknownLoadoutAbilityError extends Error {
  constructor(actorId: ActorId, abilityId: string) {
    super(`createRun: options.loadouts[${actorId}] patches ability "${abilityId}", which is not in content.abilities`);
    this.name = "UnknownLoadoutAbilityError";
  }
}

/** Resolves the run's starting EncounterRules: `options.clauseId` (looked up in `content.clauses`)
 * folded via `applyClause` onto `content.encounterRules ?? defaultEncounterRules(keyLevel)`, or
 * that base returned unchanged when no clauseId is given. Throws `UnknownClauseError` for an id
 * absent from `content.clauses` -- never silently runs with no Clause. */
export function resolveEncounterRules(content: RunContent, keyLevel: number, clauseId?: string): EncounterRules {
  const base = content.encounterRules ?? defaultEncounterRules(keyLevel);
  if (clauseId === undefined) return base;
  const clause: ClauseDef | undefined = content.clauses?.[clauseId];
  if (!clause) throw new UnknownClauseError(clauseId);
  return applyClause(base, clause);
}

/** Merges each listed actor's pre-resolved loadout abilities onto `base` (content.abilities),
 * validated against `party` and `base` itself -- see file docstring. Returns `base` unchanged
 * (same reference) when `loadouts` is omitted or empty, so `createRun` skips rebuilding
 * `RunContent` on the common no-loadout path. Validates every entry BEFORE merging anything, so a
 * bad entry never leaves a partially-applied `abilities` map behind. */
export function mergeLoadoutAbilities(
  base: Record<string, AbilityDef>,
  party: PartyMemberSpec[],
  loadouts?: Record<ActorId, Record<string, AbilityDef>>,
): Record<string, AbilityDef> {
  if (!loadouts || Object.keys(loadouts).length === 0) return base;

  const partyIds = new Set(party.map((m) => m.id));
  for (const key of Object.keys(loadouts)) {
    const actorId = Number(key) as ActorId;
    if (!partyIds.has(actorId)) throw new UnknownLoadoutActorError(actorId);
    const loadout = loadouts[actorId];
    for (const abilityId of Object.keys(loadout ?? {})) {
      if (!(abilityId in base)) throw new UnknownLoadoutAbilityError(actorId, abilityId);
    }
  }

  const merged = { ...base };
  for (const member of party) {
    const loadout = loadouts[member.id];
    if (loadout) Object.assign(merged, loadout);
  }
  return merged;
}
