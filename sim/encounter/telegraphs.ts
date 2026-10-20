/**
 * sim/encounter/telegraphs.ts — active telegraph registry and pure integer-geometry resolution.
 *
 * `EncounterState` is the encounter-lane's slice of durable run state (composed into the full sim by
 * sim/encounter/index.ts). `resolveTelegraphs(state, encounter)` returns, for every telegraph whose
 * `resolvesTick <= state.tick`, the list of actors it hits — using only integer arithmetic (squared
 * distances, no sqrt; direction vectors from faceDir's baked fixed-point table, no
 * runtime trig calls) so results are bit-identical across machines.
 *
 * Facing convention: the ONE sim convention, sim/combat/targeting.ts's faceDir (yaw 0 faces +x,
 * yaw 256 faces +z; matches sim/core/fixed.ts's yawBetween = atan2(dz, dx)). A telegraph's yaw is
 * the caster's combat yaw copied verbatim (sim/encounter/scripts.ts), so the facing vector comes
 * from faceDir itself rather than a second table -- the old local (sin, cos) reading resolved
 * every cone/line 90 degrees off the caster's real facing.
 * Cone containment requires the FULL cone angle (`angle1024`) to be < 512 (180 degrees) — a
 * documented limitation of the squared-dot-product trick used to avoid sqrt; every cone in the
 * design bible (Cleave, etc.) is well under that.
 *
 * Usage: `const hits = resolveTelegraphs(state, encounter);`
 */
import type { ActorId, SimState, Tick, Vec3 } from "./types";
import type { BreakBarState } from "./breakbar";
import type { EchoSpawnRequest, EncounterRules } from "./clauses";
import { faceDir } from "../combat/targeting";

export interface Telegraph {
  id: number;
  sourceId: ActorId;
  abilityId: string;
  shape: "circle" | "cone" | "line";
  origin: Vec3;
  yaw: number;
  radius?: number;
  angle1024?: number;
  length?: number;
  width?: number;
  resolvesTick: Tick;
}

export interface EncounterState {
  telegraphs: Telegraph[];
  breakBars: Record<string, BreakBarState>;
  pullIndex: number;
  slackTicks: number;
  parTicks: number;
  keyLevel: number;
  depleted: boolean;
  rules: EncounterRules;
  /** Player deaths this run, each counted (MPLUS-DEATH-PENALTY, STATE.md D46). Optional, defaults
   * to 0 wherever read (timer.ts), same convention as `firedOnDeath` below. */
  deaths?: number;
  /** Time deaths and wipes have added to the run clock, ticks (STATE.md D46). Optional, defaults
   * to 0 wherever read (timer.ts). */
  penaltyTicks?: number;
  /** [actorId] -> ability ids whose "onDeath" script step has already fired for that actor
   * (death-trigger-wiring): threaded into sim/encounter/scripts.ts's `stepEnemyScripts` each tick
   * so an "onDeath" step's FIRE-ONCE CONTRACT (see that file's docstring) survives across ticks
   * instead of re-firing every tick after death. Optional (defaults to `{}` wherever read, see
   * sim/encounter/index.ts's `stepEncounter`) so every EncounterState object literal elsewhere in
   * this codebase predating this field keeps compiling unchanged; `createRun` sets it explicitly
   * on every real run. */
  firedOnDeath?: Record<ActorId, string[]>;
  /** Echo (design/BIBLE.md sec 8): weak-add spawn requests queued from this run's interrupted
   * casts, not yet realized as an actor -- sim/encounter/index.ts's `stepEchoSpawns` reads and
   * writes this every tick. Optional, defaults to `[]` wherever read, same convention as
   * `firedOnDeath` above. */
  pendingEchoSpawns?: EchoSpawnRequest[];
  /** Next actor id `stepEchoSpawns` will assign to a realized Echo add -- starts at
   * sim/encounter/index.ts's `ECHO_ACTOR_ID_BASE`, far above any dungeon/party id, so a spawned
   * add never collides with one. */
  nextEchoActorId?: number;
  /** Next `Telegraph.id` sim/encounter/index.ts's `stepEncounter` will mint (starts at 1). Run
   * state, never a module global, so two runs in one process (two server rooms) mint identical,
   * seed-deterministic id sequences. Optional, defaults to 1 wherever read, same convention as
   * `firedOnDeath` above; `createRun` sets it explicitly. */
  nextTelegraphId?: number;
}

