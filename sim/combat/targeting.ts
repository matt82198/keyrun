/**
 * Tab-target cycling and integer shape queries (circle/cone/line) over actor positions in the
 * XZ plane, plus resolveTargets: the single ability-shape-to-actor-list resolver shared by
 * casting.ts (completeCast) and windows.ts (applyWindowsAndCombos) so both apply an ability's
 * "self"/"target"/"circle"/"cone"/"line" shape identically. Facing direction comes from a fixed
 * 128-entry Q12 (x4096) cos/sin lookup table, generated once offline and embedded as literal
 * constants below, indexed by yaw in angle1024 units (0..1023 = one full turn, matching
 * AbilityDef.shape.angle1024). This keeps facing math integer-only at runtime and
 * platform-deterministic (no Math.sin/cos call during simulation).
 *
 * Faction filter (P0 fix): circle/cone/line shapes resolve to every actor in radius/geometry, not
 * just enemies -- a damage/debuff ability must exclude allies (and the caster) and a heal/buff
 * ability must exclude enemies, or Cinder's cinderfield floor patch burns its own party and enemy
 * cleaves hit other enemies. sim/combat/affinity.ts classifies each ability's dominant Affinity
 * (hostile/friendly) and resolveTargets applies it after the existing geometry query; "self" and
 * "target" shapes are unaffected (already a single explicit actor). Result ids are sorted
 * ascending for determinism, matching cycleTarget's existing convention below.
 * Usage: inCircle(origin, radiusMm, actors); inCone(caster, angle1024, radiusMm, actors);
 *        cycleTarget(actors, currentTargetId); resolveTargets(state, actor, abilityDef).
 */
import type { ActorId, ActorState, SimState, Vec3 } from "./types";
import type { AbilityDef } from "./abilities";
import { isqrt } from "./movement";
import { abilityAffinity, matchesAffinity } from "./affinity";

const TABLE_N = 128;
const STEP = 1024 / TABLE_N;
export const FACING_Q = 4096;

const COS = [
  4096, 4091, 4076, 4052, 4017, 3973, 3920, 3857, 3784, 3703, 3612, 3513, 3406, 3290, 3166, 3035,
  2896, 2751, 2598, 2440, 2276, 2106, 1931, 1751, 1567, 1380, 1189, 995, 799, 601, 401, 201,
  0, -201, -401, -601, -799, -995, -1189, -1380, -1567, -1751, -1931, -2106, -2276, -2440, -2598, -2751,
  -2896, -3035, -3166, -3290, -3406, -3513, -3612, -3703, -3784, -3857, -3920, -3973, -4017, -4052, -4076, -4091,
  -4096, -4091, -4076, -4052, -4017, -3973, -3920, -3857, -3784, -3703, -3612, -3513, -3406, -3290, -3166, -3035,
  -2896, -2751, -2598, -2440, -2276, -2106, -1931, -1751, -1567, -1380, -1189, -995, -799, -601, -401, -201,
  0, 201, 401, 601, 799, 995, 1189, 1380, 1567, 1751, 1931, 2106, 2276, 2440, 2598, 2751,
  2896, 3035, 3166, 3290, 3406, 3513, 3612, 3703, 3784, 3857, 3920, 3973, 4017, 4052, 4076, 4091,
];

const SIN = [
  0, 201, 401, 601, 799, 995, 1189, 1380, 1567, 1751, 1931, 2106, 2276, 2440, 2598, 2751,
  2896, 3035, 3166, 3290, 3406, 3513, 3612, 3703, 3784, 3857, 3920, 3973, 4017, 4052, 4076, 4091,
  4096, 4091, 4076, 4052, 4017, 3973, 3920, 3857, 3784, 3703, 3612, 3513, 3406, 3290, 3166, 3035,
  2896, 2751, 2598, 2440, 2276, 2106, 1931, 1751, 1567, 1380, 1189, 995, 799, 601, 401, 201,
  0, -201, -401, -601, -799, -995, -1189, -1380, -1567, -1751, -1931, -2106, -2276, -2440, -2598, -2751,
  -2896, -3035, -3166, -3290, -3406, -3513, -3612, -3703, -3784, -3857, -3920, -3973, -4017, -4052, -4076, -4091,
  -4096, -4091, -4076, -4052, -4017, -3973, -3920, -3857, -3784, -3703, -3612, -3513, -3406, -3290, -3166, -3035,
  -2896, -2751, -2598, -2440, -2276, -2106, -1931, -1751, -1567, -1380, -1189, -995, -799, -601, -401, -201,
];

function tableIndex(angle1024: number): number {
  return (((Math.round(angle1024 / STEP) % TABLE_N) + TABLE_N) % TABLE_N);
}

/** Returns the caster's Q12-scaled facing direction vector in the XZ plane. */
export function faceDir(yaw1024: number): { x: number; z: number } {
  const idx = tableIndex(yaw1024);
  return { x: COS[idx]!, z: SIN[idx]! };
}

