/**
 * tests/oracle/oracle.test.ts -- the parity-oracle drift guard (ENGINE-PLAN lane 1B, risk R6).
 *
 * Regenerates EVERY oracle fixture from the frozen TypeScript sim/bots/content into a fresh temp
 * directory and asserts each file is byte-identical to the committed copy in
 * game/sim/Keyrun.Sim.Tests/Fixtures/oracle/. Any behaviour change in the frozen TS (or any edit to
 * a frozen source file, via manifest.json's frozenSourceHash) turns this red inside `npm test`, the
 * required `test` check. Also pins the wire format itself (FNV-1a test vectors, projection line
 * rendering) so the spec in game/sim/ORACLE.md and this implementation cannot silently disagree.
 *
 * Re-record deliberately (state the reason in the PR): `ORACLE_WRITE=1 npx vitest run tests/oracle`.
 * Usage: `npx vitest run tests/oracle`
 */
import { beforeAll, describe, expect, it } from "vitest";
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { canonicalJson, fmtInt, fnv1a32, projectionLine } from "./format";
import {
  FROZEN_ROOTS,
  NON_GENERATED,
  ORACLE_FIXTURE_DIR,
  buildFixtures,
  frozenSourceHashes,
  hashMapDrift,
  renderManifest,
  writeFixtures,
} from "./generate";
import { gitMergeFile } from "./merge-file";
import { SOURCE_SHA } from "./traces";

describe("oracle wire format", () => {
  it("fnv1a32 matches the published FNV-1a 32-bit test vectors", () => {
    expect(fnv1a32("")).toBe("811c9dc5");
    expect(fnv1a32("a")).toBe("e40c292c");
    expect(fnv1a32("foobar")).toBe("bf9cf968");
  });

  it("fnv1a32 refuses non-ASCII input", () => {
    expect(() => fnv1a32(`caf${String.fromCharCode(0xe9)}`)).toThrow(/non-ASCII/);
  });

  it("renders a projection line with actors sorted by id, '-' for no cast, 1/0 for alive", () => {
    const line = projectionLine(12, { pullIndex: 1, penaltyTicks: 900 }, [
      { id: 900, health: 0, pos: { x: -5, z: 7 }, yaw: 1023, alive: false, resource: 0, castId: null, castEndsTick: null },
      { id: 2, health: 400, pos: { x: 100, z: -2500 }, yaw: 0, alive: true, resource: 75, castId: "warden.strike", castEndsTick: 30 },
    ]);
    expect(line).toBe("t=12;pull=1;penalty=900;a=2,400,100,-2500,0,1,75,warden.strike,30|900,0,-5,7,1023,0,0,-,-");
  });

  it("projects the run clock's added time (D46), 0 before any death or wipe, and no Slack bank", () => {
    expect(projectionLine(0, { pullIndex: 0 }, [])).toBe("t=0;pull=0;penalty=0;a=");
    expect(projectionLine(0, { pullIndex: 0, slackTicks: 620 } as never, [])).not.toMatch(/slack/);
  });

  it("refuses non-integer fields and renders -0 as 0", () => {
    expect(() => fmtInt(1.5, "x")).toThrow(/not a safe integer/);
    expect(() => fmtInt(Number.NaN, "x")).toThrow(/not a safe integer/);
    expect(fmtInt(-0, "x")).toBe("0");
  });

  it("canonicalJson sorts keys, drops undefined members, refuses non-finite numbers", () => {
    expect(canonicalJson({ b: 1, a: [true, null, "x"], c: undefined })).toBe('{"a":[true,null,"x"],"b":1}');
    expect(() => canonicalJson({ a: Number.POSITIVE_INFINITY })).toThrow(/non-finite/);
  });
});

/** Writes a throwaway tree holding every FROZEN_ROOTS entry plus `overrides` (path -> text); returns its root. */
function frozenTree(overrides: Record<string, string>): string {
  const root = mkdtempSync(join(tmpdir(), "keyrun-frozen-"));
  const files: Record<string, string> = {};
  for (const r of FROZEN_ROOTS) files[r.endsWith(".ts") ? r : `${r}/anchor.ts`] = "export {};\n";
  Object.assign(files, overrides);
  for (const [rel, text] of Object.entries(files)) {
    mkdirSync(dirname(join(root, rel)), { recursive: true });
    writeFileSync(join(root, rel), text);
  }
  return root;
}

