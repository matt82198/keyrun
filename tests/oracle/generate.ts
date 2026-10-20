/**
 * tests/oracle/generate.ts -- assembles every parity-oracle fixture (vectors, traces, setups, the
 * manifest) in memory and, on request, writes them to a directory. The committed copy lives in
 * game/sim/Keyrun.Sim.Tests/Fixtures/oracle/ (ORACLE_FIXTURE_DIR).
 *
 * manifest.json records, per file, its byte size and FNV-1a 32 over its bytes; per trace, its
 * trace hash, record count, final tick and run result; the loadContent() counts lane 1D compares
 * against; and `frozenSources`, one FNV-1a 32 per frozen TypeScript source file the fixtures were
 * generated from (CR stripped so a CRLF checkout hashes like an LF one; *.md files excluded, since
 * documentation under a frozen root never changes behaviour). Any other edit under those paths
 * changes that file's entry, which the drift test reports, by path, as a freeze violation. The
 * manifest is written one member per line (renderManifest), so two PRs that edit different source
 * files change different manifest lines and merge without a conflict (see tests/sim/golden/hash-map.ts).
 *
 * Usage:
 *   - Re-record (deliberately, with a reason in the PR): `ORACLE_WRITE=1 npx vitest run tests/oracle`
 *   - In code: `writeFixtures(dir)` / `buildFixtures()`.
 */
import { mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { loadContent } from "@content/index";
import { canonicalJson } from "./format";
import { buildTraces, ORACLE_VERSION, SOURCE_SHA } from "./traces";
import { buildVectors } from "./vectors";
import { hashMapDrift, sortedHashMap, type HashMap } from "../sim/golden/hash-map";

export { hashMapDrift };

export const REPO_ROOT = fileURLToPath(new URL("../../", import.meta.url));
export const ORACLE_FIXTURE_DIR = join(REPO_ROOT, "game", "sim", "Keyrun.Sim.Tests", "Fixtures", "oracle");

/** Repo-relative roots whose content the fixtures depend on (the frozen TS + the scenario sources). */
export const FROZEN_ROOTS = [
  "sim",
  "bots/src",
  "content",
  "client/src/sim-host",
  "client/src/input",
  "tests/sim/golden",
  "tests/sim/integration/fixtures.ts",
];

/** Files in ORACLE_FIXTURE_DIR that are hand-maintained, not generated (never compared or rewritten). */
export const NON_GENERATED = new Set([".gitattributes"]);

/** FNV-1a 32 over raw bytes. */
export function fnvBytes(bytes: Uint8Array, seed = 0x811c9dc5): number {
  let h = seed >>> 0;
  for (const b of bytes) h = Math.imul((h ^ b) >>> 0, 0x01000193) >>> 0;
  return h;
}

const hex = (n: number): string => (n >>> 0).toString(16).padStart(8, "0");

function listFiles(abs: string): string[] {
  const st = statSync(abs);
  if (st.isFile()) return [abs];
  const out: string[] = [];
  for (const name of readdirSync(abs)) out.push(...listFiles(join(abs, name)));
  return out;
}

/**
 * Files under FROZEN_ROOTS that are generated OUTPUTS, not sources: no fixture reads them, and each has
 * its own gate that recomputes it from the live sources. content-pull.json changes on every content PR
 * (golden.test.ts checks its contentHashes against loadContent()), so hashing it here would put the same
 * conflicting line back into the manifest that per-file entries remove.
 */
export const DERIVED_UNDER_FROZEN_ROOTS = new Set(["tests/sim/golden/content-pull.json"]);

/**
 * One FNV-1a 32 per non-.md source file under FROZEN_ROOTS (minus DERIVED_UNDER_FROZEN_ROOTS), keyed by
 * repo-relative path and sorted: the hash covers the path, byte length and CR-stripped bytes.
 */
export function frozenSourceHashes(root: string = REPO_ROOT): HashMap {
  const files = FROZEN_ROOTS.flatMap((r) => listFiles(join(root, r)))
    .map((abs) => relative(root, abs).split(sep).join("/"))
    .filter((rel) => !rel.endsWith(".md") && !DERIVED_UNDER_FROZEN_ROOTS.has(rel));
  const enc = new TextEncoder();
  return sortedHashMap(
    files.map((rel): [string, string] => {
      const raw = readFileSync(join(root, rel));
      const lf = Uint8Array.from(raw.filter((b) => b !== 0x0d));
      return [rel, hex(fnvBytes(lf, fnvBytes(enc.encode(`${rel}\n${lf.length}\n`))))];
    }),
  );
}

/** manifest.json's text: sorted keys (canonicalJson) laid out one member per line, trailing newline. */
export function renderManifest(manifest: object): string {
  return `${JSON.stringify(JSON.parse(canonicalJson(manifest)), null, 2)}\n`;
}

function contentCounts(): Record<string, number> {
  const reg = loadContent();
  return {
    abilities: reg.abilities.size,
    effects: reg.effects.size,
    classes: reg.classes.size,
    combos: reg.combos.size,
    enemies: reg.enemies.size,
    dungeons: reg.dungeons.size,
    clauses: reg.clauses.size,
    facets: reg.facets.size,
  };
}

/** Every generated fixture file, filename -> exact text (LF line endings, trailing newline). */
export function buildFixtures(): Record<string, string> {
  const files: Record<string, string> = { ...buildVectors() };
  const traces = buildTraces();
  for (const t of traces) {
    for (const [name, text] of Object.entries(t.files)) {
      const prior = files[name];
      if (prior !== undefined && prior !== text) throw new Error(`oracle: two scenarios disagree on shared file ${name}`);
      files[name] = text;
    }
  }
  const enc = new TextEncoder();
  const listing: Record<string, { bytes: number; fnv: string }> = {};
  for (const name of Object.keys(files).sort()) {
    const bytes = enc.encode(files[name]!);
    listing[name] = { bytes: bytes.length, fnv: hex(fnvBytes(bytes)) };
  }
  const manifest = {
    kind: "keyrun-oracle-manifest",
    version: ORACLE_VERSION,
    source: { sha: SOURCE_SHA, frozenRoots: FROZEN_ROOTS, frozenSources: frozenSourceHashes() },
    contentCounts: contentCounts(),
    traces: traces.map((t) => ({
      name: t.name,
      trace: t.traceFile,
      setup: t.setupFile,
      traceHash: t.traceHash,
      records: t.records,
      finalTick: t.finalTick,
      result: t.result,
    })),
    files: listing,
  };
  files["manifest.json"] = renderManifest(manifest);
  return files;
}

/** Writes every fixture into `dir` (created if missing). Returns the filenames written. */
export function writeFixtures(dir: string, files: Record<string, string> = buildFixtures()): string[] {
  mkdirSync(dir, { recursive: true });
  for (const [name, text] of Object.entries(files)) writeFileSync(join(dir, name), text, "utf8");
  return Object.keys(files).sort();
}
