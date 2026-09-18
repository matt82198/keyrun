/**
 * sim/encounter/telegraphs.ts — active telegraph registry and pure integer-geometry resolution.
 *
 * `EncounterState` is the encounter-lane's slice of durable run state (composed into the full sim by
 * sim/encounter/index.ts). `resolveTelegraphs(state, encounter)` returns, for every telegraph whose
 * `resolvesTick <= state.tick`, the list of actors it hits — using only integer arithmetic (squared
 * distances, no sqrt; direction vectors from a baked fixed-point quarter-turn cosine table, no
 * runtime trig calls) so results are bit-identical across machines.
 *
 * Facing convention: yaw 0 faces +z; increasing yaw (angle1024, 1024 units/turn) rotates toward +x.
 * Cone containment requires the FULL cone angle (`angle1024`) to be < 512 (180 degrees) — a
 * documented limitation of the squared-dot-product trick used to avoid sqrt; every cone in the
 * design bible (Cleave, etc.) is well under that.
 *
 * Usage: `const hits = resolveTelegraphs(state, encounter);`
 */
import type { ActorId, SimState, Tick, Vec3 } from "./types";
import type { BreakBarState } from "./breakbar";

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

function normYaw(yaw: number): number {
  const m = yaw % 1024;
  return m < 0 ? m + 1024 : m;
}

/** Returns [cosScaled, sinScaled] for a yaw in angle1024 units, each scaled by SCALE. */
function cosSin1024(yaw: number): [number, number] {
  const t = normYaw(yaw);
  const q = Math.floor(t / QUARTER);
  const r = t - q * QUARTER;
  switch (q) {
    case 0:
      return [cosQ(r), cosQ(QUARTER - r)];
    case 1:
      return [-cosQ(QUARTER - r), cosQ(r)];
    case 2:
      return [-cosQ(r), -cosQ(QUARTER - r)];
    default:
      return [cosQ(QUARTER - r), -cosQ(r)];
  }
}

export function pointInCircle(origin: Vec3, radius: number, p: Vec3): boolean {
  const dx = p.x - origin.x;
  const dz = p.z - origin.z;
  return dx * dx + dz * dz <= radius * radius;
}

/** dirX/dirZ = facing vector (yaw=0 -> +z, yaw=256 -> +x), each scaled by SCALE. Full cone angle < 512. */
export function pointInCone(origin: Vec3, yaw: number, angle1024: number, radius: number, p: Vec3): boolean {
  const dx = p.x - origin.x;
  const dz = p.z - origin.z;
  const distSq = dx * dx + dz * dz;
  if (distSq > radius * radius) return false;
  if (distSq === 0) return true;
  const [cosYaw, sinYaw] = cosSin1024(yaw);
  const dot = dx * sinYaw + dz * cosYaw; // scaled by SCALE
  if (dot < 0) return false;
  const halfAngle = Math.min(511, Math.max(0, angle1024)) / 2;
  const cosHalf = cosQ(halfAngle <= QUARTER ? halfAngle : QUARTER); // clamp: half-angle assumed <= 90deg
  return dot * dot >= distSq * cosHalf * cosHalf;
}

export function pointInLine(origin: Vec3, yaw: number, length: number, width: number, p: Vec3): boolean {
  const dx = p.x - origin.x;
  const dz = p.z - origin.z;
  const [cosYaw, sinYaw] = cosSin1024(yaw);
  const forward = dx * sinYaw + dz * cosYaw; // scaled by SCALE
  const lateral = dx * cosYaw - dz * sinYaw; // scaled by SCALE
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
