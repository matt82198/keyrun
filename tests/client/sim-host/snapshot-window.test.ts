/**
 * tests/client/sim-host/snapshot-window.test.ts
 *
 * The sim-to-snapshot projection of an open Window (design/BIBLE.md §4) that render/window-ring.ts
 * draws from, driven through the public path: REAL content + client run-setup, sim/index.ts's
 * createRun/stepRun, and snapshot-codec.ts's buildSnapshot (the calls SimRuntime.step() makes).
 *
 * Scenario (seed 1, hollow-vault, human warden id 1): the human walks 14 ticks toward the Choir
 * lieutenant (actor 102, same approach as snapshot-kick.test.ts), then Strikes it at tick 20.
 * warden.strike carries `{ op: "window", windowId: "sundered", durationTicks: 60, onTarget: true }`.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { createRun, stepRun, type Run } from "../../../sim/index.js";
import type { Command } from "../../../sim/core/commands.js";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { buildSnapshot } from "../../../client/src/sim-host/snapshot-codec.js";
import type { Snapshot } from "../../../client/src/sim-host/snapshot.js";

const HUMAN = 1;
const LIEUTENANT = 102;
const STRIKE_TICK = 20;

function play(ticks: number, strike: boolean): Snapshot[] {
  const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
  let run: Run = createRun(1, setup.party, setup.dungeon, setup.content, setup.runOptions);
  const out: Snapshot[] = [];
  for (let i = 0; i < ticks; i++) {
    const tick = run.state.tick;
    const commands: Command[] = [];
    if (tick < 14) commands.push({ tick, actorId: HUMAN, kind: "move", dir: { x: 0, z: -1000 }, yaw: 512 });
    if (strike && tick === STRIKE_TICK) {
      commands.push({ tick, actorId: HUMAN, kind: "cast", abilityId: "warden.strike", targetId: LIEUTENANT });
    }
    run = stepRun(run, commands).run;
    out.push(buildSnapshot(run, setup, 0, null));
  }
  return out;
}

const windowOf = (s: Snapshot, id: number) => s.actors.find((a) => a.id === id)?.window ?? null;

describe("snapshot-codec Window projection", () => {
  const frames = play(STRIKE_TICK + 80, true);
  const opened = frames.find((s) => windowOf(s, LIEUTENANT) !== null);

  it("projects the Sundered Window onto the struck enemy with its content duration", () => {
    expect(opened).toBeDefined();
    const w = windowOf(opened!, LIEUTENANT)!;
    expect(w).toMatchObject({ windowId: "sundered", setupClass: "warden", durationTicks: 60, lock: false });
    expect(w.endsTick - w.durationTicks).toBeGreaterThanOrEqual(STRIKE_TICK);
    expect(w.endsTick).toBeGreaterThan(opened!.tick);
  });

  it("keeps the same Window instance while open and drops it to null once it expires", () => {
    const w = windowOf(opened!, LIEUTENANT)!;
    const during = frames.filter((s) => s.tick < w.endsTick && s.tick >= opened!.tick);
    expect(during.length).toBeGreaterThan(30);
    for (const s of during) expect(windowOf(s, LIEUTENANT)?.endsTick).toBe(w.endsTick);
    const after = frames.filter((s) => s.tick >= w.endsTick);
    expect(after.length).toBeGreaterThan(0);
    for (const s of after) expect(windowOf(s, LIEUTENANT)).toBeNull();
  });

  it("projects null for every actor when no Setup ability was used", () => {
    for (const s of play(STRIKE_TICK + 10, false)) {
      for (const a of s.actors) expect(a.window).toBeNull();
    }
  });
});
