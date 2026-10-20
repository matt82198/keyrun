/**
 * tests/oracle/format.ts -- the parity-oracle wire format shared by every oracle fixture writer.
 *
 * game/sim/ORACLE.md is the normative spec; this file is the TypeScript reference implementation
 * of it. Three pieces:
 *   - fnv1a32(text): FNV-1a 32-bit over the UTF-16 code units of an ASCII string (== its bytes),
 *     returned as 8 lowercase hex chars.
 *   - projectionLine(tick, encounter, actors): the per-tick projection line
 *     `t=<tick>;pull=<i>;penalty=<n>;a=<actor>|<actor>|...` (penalty: the time deaths and wipes have
 *     added to the run clock, STATE.md D46; 0 before any) with each actor rendered as
 *     `<id>,<hp>,<x>,<z>,<yaw>,<alive>,<res>,<castAbility|->,<castEnd|->` and actors sorted by id.
 *   - canonicalJson(value): sorted-key compact JSON (undefined members dropped), used for every
 *     command/event/setup payload so a reader in another language can re-render it byte-for-byte.
 *
 * Every integer field goes through fmtInt, which REFUSES anything that is not a safe integer
 * (a float, NaN, Infinity) -- the oracle must never quietly record a non-integer sim value as if
 * it were exact. -0 renders as "0".
 *
 * Usage: `import { fnv1a32, projectionLine, canonicalJson } from "./format";`
 */

/** Minimal actor shape the projection reads (a structural subset of sim/core/state.ts ActorState). */
export interface ProjectedActor {
  id: number;
  health: number;
  pos: { x: number; z: number };
  yaw: number;
  alive: boolean;
  resource: number;
  castId: string | null;
  castEndsTick: number | null;
}

/** Minimal encounter shape the projection reads (sim/encounter EncounterState subset). */
export interface ProjectedEncounter {
  pullIndex: number;
  /** Run-clock added time (sim/encounter/timer.ts chargeDeath/chargeWipe); absent until the first death. */
  penaltyTicks?: number;
}

const FNV_OFFSET = 0x811c9dc5;
const FNV_PRIME = 0x01000193;

/** FNV-1a 32 over an ASCII string. Throws on any code unit > 0x7f (the format is ASCII-only). */
export function fnv1a32(text: string, seed: number = FNV_OFFSET): string {
  return fnv1a32Raw(text, seed).toString(16).padStart(8, "0");
}

/** Same as fnv1a32 but returns the uint32 state, so a caller can chain lines into one trace hash. */
export function fnv1a32Raw(text: string, seed: number = FNV_OFFSET): number {
  let hash = seed >>> 0;
  for (let i = 0; i < text.length; i += 1) {
    const c = text.charCodeAt(i);
    if (c > 0x7f) throw new Error(`fnv1a32: non-ASCII code unit 0x${c.toString(16)} at ${i}`);
    hash = Math.imul((hash ^ c) >>> 0, FNV_PRIME) >>> 0;
  }
  return hash;
}

/** Decimal rendering of a safe integer; refuses non-integers. -0 renders "0". */
export function fmtInt(n: number, what: string): string {
  if (!Number.isSafeInteger(n)) throw new Error(`oracle: ${what} is not a safe integer: ${String(n)}`);
  return Object.is(n, -0) ? "0" : String(n);
}

const ID_TOKEN = /^[A-Za-z0-9._:-]+$/;

function fmtCastId(castId: string | null, actorId: number): string {
  if (castId === null) return "-";
  if (!ID_TOKEN.test(castId) || castId === "-") {
    throw new Error(`oracle: actor ${actorId} castId ${JSON.stringify(castId)} is not a projection-safe token`);
  }
  return castId;
}

/** One actor's projection field group (no separators around it). */
export function projectActor(a: ProjectedActor): string {
  const id = fmtInt(a.id, "actor id");
  return [
    id,
    fmtInt(a.health, `actor ${id} hp`),
    fmtInt(a.pos.x, `actor ${id} x`),
    fmtInt(a.pos.z, `actor ${id} z`),
    fmtInt(a.yaw, `actor ${id} yaw`),
    a.alive ? "1" : "0",
    fmtInt(a.resource, `actor ${id} res`),
    fmtCastId(a.castId, a.id),
    a.castEndsTick === null ? "-" : fmtInt(a.castEndsTick, `actor ${id} castEnd`),
  ].join(",");
}

/** The full per-tick projection line (see file docstring and game/sim/ORACLE.md). */
export function projectionLine(tick: number, encounter: ProjectedEncounter, actors: Iterable<ProjectedActor>): string {
  const sorted = [...actors].sort((p, q) => p.id - q.id);
  const body = sorted.map(projectActor).join("|");
  return `t=${fmtInt(tick, "tick")};pull=${fmtInt(encounter.pullIndex, "pullIndex")};penalty=${fmtInt(encounter.penaltyTicks ?? 0, "penaltyTicks")};a=${body}`;
}

/**
 * Sorted-key compact JSON. Object members whose value is undefined are dropped (JSON.stringify
 * semantics); functions, symbols, bigints and non-finite numbers throw rather than being lost.
 * Numbers use JS's shortest round-trip rendering (integers print without a fraction; -0 prints 0).
 * Strings are escaped with JSON.stringify and must be ASCII.
 */
export function canonicalJson(value: unknown): string {
  if (value === null) return "null";
  switch (typeof value) {
    case "number":
      if (!Number.isFinite(value)) throw new Error(`canonicalJson: non-finite number ${String(value)}`);
      return JSON.stringify(value);
    case "boolean":
      return value ? "true" : "false";
    case "string": {
      for (let i = 0; i < value.length; i += 1) {
        if (value.charCodeAt(i) > 0x7f) throw new Error(`canonicalJson: non-ASCII string ${JSON.stringify(value)}`);
      }
      return JSON.stringify(value);
    }
    case "object": {
      if (Array.isArray(value)) return `[${value.map((v) => (v === undefined ? "null" : canonicalJson(v))).join(",")}]`;
      const obj = value as Record<string, unknown>;
      const parts: string[] = [];
      for (const k of Object.keys(obj).sort()) {
        const v = obj[k];
        if (v === undefined) continue;
        parts.push(`${canonicalJson(k)}:${canonicalJson(v)}`);
      }
      return `{${parts.join(",")}}`;
    }
    default:
      throw new Error(`canonicalJson: unsupported ${typeof value}`);
  }
}