describe("frozen-source hashes ignore documentation (.md) files", () => {
  it("a .md-only edit under a frozen root leaves frozenSourceHashes unchanged", () => {
    const root = frozenTree({ "sim/engine.ts": "export const x = 1;\n", "content/CLAUDE.md": "docs v1\n" });
    try {
      const before = frozenSourceHashes(root);
      writeFileSync(join(root, "content/CLAUDE.md"), "docs v2, prose only\n");
      expect(hashMapDrift(before, frozenSourceHashes(root))).toEqual([]);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("a non-.md edit under a frozen root still changes frozenSourceHashes", () => {
    const root = frozenTree({ "sim/engine.ts": "export const x = 1;\n", "content/data/tuning.json": "{}\n" });
    try {
      const before = frozenSourceHashes(root);
      writeFileSync(join(root, "content/data/tuning.json"), '{"hp":2}\n');
      expect(hashMapDrift(before, frozenSourceHashes(root))).toEqual(["changed content/data/tuning.json"]);
      const beforeSim = frozenSourceHashes(root);
      writeFileSync(join(root, "sim/engine.ts"), "export const x = 2;\n");
      expect(hashMapDrift(beforeSim, frozenSourceHashes(root))).toEqual(["changed sim/engine.ts"]);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});

describe("frozen-source drift gate: per file, fail-closed, merge-friendly", () => {
  const twoContentFiles = { "content/data/a.json": '{"hp":1}\n', "content/data/b.json": '{"hp":1}\n' };

  it("a content edit with stale (not regenerated) fixtures still fails the drift check", () => {
    const root = frozenTree(twoContentFiles);
    try {
      const recorded = frozenSourceHashes(root); // what the committed manifest was generated from
      writeFileSync(join(root, "content/data/a.json"), '{"hp":2}\n'); // a content edit, no ORACLE_WRITE
      expect(hashMapDrift(recorded, frozenSourceHashes(root))).toEqual(["changed content/data/a.json"]);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("an added or a removed frozen source file also fails the drift check", () => {
    const root = frozenTree(twoContentFiles);
    try {
      const recorded = frozenSourceHashes(root);
      writeFileSync(join(root, "content/data/c.json"), "{}\n");
      rmSync(join(root, "content/data/b.json"));
      expect(hashMapDrift(recorded, frozenSourceHashes(root))).toEqual([
        "removed content/data/b.json",
        "added content/data/c.json",
      ]);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("the derived content-pull golden is not a frozen source (golden.test.ts gates it against live content)", () => {
    const root = frozenTree({ "tests/sim/golden/content-pull.json": "{}\n", "tests/sim/golden/scripted-pull.json": "{}\n" });
    try {
      const listed = Object.keys(frozenSourceHashes(root));
      expect(listed).toContain("tests/sim/golden/scripted-pull.json"); // an input the traces read
      expect(listed).not.toContain("tests/sim/golden/content-pull.json");
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it("two independent content edits on separate branches merge manifest.json without a conflict", () => {
    const root = frozenTree({ ...twoContentFiles, "content/data/m.json": "{}\n" });
    try {
      const manifestFor = (): string =>
        renderManifest({ kind: "keyrun-oracle-manifest", source: { frozenSources: frozenSourceHashes(root) }, version: 1 });
      const base = manifestFor();
      writeFileSync(join(root, "content/data/a.json"), '{"hp":2}\n'); // branch A edits a.json
      const ours = manifestFor();
      writeFileSync(join(root, "content/data/a.json"), '{"hp":1}\n');
      writeFileSync(join(root, "content/data/b.json"), '{"hp":3}\n'); // branch B edits b.json
      const theirs = manifestFor();
      writeFileSync(join(root, "content/data/a.json"), '{"hp":2}\n');
      const both = manifestFor(); // what regenerating on the merged tree writes
      expect(ours).not.toBe(base);
      expect(theirs).not.toBe(base);
      const merged = gitMergeFile(base, ours, theirs);
      expect(merged.clean, merged.text).toBe(true);
      expect(merged.text).toBe(both);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});

describe("oracle fixtures match the frozen TypeScript sim byte for byte", () => {
  let tempDir = "";
  let generated: Record<string, string> = {};

  beforeAll(() => {
    generated = buildFixtures();
    if (process.env.ORACLE_WRITE === "1") writeFixtures(ORACLE_FIXTURE_DIR, generated);
    tempDir = mkdtempSync(join(tmpdir(), "keyrun-oracle-"));
    writeFixtures(tempDir, generated);
    return () => rmSync(tempDir, { recursive: true, force: true });
  }, 300_000);

  it("the frozen sources hash to the values the committed fixtures were generated from", () => {
    const committed = JSON.parse(readFileSync(join(ORACLE_FIXTURE_DIR, "manifest.json"), "utf8")) as {
      source: { sha: string; frozenSources: Record<string, { fnv: string }> };
    };
    expect(committed.source.sha).toBe(SOURCE_SHA);
    expect(Object.keys(committed.source.frozenSources ?? {}).length).toBeGreaterThan(100);
    expect(
      hashMapDrift(committed.source.frozenSources, frozenSourceHashes()),
      "these files under the frozen TS roots (manifest.json source.frozenRoots) changed since the oracle was recorded; the TS is FROZEN (ENGINE-PLAN s3)",
    ).toEqual([]);
  });

  it("the committed fixture set is exactly the generated set", () => {
    const committed = readdirSync(ORACLE_FIXTURE_DIR).filter((f) => !NON_GENERATED.has(f)).sort();
    expect(committed).toEqual(readdirSync(tempDir).sort());
  });

  it("every regenerated file is byte-identical to its committed copy", () => {
    // manifest.json last: a behaviour drift should be reported against the trace/vector it hit.
    const names = readdirSync(tempDir).sort((p, q) => Number(p === "manifest.json") - Number(q === "manifest.json") || p.localeCompare(q));
    expect(names.length).toBeGreaterThan(10);
    const drifted: string[] = [];
    for (const name of names) {
      const fresh = readFileSync(join(tempDir, name));
      const committed = readFileSync(join(ORACLE_FIXTURE_DIR, name));
      if (fresh.equals(committed)) continue;
      const a = fresh.toString("utf8").split("\n");
      const b = committed.toString("utf8").split("\n");
      // Report the first diverging tick record (line 2+) before the header, which only summarises it.
      let i = 1;
      while (i < Math.max(a.length, b.length) && a[i] === b[i]) i += 1;
      if (i >= Math.max(a.length, b.length)) i = 0;
      const la = a[i] ?? "<eof>";
      const lb = b[i] ?? "<eof>";
      let col = 0;
      while (col < la.length && la[col] === lb[col]) col += 1;
      const from = Math.max(0, col - 120);
      drifted.push(
        `${name} line ${i + 1} col ${col + 1}:\n  regenerated: ...${la.slice(from, col + 200)}\n  committed:   ...${lb.slice(from, col + 200)}`,
      );
    }
    expect(drifted, `oracle fixtures drifted from the frozen TS:\n${drifted.join("\n")}`).toEqual([]);
  });

  it("every committed trace is self-consistent: full projections hash to their record's h", () => {
    const traces = readdirSync(ORACLE_FIXTURE_DIR).filter((f) => f.endsWith(".trace.jsonl"));
    expect(traces.length).toBe(5);
    for (const name of traces) {
      const lines = readFileSync(join(ORACLE_FIXTURE_DIR, name), "utf8").split("\n").filter((l) => l.length > 0);
      const header = JSON.parse(lines[0]!) as { source: { sha: string }; records: number; traceHash: string };
      expect(header.source.sha, name).toBe(SOURCE_SHA);
      expect(lines.length - 1, name).toBe(header.records);
      let fulls = 0;
      for (const l of lines.slice(1)) {
        const rec = JSON.parse(l) as { t: number; h: string; p?: string };
        if (rec.p === undefined) continue;
        fulls += 1;
        expect(rec.p.startsWith(`t=${rec.t};`), `${name} t=${rec.t}`).toBe(true);
        expect(fnv1a32(rec.p), `${name} t=${rec.t}`).toBe(rec.h);
      }
      expect(fulls, name).toBeGreaterThan(1);
    }
  });
});
