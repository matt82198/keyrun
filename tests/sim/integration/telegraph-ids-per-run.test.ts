/**
 * tests/sim/integration/telegraph-ids-per-run.test.ts — telegraph ids are per-run state, not a
 * process-wide counter (STATE.md defect (i), sim/CLAUDE.md D1/D3 determinism). Two runs created in
 * one process with the same seed and stepped tick-for-tick (as two server rooms would be) must mint
 * identical telegraph id sequences; with a module-level counter they interleave (run A gets 1, run B
 * gets 2, ...), so a run's ids depend on what else the process happens to be simulating.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type Run } from "@sim/index";
import { CONTENT, DUNGEON, FULL_PARTY, SEED, scriptedPlayerCommands } from "./fixtures";

const TICKS = 400;

/** Per-tick list of telegraph ids alive in `run.encounter` plus every telegraph_hit id emitted. */
interface IdTrace {
  live: number[][];
  hits: number[];
}

function stepTraced(run: Run, commands: ReturnType<typeof scriptedPlayerCommands>, trace: IdTrace): Run {
  const tick = run.state.tick;
  const { run: next, events } = stepRun(run, commands.filter((c) => c.tick === tick));
  trace.live.push(next.encounter.telegraphs.map((t) => t.id));
  for (const e of events) if (e.type === "telegraph_hit") trace.hits.push(e.telegraphId as number);
  return next;
}

describe("telegraph ids are owned by the run (determinism across concurrent runs)", () => {
  it("two same-seed runs stepped in lockstep mint identical telegraph id sequences", () => {
    const commands = scriptedPlayerCommands(TICKS);
    let a = createRun(SEED, FULL_PARTY, DUNGEON, CONTENT);
    let b = createRun(SEED, FULL_PARTY, DUNGEON, CONTENT);
    const ta: IdTrace = { live: [], hits: [] };
    const tb: IdTrace = { live: [], hits: [] };
    for (let i = 0; i < TICKS; i++) {
      a = stepTraced(a, commands, ta);
      b = stepTraced(b, commands, tb);
    }
    // Non-vacuity: the boss script really telegraphed and resolved more than once.
    expect(ta.hits.length).toBeGreaterThan(1);
    expect(tb.hits).toEqual(ta.hits);
    expect(tb.live).toEqual(ta.live);
    // Ids start at 1 and count up within the run, independent of any other run in the process.
    expect(ta.hits[0]).toBe(1);
    expect(ta.hits).toEqual(ta.hits.map((_, i) => i + 1));
  });

  it("a run created after another run has already minted ids still starts at 1", () => {
    const commands = scriptedPlayerCommands(TICKS);
    let first = createRun(SEED, FULL_PARTY, DUNGEON, CONTENT);
    const tFirst: IdTrace = { live: [], hits: [] };
    for (let i = 0; i < TICKS; i++) first = stepTraced(first, commands, tFirst);
    let second = createRun(SEED, FULL_PARTY, DUNGEON, CONTENT);
    const tSecond: IdTrace = { live: [], hits: [] };
    for (let i = 0; i < TICKS; i++) second = stepTraced(second, commands, tSecond);
    expect(tFirst.hits.length).toBeGreaterThan(1);
    expect(tSecond.hits).toEqual(tFirst.hits);
  });
});