const SCALE = 4096;
const QUARTER = 256; // angle1024 units in a 90-degree quarter turn
const STEP = 8;
// cos(i*STEP degrees-in-1024ths) for i in 0..32, i.e. cos(0deg..90deg) scaled by SCALE.
const COS_Q = [
  4096, 4091, 4076, 4052, 4017, 3973, 3920, 3857, 3784, 3703, 3612, 3513, 3406, 3290, 3166, 3035,
  2896, 2751, 2598, 2440, 2276, 2106, 1931, 1751, 1567, 1380, 1189, 995, 799, 601, 401, 201, 0,
];

function cosQ(r: number): number {
  const idx = Math.min(COS_Q.length - 1, Math.round(r / STEP));
  return COS_Q[idx] ?? 0;
}


export function pointInCircle(origin: Vec3, radius: number, p: Vec3): boolean {
  const dx = p.x - origin.x;
  const dz = p.z - origin.z;
  return dx * dx + dz * dz <= radius * radius;
}

/** Facing vector = faceDir(yaw) (yaw=0 -> +x, yaw=256 -> +z), scaled by SCALE. Full cone angle < 512. */
export function pointInCone(origin: Vec3, yaw: number, angle1024: number, radius: number, p: Vec3): boolean {
  const dx = p.x - origin.x;
  const dz = p.z - origin.z;
  const distSq = dx * dx + dz * dz;
  if (distSq > radius * radius) return false;
  if (distSq === 0) return true;
  const f = faceDir(yaw);
  const dot = dx * f.x + dz * f.z; // scaled by SCALE
  if (dot < 0) return false;
  const halfAngle = Math.min(511, Math.max(0, angle1024)) / 2;
  const cosHalf = cosQ(halfAngle <= QUARTER ? halfAngle : QUARTER); // clamp: half-angle assumed <= 90deg
  return dot * dot >= distSq * cosHalf * cosHalf;
}

export function pointInLine(origin: Vec3, yaw: number, length: number, width: number, p: Vec3): boolean {
  const dx = p.x - origin.x;
  const dz = p.z - origin.z;
  const f = faceDir(yaw);
  const forward = dx * f.x + dz * f.z; // scaled by SCALE
  const lateral = dx * f.z - dz * f.x; // scaled by SCALE
  if (forward < 0 || forward > length * SCALE) return false;
  return Math.abs(lateral) * 2 <= width * SCALE;
}

export interface TelegraphHit {
  telegraphId: number;
  hitActorIds: ActorId[];
}

export function resolveTelegraphs(state: SimState, encounter: EncounterState): TelegraphHit[] {
  const results: TelegraphHit[] = [];
  for (const tg of encounter.telegraphs) {
    if (tg.resolvesTick > state.tick) continue;
    const hitActorIds: ActorId[] = [];
    for (const key of Object.keys(state.actors)) {
      const actor = state.actors[Number(key)];
      if (!actor || !actor.alive || actor.id === tg.sourceId) continue;
      let hit = false;
      if (tg.shape === "circle") hit = pointInCircle(tg.origin, tg.radius ?? 0, actor.pos);
      else if (tg.shape === "cone") hit = pointInCone(tg.origin, tg.yaw, tg.angle1024 ?? 0, tg.radius ?? 0, actor.pos);
      else hit = pointInLine(tg.origin, tg.yaw, tg.length ?? 0, tg.width ?? 0, actor.pos);
      if (hit) hitActorIds.push(actor.id);
    }
    results.push({ telegraphId: tg.id, hitActorIds });
  }
  return results;
}
