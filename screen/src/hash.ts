/**
 * Usage: import { hashString, seededPick } from "./hash.js";
 *
 * Tiny deterministic string hash and bounded pick, shared by craft.ts (facet reroll), progression.ts
 * (reward roll seed) and clauses.ts (Clause offer seed). Not a PRNG on its own -- callers that need
 * a real bit-mixed sequence feed this hash's output into sim/core/prng.ts's `seedState`/`nextInt`.
 */
export function hashString(basis: string): number {
  let hash = 0;
  for (let i = 0; i < basis.length; i += 1) {
    hash = (hash * 31 + basis.charCodeAt(i)) >>> 0;
  }
  return hash;
}

/** Tiny deterministic integer pick, seed -> [0, n). Used where a full PRNG state isn't needed. */
export function seededPick(seed: number, n: number): number {
  if (n <= 0) return 0;
  const mixed = Math.imul(seed ^ 0x9e3779b9, 0x85ebca6b) >>> 0;
  return mixed % n;
}
