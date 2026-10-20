/**
 * tests/sim/golden/hash-map.ts -- the merge-friendly shape shared by the two generated hash files that
 * pin "what the fixtures were recorded from": the oracle manifest's `source.frozenSources` (one entry per
 * frozen source file, tests/oracle/generate.ts) and content-pull.json's `contentHashes` (one entry per
 * content registry entry, build-content-pull.ts).
 *
 * Why per entry instead of one whole-tree hash: a single hash line changes on every content PR, so any
 * two content PRs conflict on it. With one `"<key>": { "fnv": "<hash>" }` entry per input, rendered by
 * JSON.stringify(_, null, 2) with sorted keys, each hash sits on its own line with unchanged lines
 * around it, so two edits to different inputs change different, non-adjacent lines and git merges them.
 * The gate stays fail-closed: hashMapDrift reports every added, removed or changed key, and the tests
 * require it to be empty.
 *
 * Usage: `hashMapDrift(recorded, live)` -> ["changed content/data/x.json", "added ...", "removed ..."].
 */

/** Input key -> its FNV-1a 32 hash, wrapped in an object so each hash renders between unchanged lines. */
export type HashMap = Record<string, { fnv: string }>;

/** Sorts a HashMap's keys so the rendered JSON is stable and diffs stay local to the edited entry. */
export function sortedHashMap(entries: Iterable<[string, string]>): HashMap {
  const out: HashMap = {};
  for (const [key, fnv] of [...entries].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))) out[key] = { fnv };
  return out;
}

/** Every key whose hash differs between `recorded` and `live`, as "removed k" / "added k" / "changed k", sorted by key. */
export function hashMapDrift(recorded: HashMap | undefined, live: HashMap): string[] {
  const before = recorded ?? {};
  const keys = [...new Set([...Object.keys(before), ...Object.keys(live)])].sort();
  const out: string[] = [];
  for (const k of keys) {
    const a = before[k]?.fnv;
    const b = live[k]?.fnv;
    if (a === undefined) out.push(`added ${k}`);
    else if (b === undefined) out.push(`removed ${k}`);
    else if (a !== b) out.push(`changed ${k}`);
  }
  return out;
}
