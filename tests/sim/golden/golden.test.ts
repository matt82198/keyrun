/**
 * tests/sim/golden/golden.test.ts — loads tests/sim/golden/scripted-pull.json (see that file's
 * sibling README.md for the fixture format) and recomputes the final state hash through the real
 * composition root (sim/index.ts's createRun/stepRun), pinning sim/index + sim/combat +
 * sim/encounter's combined behaviour the same way tests/sim/core/replay.test.ts pins sim/core
 * alone. The scenario (party/dungeon/content) lives in tests/sim/integration/fixtures.ts and is
 * shared with tests/sim/integration/scripted-pull.test.ts so both suites drive the exact same run.
 */
import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { createRun, stepRun } from "@sim/index";
import { hashState } from "@sim/core/state";
import type { Command } from "@sim/core/commands";
import { BASE_PARTY, CONTENT, DUNGEON, SEED, TICKS, scriptedPlayerCommands } from "../integration/fixtures";

interface GoldenFixture {
  seed: number;
  ticks: number;
  commands: Command[];
  expectedHash: string;
}

const fixturePath = fileURLToPath(new URL("./scripted-pull.json", import.meta.url));
const fixture = JSON.parse(readFileSync(fixturePath, "utf8")) as GoldenFixture;

describe("golden replay: scripted-pull", () => {
  it("fixture matches the live scenario (seed, tick count, command stream)", () => {
    expect(fixture.seed).toBe(SEED);
    expect(fixture.ticks).toBe(TICKS);
    expect(fixture.commands).toEqual(scriptedPlayerCommands());
  });

  it("recomputes the pinned final-state hash", () => {
    let run = createRun(fixture.seed, BASE_PARTY, DUNGEON, CONTENT);
    for (let t = 0; t < fixture.ticks; t += 1) {
      const tickCommands = fixture.commands.filter((c) => c.tick === t);
      run = stepRun(run, tickCommands).run;
    }
    expect(hashState(run.state)).toBe(fixture.expectedHash);
  });
});
