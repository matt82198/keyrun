/**
 * tests/lanes/row-files.test.ts
 * Usage: `npx vitest run tests/lanes`. Lanes never edit the game/CLAUDE.md lane list or
 * BUILDLOG.md in their PR; each lane writes game/lanes/<branch-with-slashes-as-dashes>.md
 * instead, and a later fold lane merges the rows (see game/lanes/README.md). Zero row files
 * is the normal post-fold state (D29: a fold empties game/lanes of consumed rows), so these
 * tests never require a row file to exist. They pin the row-file shape a fold lane depends on:
 * a README that states the convention, and every row file that does exist holding exactly two
 * non-empty lines, a lane bullet then a BUILDLOG line. The shape checker is also exercised on
 * inline samples, so it is proven to fail on malformed rows even when game/lanes holds none.
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

/** Returns the shape violations of one row file's non-empty lines; [] means well-formed. */
function rowShapeErrors(lines: string[]): string[] {
  const errors: string[] = [];
  if (lines.length !== 2) errors.push(`line count ${lines.length}, expected 2`);
  if (!(lines[0] ?? "").startsWith("- ")) errors.push("line 1 is not a lane bullet");
  if (!(lines[1] ?? "").startsWith("BUILDLOG: ")) errors.push("line 2 is not a BUILDLOG line");
  return errors;
}

describe("game/lanes row files", () => {
  it("game/lanes/README.md exists and states the convention", () => {
    const readme = join(LANES_DIR, "README.md");
    expect(existsSync(readme)).toBe(true);
    expect(readFileSync(readme, "utf8")).toContain("game/lanes/");
  });

  it("every row file that exists is exactly two non-empty lines: a lane bullet, then a BUILDLOG line", () => {
    for (const name of rowFiles()) {
      expect(rowShapeErrors(nonEmptyLines(join(LANES_DIR, name))), name).toEqual([]);
    }
  });

  it("the shape checker accepts a well-formed row and rejects malformed ones", () => {
    expect(rowShapeErrors(["- X bullet", "BUILDLOG: y"])).toEqual([]);
    expect(rowShapeErrors(["- X bullet"])).not.toEqual([]);
    expect(rowShapeErrors(["X bullet", "BUILDLOG: y"])).not.toEqual([]);
    expect(rowShapeErrors(["- X bullet", "y"])).not.toEqual([]);
    expect(rowShapeErrors(["- X bullet", "BUILDLOG: y", "extra"])).not.toEqual([]);
  });
});
