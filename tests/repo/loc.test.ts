// Usage: vitest suite enforcing the sim/CLAUDE.md "<=190 LOC per file" rule across source roots,
// with a shrinking allowlist for pre-existing violators (see sim/CLAUDE.md "Layout").
import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

const LOC_LIMIT = 190;

// Roots scanned for the LOC gate, relative to the repo root.
const ROOTS = ["sim", "bots", "client/src", "content", "screen/src"];

// Directories never descended into, regardless of root.
const EXCLUDED_DIR_NAMES = new Set(["node_modules", "dist"]);

// Pre-existing violators at the LOC count they had when this gate was added. An allowlisted file
// fails the gate if it GROWS past its recorded count here -- it may only shrink from here on. When
// a file's real line count drops to <=190, the second test below fails, forcing its entry to be
// removed: the allowlist only ever shrinks, never grows or gets re-padded.
const ALLOWLIST: Record<string, number> = {
  "screen/src/rules.ts": 291,
  "client/src/sim-host/worker.ts": 312,
  "screen/src/panels/run.ts": 235,
};

function countLines(content: string): number {
  const lines = content.split("\n");
  if (lines[lines.length - 1] === "") lines.pop(); // trailing newline at EOF isn't an extra line
  return lines.length;
}

function walk(dir: string, out: string[]): void {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    const stat = statSync(full);
    if (stat.isDirectory()) {
      if (EXCLUDED_DIR_NAMES.has(entry)) continue;
      walk(full, out);
    } else if (entry.endsWith(".ts") && !entry.endsWith(".test.ts")) {
      out.push(full);
    }
  }
}

function listSourceFiles(): string[] {
  const files: string[] = [];
  for (const root of ROOTS) {
    const rootDir = join(process.cwd(), root);
    try {
      statSync(rootDir);
    } catch {
      continue; // root not present yet (e.g. server/ before it exists) -- nothing to scan
    }
    walk(rootDir, files);
  }
  return files;
}

function repoRelative(absolutePath: string): string {
  const cwd = process.cwd();
  const rel = absolutePath.startsWith(cwd) ? absolutePath.slice(cwd.length + 1) : absolutePath;
  return rel.split("\\").join("/");
}

describe("repo: LOC gate (sim/CLAUDE.md <=190 LOC per file)", () => {
  it("no file exceeds the cap, except recorded allowlist entries which may not grow", () => {
    const files = listSourceFiles();
    expect(files.length).toBeGreaterThan(0);

    const violations: string[] = [];
    for (const absPath of files) {
      const rel = repoRelative(absPath);
      const loc = countLines(readFileSync(absPath, "utf8"));
      const allowedLoc = ALLOWLIST[rel];
      if (allowedLoc === undefined) {
        if (loc > LOC_LIMIT) violations.push(`${rel}: ${loc} LOC (cap ${LOC_LIMIT}, not allowlisted)`);
      } else if (loc > allowedLoc) {
        violations.push(`${rel}: ${loc} LOC (grew past allowlisted ${allowedLoc} LOC)`);
      }
    }
    expect(violations).toEqual([]);
  });

  it("every allowlist entry is still a genuine violator (remove entries that shrank to <=190)", () => {
    const stale: string[] = [];
    for (const rel of Object.keys(ALLOWLIST)) {
      const loc = countLines(readFileSync(join(process.cwd(), rel), "utf8"));
      if (loc <= LOC_LIMIT) {
        stale.push(`${rel}: now ${loc} LOC (<=${LOC_LIMIT}) -- remove from ALLOWLIST instead of keeping it`);
      }
    }
    expect(stale).toEqual([]);
  });
});
