/**
 * tests/client/sim-host/snapshot-effects.test.ts
 *
 * Item 5 status channel: every content-defined effect an actor carries (stun, chill, braced,
 * burning, ...) reaches the Snapshot as `ActorSnapshot.effects` = [{ id, ticks remaining }], so the
 * renderer can draw status chips. Synthetic sim bookkeeping effects (Window "window:"/"lockwindow:",
 * kick "lockout:", "lock:lastclass:") are not content effects and never appear.
 *
 * Driven through the public path: REAL content + client run-setup, sim/index.ts's createRun/stepRun,
 * snapshot-codec.ts's buildSnapshot. Scenario (seed 1, hollow-vault, human warden id 1): Brace at
 * tick 2 (warden.brace -> braceSelf, 60 ticks), walk 14 ticks toward the Choir lieutenant (102),
 * Strike it at tick 20 (opens the "sundered" Window effect on it), Charge it at tick 50 (after Strike's GCD)
 * (warden.charge -> stunned, 20 ticks). Also: a Taunt cast event carries `taunt: true`.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { createRun, stepRun, type Run } from "../../../sim/index.js";
import type { Command } from "../../../sim/core/commands.js";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { buildSnapshot, toEffects } from "../../../client/src/sim-host/snapshot-codec.js";
import { MAX_ACTOR_EFFECTS, type CastSnapshotEvent, type Snapshot } from "../../../client/src/sim-host/snapshot.js";

const HUMAN = 1;
const LIEUTENANT = 102;

function play(ticks: number, extra: (tick: number) => Command[] = () => []): { frames: Snapshot[]; run: Run } {
  const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
  let run: Run = createRun(1, setup.party, setup.dungeon, setup.content, setup.runOptions);
  const frames: Snapshot[] = [];
  for (let i = 0; i < ticks; i++) {
    const tick = run.state.tick;
    const commands: Command[] = [];
    if (tick === 2) commands.push({ tick, actorId: HUMAN, kind: "cast", abilityId: "warden.brace" });
    if (tick >= 3 && tick < 17) commands.push({ tick, actorId: HUMAN, kind: "move", dir: { x: 0, z: -1000 }, yaw: 512 });
    if (tick === 20) commands.push({ tick, actorId: HUMAN, kind: "cast", abilityId: "warden.strike", targetId: LIEUTENANT });
    if (tick === 50) commands.push({ tick, actorId: HUMAN, kind: "cast", abilityId: "warden.charge", targetId: LIEUTENANT });
    commands.push(...extra(tick));
    const stepped = stepRun(run, commands);
    run = stepped.run;
    frames.push(buildSnapshot(run, setup, 0, null, stepped.events));
  }
  return { frames, run };
}

const effectsOf = (s: Snapshot, id: number) => s.actors.find((a) => a.id === id)?.effects;

describe("snapshot status effects channel (item 5)", () => {
  it("Brace shows on YOU as braceSelf with its remaining ticks, counting down, then gone", () => {
    const { frames } = play(80);
    const braced = frames.filter((s) => effectsOf(s, HUMAN)?.some((e) => e.id === "braceSelf"));
    expect(braced.length).toBeGreaterThan(50);
    const first = braced[0]!;
    const e0 = effectsOf(first, HUMAN)!.find((e) => e.id === "braceSelf")!;
    expect(e0.ticks).toBeGreaterThan(55);
    expect(e0.ticks).toBeLessThanOrEqual(60);
    const next = effectsOf(braced[1]!, HUMAN)!.find((e) => e.id === "braceSelf")!;
    expect(next.ticks).toBe(e0.ticks - 1);
    expect(effectsOf(frames[frames.length - 1]!, HUMAN)!.some((e) => e.id === "braceSelf")).toBe(false);
  });

  it("Charge stuns what it hits: the stunned chip appears with <= 20 ticks", () => {
    const { frames } = play(70);
    const stunned = frames.flatMap((s) => s.actors.flatMap((a) => (a.effects ?? []).filter((e) => e.id === "stunned")));
    expect(stunned.length).toBeGreaterThan(0);
    for (const e of stunned) expect(e.ticks).toBeGreaterThan(0);
    expect(Math.max(...stunned.map((e) => e.ticks))).toBeLessThanOrEqual(20);
  });

  it("never lists synthetic Window/lockout bookkeeping, even while the sundered Window is open", () => {
    const { frames, run } = play(70);
    const raw = Object.values(run.state.actors).flatMap((a) => a.effects.map((e) => e.effectId));
    expect(raw.some((id) => id.startsWith("window:"))).toBe(true); // the Window really is an effect...
    for (const s of frames) for (const a of s.actors) for (const e of a.effects ?? []) {
      expect(e.id).not.toMatch(/^(window|lockwindow|lockout|lock):/); // ...but never a chip
      expect(run.content.effects[e.id]).toBeDefined();
    }
  });

  it("toEffects caps the list, drops expired entries, and is sorted by id", () => {
    const { run } = play(1);
    const ids = Object.keys(run.content.effects).slice(0, MAX_ACTOR_EFFECTS + 3);
    const actor = { ...run.state.actors[HUMAN]!, effects: ids.map((effectId, i) => ({ effectId, sourceId: HUMAN, stacks: 1, endsTick: i === 0 ? 5 : 100, nextPeriodicTick: null })) };
    const out = toEffects(actor, 5, run.content.effects);
    expect(out.length).toBe(MAX_ACTOR_EFFECTS);
    expect(out.some((e) => e.id === ids[0])).toBe(false);
    expect([...out].map((e) => e.id)).toEqual([...out].map((e) => e.id).sort());
    for (const e of out) expect(e.ticks).toBe(95);
  });

  it("a Taunt cast event is flagged taunt: true (its threat op is a big multiplier); Strike is not", () => {
    const { frames } = play(70, (tick) => (tick === 56 ? [{ tick, actorId: HUMAN, kind: "cast", abilityId: "warden.taunt", targetId: LIEUTENANT }] : []));
    const casts = frames.flatMap((s) => (s.events ?? []).filter((e): e is CastSnapshotEvent => e.type === "cast" && e.actorId === HUMAN));
    expect(casts.find((e) => e.abilityId === "warden.taunt")?.taunt).toBe(true);
    expect(casts.find((e) => e.abilityId === "warden.strike")?.taunt).toBeUndefined();
  });
});