export function inCircle(origin: Vec3, radiusMm: number, actors: ActorState[]): ActorId[] {
  const r2 = radiusMm * radiusMm;
  return actors
    .filter((a) => {
      const dx = a.pos.x - origin.x;
      const dz = a.pos.z - origin.z;
      return dx * dx + dz * dz <= r2;
    })
    .map((a) => a.id);
}

export function inCone(caster: ActorState, angle1024: number, radiusMm: number, actors: ActorState[]): ActorId[] {
  const face = faceDir(caster.yaw);
  const cosHalf = COS[tableIndex(angle1024 / 2)]!;
  const r2 = radiusMm * radiusMm;
  return actors
    .filter((a) => {
      if (a.id === caster.id) return false;
      const dx = a.pos.x - caster.pos.x;
      const dz = a.pos.z - caster.pos.z;
      const dist2 = dx * dx + dz * dz;
      if (dist2 === 0 || dist2 > r2) return false;
      const dist = isqrt(dist2);
      const dot = face.x * dx + face.z * dz;
      return dot >= cosHalf * dist;
    })
    .map((a) => a.id);
}

/** `radiusMm` here is the line's half-width; targets within [0, length] ahead and within the width. */
export function inLine(caster: ActorState, length: number, radiusMm: number, actors: ActorState[]): ActorId[] {
  const face = faceDir(caster.yaw);
  return actors
    .filter((a) => {
      if (a.id === caster.id) return false;
      const dx = a.pos.x - caster.pos.x;
      const dz = a.pos.z - caster.pos.z;
      const forward = Math.trunc((face.x * dx + face.z * dz) / FACING_Q);
      const perp = Math.trunc((face.x * dz - face.z * dx) / FACING_Q);
      return forward >= 0 && forward <= length && Math.abs(perp) <= radiusMm;
    })
    .map((a) => a.id);
}

/**
 * Resolves an ability's shape (self/target/circle/cone/line) against `state` to the concrete
 * ActorState list it applies to. "target" reads `actor.targetId` (set by startCast from its
 * explicit targetId argument, or left over from a prior cast/tab-target). Shared by casting.ts's
 * completeCast (an ability's own effects) and windows.ts's applyWindowsAndCombos (Window/Payoff
 * ops on that same ability) so both resolve identical target sets for the same input.
 *
 * circle/cone/line additionally apply `def`'s Affinity (see affinity.ts): a hostile ability keeps
 * only actors of the opposing `kind` (dropping allies and the caster); a friendly one keeps only
 * the caster's own `kind` (including the caster, even for cone/line shapes whose raw geometry
 * query excludes it -- see the explicit `add(actor.id)` below).
 *
 * Dead actors (`alive === false`) never resolve as a "target" or circle/cone/line target: a
 * target that died mid-cast resolves to [] (the same outcome as no targetId), and corpses do not
 * soak AoE. "self" is unchanged -- the caster acting on itself, reachable while dead only via
 * rules.ts's onDeath DEATH-CAST EXCEPTION; damage.ts already makes heal/damage on it no-ops.
 */
export function resolveTargets(state: SimState, actor: ActorState, def: AbilityDef): ActorState[] {
  if (def.shape.kind === "self") return [actor];
  if (def.shape.kind === "target") {
    const t = actor.targetId !== null ? state.actors[actor.targetId] : undefined;
    return t && t.alive ? [t] : [];
  }
  const all = Object.values(state.actors).filter((a) => a.alive);
  let ids: ActorId[];
  if (def.shape.kind === "circle") {
    ids = inCircle(actor.pos, def.shape.radius ?? 0, all);
  } else if (def.shape.kind === "cone") {
    ids = inCone(actor, def.shape.angle1024 ?? 0, def.shape.radius ?? 0, all);
  } else {
    ids = inLine(actor, def.shape.length ?? 0, def.shape.radius ?? 0, all);
  }
  const affinity = abilityAffinity(def);
  const kept = new Set(ids.filter((id) => {
    const candidate = state.actors[id];
    return !!candidate && matchesAffinity(affinity, actor, candidate);
  }));
  if (affinity === "friendly" && actor.alive) kept.add(actor.id);
  return [...kept]
    .sort((a, b) => a - b)
    .map((id) => state.actors[id])
    .filter((a): a is ActorState => !!a);
}

/** Cycles to the next living actor id after `fromId` in ascending id order (wraps around). */
export function cycleTarget(actors: ActorState[], fromId: ActorId | null): ActorId | null {
  const candidates = actors.filter((a) => a.alive).map((a) => a.id).sort((x, y) => x - y);
  if (candidates.length === 0) return null;
  if (fromId === null) return candidates[0]!;
  const idx = candidates.indexOf(fromId);
  const nextIdx = idx === -1 ? 0 : (idx + 1) % candidates.length;
  return candidates[nextIdx]!;
}
