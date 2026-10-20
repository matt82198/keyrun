/**
 * tests/sim/golden/golden.test.ts — loads tests/sim/golden/scripted-pull.json (see that file's
 * sibling README.md for the fixture format) and recomputes the final state hash through the real
 * composition root (sim/index.ts's createRun/stepRun), pinning sim/index + sim/combat +
 * sim/encounter's combined behaviour the same way tests/sim/core/replay.test.ts pins sim/core
 * alone. The scenario (party/dungeon/content) lives in tests/sim/integration/fixtures.ts and is
 * shared with tests/sim/integration/scripted-pull.test.ts so both suites drive the exact same run.
 *
 * Also loads tests/sim/golden/content-pull.json (see its own "why" field and
 * tests/sim/golden/build-content-pull.ts, the fixture's generator/recorder): the same
 * golden-replay contract, but through the REAL content/ registry instead of fixtures.ts's
 * hand-authored one-ability content, so an edit to any real ability/effect/combo/enemy/dungeon
 * file is guaranteed to hash-differ (contentHash) and any silent behaviour change in how that
 * content plays is guaranteed to either hash-differ or event-count-differ. Deviation from the
 * backlog item's requested event-type list: this sim has no "cast_start" event (only
 * "cast_complete"/"cast_stopped" -- see sim/combat/casting.ts); "cast_complete" is asserted in its
 * place.
 */
import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { createRun, stepRun } from "@sim/index";
import { hashState } from "@sim/core/state";
import type { Command } from "@sim/core/commands";
import { BASE_PARTY, CONTENT, DUNGEON, SEED, TICKS, scriptedPlayerCommands } from "../integration/fixtures";
import {
  SEED as CONTENT_SEED,
  TICKS as CONTENT_TICKS,
  buildCommands,
  contentHashes,
  eventCounts,
  renderContentPull,
  runScenario,
} from "./build-content-pull";
import { hashMapDrift, type HashMap } from "./hash-map";
import { loadContent, type ContentRegistry } from "@content/index";
import { gitMergeFile } from "../../oracle/merge-file";

interface GoldenFixture {
  seed: number;
  ticks: number;
  commands: Command[];
  expectedHash: string;
}

interface ContentGoldenFixture extends GoldenFixture {
  contentHashes: HashMap;
  eventCounts: Record<string, number>;
  why: string;
}

const fixturePath = fileURLToPath(new URL("./scripted-pull.json", import.meta.url));
const fixture = JSON.parse(readFileSync(fixturePath, "utf8")) as GoldenFixture;

const contentFixturePath = fileURLToPath(new URL("./content-pull.json", import.meta.url));
const contentFixture = JSON.parse(readFileSync(contentFixturePath, "utf8")) as ContentGoldenFixture;

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

describe("golden replay: content-pull (real content/ registry)", () => {
  it("fixture matches the live scenario (seed, ticks, command stream, content version)", () => {
    expect(contentFixture.seed).toBe(CONTENT_SEED);
    expect(contentFixture.ticks).toBe(CONTENT_TICKS);
    expect(contentFixture.commands).toEqual(buildCommands());
    // Hashes every real registry entry: any content/ edit changes one, forcing a deliberate
    // re-record (npx tsx tests/sim/golden/build-content-pull.ts) instead of a silent drift.
    expect(Object.keys(contentFixture.contentHashes ?? {}).length).toBeGreaterThan(100);
    expect(
      hashMapDrift(contentFixture.contentHashes, contentHashes()),
      "these content entries changed since content-pull.json was recorded; re-record it with npx tsx tests/sim/golden/build-content-pull.ts",
    ).toEqual([]);
  });

  it("recomputes the pinned final-state hash and event counts by type", () => {
    const { run, events } = runScenario();
    const counts = eventCounts(events);
    expect(hashState(run.state)).toBe(contentFixture.expectedHash);
    expect(counts).toEqual(contentFixture.eventCounts);
    // Named individually so a regression in any one of these reads as a labelled diff, not just
    // a bulk object mismatch.
    expect(counts.cast_complete ?? 0).toBe(contentFixture.eventCounts.cast_complete ?? 0);
    expect(counts.combo ?? 0).toBe(contentFixture.eventCounts.combo ?? 0);
    expect(counts.damage ?? 0).toBe(contentFixture.eventCounts.damage ?? 0);
    expect(counts.death ?? 0).toBe(contentFixture.eventCounts.death ?? 0);
  });

  it("produces the same final-state hash on two independent runs (determinism)", () => {
    const a = runScenario();
    const b = runScenario();
    expect(hashState(a.run.state)).toBe(hashState(b.run.state));
  });
});

/** A copy of `reg` with ability `id`'s cooldown moved by `delta` ticks (a one-field content edit). */
function withCooldownEdit(reg: ContentRegistry, id: string, delta: number): ContentRegistry {
  const abilities = new Map(reg.abilities);
  const def = abilities.get(id)!;
  abilities.set(id, { ...def, cooldownTicks: def.cooldownTicks + delta });
  return { ...reg, abilities };
}

describe("content-pull contentHashes: per entry, fail-closed, merge-friendly", () => {
  const reg = loadContent();
  // Two abilities that sort next to each other: the hardest case for a line-based merge.
  const [idA, idB] = [...reg.abilities.keys()].sort().slice(0, 2) as [string, string];

  it("a content edit with a stale (not re-recorded) content-pull.json still fails the drift check", () => {
    const recorded = contentHashes(reg); // what the committed fixture was recorded from
    const edited = withCooldownEdit(reg, idA, 5); // a content edit, no re-record
    expect(hashMapDrift(recorded, contentHashes(edited))).toEqual([`changed abilities/${idA}`]);
  });

  it("an added or a removed content entry also fails the drift check", () => {
    const recorded = contentHashes(reg);
    const abilities = new Map(reg.abilities);
    const def = abilities.get(idB)!;
    abilities.delete(idB);
    abilities.set("zz.new", { ...def, id: "zz.new" });
    expect(hashMapDrift(recorded, contentHashes({ ...reg, abilities }))).toEqual([
      `removed abilities/${idB}`,
      "added abilities/zz.new",
    ]);
  });

  it("two independent content edits on separate branches merge content-pull.json without a conflict", () => {
    const render = (r: ContentRegistry): string => renderContentPull({ ...contentFixture, contentHashes: contentHashes(r) });
    const base = render(reg);
    const ours = render(withCooldownEdit(reg, idA, 5)); // branch A edits one ability
    const theirs = render(withCooldownEdit(reg, idB, 7)); // branch B edits the next one
    const both = render(withCooldownEdit(withCooldownEdit(reg, idA, 5), idB, 7));
    expect(ours).not.toBe(base);
    expect(theirs).not.toBe(base);
    const merged = gitMergeFile(base, ours, theirs);
    expect(merged.clean, merged.text).toBe(true);
    expect(merged.text).toBe(both);
  });
});
