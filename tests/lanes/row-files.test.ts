/**
 * tests/lanes/row-files.test.ts
 * Usage: `npx vitest run tests/lanes`. Lanes never edit the game/CLAUDE.md lane list or
 * BUILDLOG.md in their PR; each lane writes game/lanes/<branch-with-slashes-as-dashes>.md
 * instead, and a later fold lane merges the rows (see game/lanes/README.md). These tests
 * pin the row-file shape the fold lane depends on: a README that states the convention, and
 * every row file holding exactly two non-empty lines, a lane bullet then a BUILDLOG line.
 */
import { describe, expect, it } from "vitest";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const LANES_DIR = fileURLToPath(new URL("../../game/lanes", import.meta.url));

function rowFiles(): string[] {
  return readdirSync(LANES_DIR).filter((name) => name.endsWith(".md") && name !== "README.md");
}

function nonEmptyLines(path: string): string[] {
  return readFileSync(path, "utf8")
    .split(/\r?\n/)
    .filter((line) => line.trim() !== "");
}

describe("game/lanes row files", () => {
  it("game/lanes/README.md exists and states the convention", () => {
    const readme = join(LANES_DIR, "README.md");
    expect(existsSync(readme)).toBe(true);
    expect(readFileSync(readme, "utf8")).toContain("game/lanes/");
  });

  it("the example row file for this lane is present", () => {
    expect(rowFiles()).toContain("chore-lane-row-files.md");
  });

  it("every row file is exactly two non-empty lines: a lane bullet, then a BUILDLOG line", () => {
    const files = rowFiles();
    expect(files.length).toBeGreaterThan(0);
    for (const name of files) {
      const lines = nonEmptyLines(join(LANES_DIR, name));
      expect(lines.length, `${name} line count`).toBe(2);
      expect(lines[0]?.startsWith("- ") ?? false, `${name} line 1 is a lane bullet`).toBe(true);
      expect(lines[1]?.startsWith("BUILDLOG: ") ?? false, `${name} line 2 is a BUILDLOG line`).toBe(true);
    }
  });
});
