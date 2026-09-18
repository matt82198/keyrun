/**
 * sim/encounter/script-target.ts — resolves the targetId an enemy script's cast command should
 * carry (the P0 fix: sim/combat/casting.ts's startCast refuses any `shape.kind === "target"`
 * ability with no targetId, so an enemy script that never supplies one never lands).
 *
 * Default rules (no `ScriptStep.target` override): a "friendly"/heal ability (any effect op
 * `"heal"`) targets the lowest-health living ally enemy, falling back to itself when no other
 * living ally exists; every other ("hostile") ability targets the enemy's highest-threat living
 * player (sim/combat/threat.ts), falling back to the nearest living player. A self-shaped ability
 * (`shape.kind === "self"`) always targets itself, bypassing both pools.
 *
 * Overrides (`ScriptStep.target`, only honoured when content actually sets one -- none does today):
 * `"self"` forces itself regardless of shape/classification; `"threat"` forces the hostile
 * (player) pool with threat/nearest selection even on an otherwise-friendly ability; `"random"`
 * keeps the normal pool but picks uniformly among in-range candidates via a PRNG draw derived
 * from `state.prng`, `state.tick` and the caster's id (deterministic, no `state` mutation needed --
 * see the WIRING NOTE below for why this can't thread `state.prng` itself forward).
 *
 * `ability` (the caster's AbilityDef, when known) unlocks two refinements: range gating (a
 * candidate outside `ability.range` is excluded; if that empties an otherwise-required pool, the
 * caller should skip the cast this tick -- see `blocked` -- rather than spam a doomed command) and
 * precise shape handling (a known non-target, non-self shape, e.g. circle/cone/line, needs no
 * resolved target and is left alone). WIRING NOTE (same pattern as this file's neighbour
 * scripts.ts's `firedOnDeath`/onDeath gaps): sim/encounter/index.ts's `stepEncounter` and
 * sim/index.ts's `stepRun` never pass an `abilities` registry into `stepEnemyScripts` today (out
 * of scope for this lane -- both files are owned elsewhere), so every call through the real game
 * loop runs with `ability` undefined. That is deliberately safe: with no ability data, this module
 * falls back to the always-correct-enough default (hostile threat/nearest targeting whenever a
 * living player exists, exactly what fixes the P0 defect end-to-end today) and never blocks a
 * command, matching the pre-fix behaviour of always emitting on schedule. The precise,
 * range-aware, heal-classified ruleset above is fully implemented and unit-tested here and in
 * scripts.ts against an explicit `abilities` map; a follow-up integration lane threading
 * `content.abilities` through `stepEncounter`/`stepRun` (a one-line change at each call site) is
 * what turns it on for real content, the same follow-up already flagged for `firedOnDeath`.
 *
 * Usage: `const { targetId, blocked } = resolveScriptTarget({ state, caster, override, ability });`
 */
import type { ActorId, ActorState, SimState, Tick } from "./types";
import type { AbilityDef } from "../combat/abilities";
import { rangeMm } from "../combat/casting";
import { nextInt, type PrngState } from "../core/prng";

export type ScriptTargetOverride = "self" | "threat" | "random";

export interface ResolveScriptTargetInput {
  state: SimState;
  caster: ActorState;
  override?: ScriptTargetOverride;
  /** The caster's AbilityDef for the ability about to be cast, when the caller has one. See the
   * file docstring's WIRING NOTE for why the real game loop calls this with `ability` undefined. */
  ability?: AbilityDef;
}

export interface ResolveScriptTargetResult {
  targetId: ActorId | undefined;
  /** True when a target was required (a known `"target"`-shaped ability) and none could be
   * resolved -- even after range gating -- so the caller should not emit a cast command this tick. */
  blocked: boolean;
}

function isHealAbility(ability: AbilityDef | undefined): boolean {
  return ability !== undefined && ability.effects.some((op) => op.op === "heal");
}

function inRange(caster: ActorState, candidate: ActorState, ability: AbilityDef | undefined): boolean {
  return ability === undefined || rangeMm(caster, candidate) <= ability.range;
}

function livingPlayers(state: SimState): ActorState[] {
  return Object.values(state.actors).filter((a) => a.alive && a.kind === "player");
}

function livingAllies(state: SimState, casterId: ActorId): ActorState[] {
  return Object.values(state.actors).filter((a) => a.alive && a.kind === "enemy" && a.id !== casterId);
}

function nearestId(caster: ActorState, candidates: ActorState[]): ActorId {
  return candidates
    .slice()
    .sort((a, b) => rangeMm(caster, a) - rangeMm(caster, b) || a.id - b.id)[0]!.id;
}

function lowestHealthId(candidates: ActorState[]): ActorId {
  return candidates.slice().sort((a, b) => a.health - b.health || a.id - b.id)[0]!.id;
}

function highestThreatLivingId(caster: ActorState, candidateIds: Set<ActorId>): ActorId | null {
  let best: ActorId | null = null;
  let bestVal = -Infinity;
  for (const key of Object.keys(caster.threat)) {
    const id = Number(key);
    if (!candidateIds.has(id)) continue;
    const val = caster.threat[id] ?? 0;
    if (val > bestVal || (val === bestVal && best !== null && id < best)) {
      bestVal = val;
      best = id;
    }
  }
  return best;
}

/** Derives a deterministic per-(state, caster) PRNG draw without mutating or threading `state`
 * itself -- see the file docstring's WIRING NOTE. */
function randomId(state: SimState, casterId: ActorId, candidates: ActorState[]): ActorId {
  const [p0, p1, p2, p3] = state.prng;
  const tick = state.tick as Tick;
  let seed: PrngState = [(p0 ^ tick) >>> 0, (p1 ^ casterId) >>> 0, p2 >>> 0, p3 >>> 0];
  if (seed[0] === 0 && seed[1] === 0 && seed[2] === 0 && seed[3] === 0) seed = [1, seed[1], seed[2], seed[3]];
  const sorted = candidates.slice().sort((a, b) => a.id - b.id);
  const [idx] = nextInt(seed, sorted.length);
  return sorted[idx]!.id;
}

function threatThenNearest(caster: ActorState, candidates: ActorState[]): ActorId {
  const ids = new Set(candidates.map((a) => a.id));
  return highestThreatLivingId(caster, ids) ?? nearestId(caster, candidates);
}

export function resolveScriptTarget(input: ResolveScriptTargetInput): ResolveScriptTargetResult {
  const { state, caster, override, ability } = input;

  if (override === "self") return { targetId: caster.id, blocked: false };
  if (ability?.shape.kind === "self") return { targetId: caster.id, blocked: false };

  const requiresTarget = ability?.shape.kind === "target";
  const heal = isHealAbility(ability) && override !== "threat";

  if (ability !== undefined && !requiresTarget && !heal && override === undefined) {
    return { targetId: undefined, blocked: false }; // circle/cone/line: no target needed, leave alone
  }

  const pool = heal ? livingAllies(state, caster.id) : livingPlayers(state);
  const candidates = pool.filter((a) => inRange(caster, a, ability));

  let picked: ActorId | null = null;
  if (candidates.length > 0) {
    picked =
      override === "random"
        ? randomId(state, caster.id, candidates)
        : heal
          ? lowestHealthId(candidates)
          : threatThenNearest(caster, candidates);
  }

  if (picked !== null) return { targetId: picked, blocked: false };
  if (heal) return { targetId: caster.id, blocked: false }; // "else self"
  if (ability !== undefined) return { targetId: undefined, blocked: true }; // target required, none in range
  return { targetId: undefined, blocked: false }; // ability unknown: preserve legacy cast-anyway behaviour
}
