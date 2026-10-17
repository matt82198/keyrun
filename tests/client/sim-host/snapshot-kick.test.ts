/**
 * tests/client/sim-host/snapshot-kick.test.ts
 *
 * D10 readability of the interrupt (Keyrun's signature mechanic), driven through the public path:
 * the REAL content registry + client run-setup, sim/index.ts's createRun/stepRun, and
 * snapshot-codec.ts's buildSnapshot -- the same three calls SimRuntime.step() makes.
 *
 * Scenario (seed 1, hollow-vault, human warden id 1): the Choir lieutenant (actor 102) starts
 * `choir.healCast` (interruptible: true in content/data/abilities/enemies.json) at tick 185. The
 * human walks 14 ticks toward it to get inside warden.shieldBash's 3000mm range, then kicks it a few
 * ticks into the cast.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { createRun, stepRun, type Run } from "../../../sim/index.js";
import type { Command } from "../../../sim/core/commands.js";
import type { SimEvent } from "../../../sim/core/events.js";
import { buildRunSetup, type RunSetup } from "../../../client/src/sim-host/run-setup.js";
import { buildSnapshot } from "../../../client/src/sim-host/snapshot-codec.js";
import { MAX_SNAPSHOT_EVENTS, type Snapshot } from "../../../client/src/sim-host/snapshot.js";

const HUMAN = 1;
const LIEUTENANT = 102;
const HEAL = "choir.healCast";

function setupWith(healInterruptible?: boolean): RunSetup {
  const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
  if (healInterruptible === undefined) return setup;
  const heal = setup.content.abilities[HEAL]!;
  return { ...setup, content: { ...setup.content, abilities: { ...setup.content.abilities, [HEAL]: { ...heal, interruptible: healInterruptible } } } };
}

interface Frame {
  snapshot: Snapshot;
  events: SimEvent[];
}

/** Steps the run, returning one snapshot per tick. `kick` makes the human Shield Bash the
 * lieutenant 6 ticks into its heal cast (once). */
function play(setup: RunSetup, ticks: number, kick: boolean): Frame[] {
  let run: Run = createRun(1, setup.party, setup.dungeon, setup.content, setup.runOptions);
  const frames: Frame[] = [];
  let kicked = false;
  for (let i = 0; i < ticks; i++) {
    const tick = run.state.tick;
    const lt = run.state.actors[LIEUTENANT]!;
    const commands: Command[] = [];
    if (tick < 14) commands.push({ tick, actorId: HUMAN, kind: "move", dir: { x: 0, z: -1000 }, yaw: 512 });
    if (kick && !kicked && lt.castId === HEAL && tick > lt.castStartTick! + 5) {
      commands.push({ tick, actorId: HUMAN, kind: "cast", abilityId: "warden.shieldBash", targetId: LIEUTENANT });
      kicked = true;
    }
    const stepped = stepRun(run, commands);
    run = stepped.run;
    frames.push({ snapshot: buildSnapshot(run, setup, 0, null), events: stepped.events });
  }
  return frames;
}

function castOf(s: Snapshot, id: number) {
  return s.actors.find((a) => a.id === id)?.cast ?? null;
}

describe("snapshot: a casting enemy carries kickable from the sim's cast definition", () => {
  it("kickable is true for an interruptible cast", () => {
    const frame = play(setupWith(), 200, false).find((f) => castOf(f.snapshot, LIEUTENANT) !== null)!;
    expect(castOf(frame.snapshot, LIEUTENANT)).toMatchObject({ abilityId: HEAL, kickable: true });
  });

  it("kickable is false when the same cast's definition is not interruptible", () => {
    const frame = play(setupWith(false), 200, false).find((f) => castOf(f.snapshot, LIEUTENANT) !== null)!;
    expect(castOf(frame.snapshot, LIEUTENANT)).toMatchObject({ abilityId: HEAL, kickable: false });
  });
});

describe("snapshot codec: kickable and events round-trip byte-exact", () => {
  it("JSON encode -> decode -> encode is identical and deep-equal (structured clone too)", () => {
    const frames = play(setupWith(), 260, true);
    const casting = frames.find((f) => castOf(f.snapshot, LIEUTENANT) !== null)!.snapshot;
    const withEvent = frames.find((f) => (f.snapshot.events ?? []).length > 0)!.snapshot;
    for (const s of [casting, withEvent]) {
      const wire = JSON.stringify(s);
      const decoded = JSON.parse(wire) as Snapshot;
      expect(JSON.stringify(decoded)).toBe(wire);
      expect(decoded).toEqual(s);
      expect(structuredClone(s)).toEqual(s);
    }
    expect(JSON.parse(JSON.stringify(casting)).actors.find((a: { id: number }) => a.id === LIEUTENANT).cast.kickable).toBe(true);
  });
});

describe("snapshot: a landed kick yields a one-shot interrupt event for that actor", () => {
  it("the next snapshot carries the interrupt event, matching the sim's own emitted event", () => {
    const frames = play(setupWith(), 260, true);
    const idx = frames.findIndex((f) => f.events.some((e) => e.type === "interrupt"));
    expect(idx).toBeGreaterThan(0);
    const simEvent = frames[idx]!.events.find((e) => e.type === "interrupt")!;
    const snap = frames[idx]!.snapshot;
    expect(castOf(snap, LIEUTENANT)).toBeNull();
    expect(snap.events).toEqual([
      { type: "interrupt", tick: simEvent.tick, actorId: LIEUTENANT, sourceId: HUMAN, school: simEvent.school },
    ]);
    expect(simEvent.targetId).toBe(LIEUTENANT);
  });

  it("is consumed: no snapshot before or after the landing tick repeats it", () => {
    const frames = play(setupWith(), 260, true);
    const carrying = frames.filter((f) => (f.snapshot.events ?? []).some((e) => e.type === "interrupt"));
    expect(carrying).toHaveLength(1);
  });

  it("no kick, no interrupt event (the cast completes instead)", () => {
    const frames = play(setupWith(), 300, false);
    expect(frames.every((f) => (f.snapshot.events ?? []).length === 0)).toBe(true);
    expect(frames.some((f) => f.events.some((e) => e.type === "cast_complete" && e.abilityId === HEAL))).toBe(true);
  });

  it("the events channel is bounded", () => {
    expect(MAX_SNAPSHOT_EVENTS).toBeGreaterThan(0);
    for (const f of play(setupWith(), 260, true)) expect((f.snapshot.events ?? []).length).toBeLessThanOrEqual(MAX_SNAPSHOT_EVENTS);
  });
});
