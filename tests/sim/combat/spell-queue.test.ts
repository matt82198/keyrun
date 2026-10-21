/**
 * The spell queue window (PR #370), driven through rules.ts tick by tick. Twin of
 * game/sim/Keyrun.Sim.Tests/SpellQueueTests.cs: a player's press refused by its cast or GCD inside the last
 * SPELL_QUEUE_WINDOW_TICKS ticks is held and fires on the first legal tick; an earlier press is dropped; the latest
 * press wins; a fresh press on the firing tick supersedes; queued and fresh kicks on one tick resolve by actor id
 * (BIBLE s3); a queued kick firing on the enemy cast's end tick is too late; enemies never queue.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { makeRules } from "../../../sim/combat/rules";
import { SPELL_QUEUE_WINDOW_TICKS } from "../../../sim/combat/spell-queue";
import { TICK_MS } from "../../../sim/core/tick";
import type { Command, SimEvent, SimState } from "../../../sim/combat/types";

const CHANNEL_TICKS = 30;
const GCD_TICKS = 15;

function def(id: string, castTicks: number, gcdTicks: number, effects: AbilityDef["effects"], interruptible = false): AbilityDef {
  return {
    id, castTicks, gcdTicks, cooldownTicks: 0, charges: 1, cost: { resource: "none", amount: 0 }, range: 5000,
    shape: { kind: "target" }, interruptible, effects,
  };
}

const channel = def("q.channel", CHANNEL_TICKS, GCD_TICKS, [{ op: "damage", amount: 5, school: "fire" }]);
const bolt = def("q.bolt", 0, GCD_TICKS, [{ op: "damage", amount: 5, school: "fire" }]);
const zap = def("q.zap", 0, GCD_TICKS, [{ op: "damage", amount: 7, school: "fire" }]);
const kick = def("q.kick", 0, 0, [{ op: "interrupt" }]);
const mending = def("q.mending", 80, 0, [{ op: "heal", amount: 50 }], true);

interface Arena { state: SimState; rules: ReturnType<typeof makeRules>; events: SimEvent[] }

function arena(): Arena {
  const state: SimState = {
    tick: 0, seed: 1, prng: [0, 0, 0, 0], runTimerTicks: 0, phase: "combat", log: [],
    actors: {
      1: createActor({ id: 1, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1000, maxResource: 100 }),
      2: createActor({ id: 2, kind: "enemy", classId: "choir.cantor", role: "none", pos: { x: 0, y: 0, z: 1000 }, maxHealth: 100000, maxResource: 0 }),
      3: createActor({ id: 3, kind: "player", classId: "cinder", role: "damage", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 1000, maxResource: 100 }),
    },
  };
  const rules = makeRules({
    abilities: { [channel.id]: channel, [bolt.id]: bolt, [zap.id]: zap, [kick.id]: kick, [mending.id]: mending },
    effects: {}, lockoutTicks: 60, baseSpeedMmPerTick: 300, actorRadiusMm: 50,
    bounds: { minX: -10000, maxX: 10000, minZ: -10000, maxZ: 10000 },
  });
  return { state, rules, events: [] };
}

function step(a: Arena, ...cmds: Command[]): void {
  a.rules(a.state, cmds, (e) => a.events.push(e));
  a.state.tick += 1;
}

function until(a: Arena, tick: number): void {
  while (a.state.tick < tick) step(a);
}

function cast(a: Arena, actorId: number, ability: AbilityDef, targetId = 2): Command {
  return { tick: a.state.tick, actorId, kind: "cast", abilityId: ability.id, targetId };
}

function completeTick(a: Arena, abilityId: string): number | undefined {
  return a.events.find((e) => e.type === "cast_complete" && (e as { abilityId?: string }).abilityId === abilityId)?.tick;
}

describe("spell queue window", () => {
  it("is WoW's 400 ms in ticks", () => {
    expect(SPELL_QUEUE_WINDOW_TICKS).toBe(8);
    expect(SPELL_QUEUE_WINDOW_TICKS * TICK_MS).toBe(400);
  });

  it("a press inside the last window ticks of a cast fires on the tick the cast ends", () => {
    const a = arena();
    step(a, cast(a, 1, channel));
    until(a, CHANNEL_TICKS - SPELL_QUEUE_WINDOW_TICKS);
    step(a, cast(a, 1, bolt));
    expect(a.state.actors[1]!.castId).toBe(channel.id);
    expect(a.state.actors[1]!.queuedCast?.abilityId).toBe(bolt.id);
    until(a, CHANNEL_TICKS + 1);
    expect(completeTick(a, channel.id)).toBe(CHANNEL_TICKS);
    expect(completeTick(a, bolt.id)).toBe(CHANNEL_TICKS);
    expect(a.state.actors[1]!.queuedCast).toBeUndefined();
  });

  it("a press inside the last window ticks of the GCD fires on the tick the GCD ends", () => {
    const a = arena();
    step(a, cast(a, 1, bolt));
    until(a, GCD_TICKS - 3);
    step(a, cast(a, 1, zap));
    until(a, GCD_TICKS + 2);
    expect(completeTick(a, zap.id)).toBe(GCD_TICKS);
  });

  it("a press earlier than the window is dropped", () => {
    const a = arena();
    step(a, cast(a, 1, channel));
    until(a, CHANNEL_TICKS - SPELL_QUEUE_WINDOW_TICKS - 1);
    step(a, cast(a, 1, bolt));
    expect(a.state.actors[1]!.queuedCast).toBeUndefined();
    until(a, CHANNEL_TICKS + 10);
    expect(completeTick(a, channel.id)).toBe(CHANNEL_TICKS);
    expect(completeTick(a, bolt.id)).toBeUndefined();
  });

  it("the latest queued press wins", () => {
    const a = arena();
    step(a, cast(a, 1, channel));
    until(a, CHANNEL_TICKS - 6);
    step(a, cast(a, 1, bolt));
    step(a, cast(a, 1, zap));
    until(a, CHANNEL_TICKS + 20);
    expect(completeTick(a, zap.id)).toBe(CHANNEL_TICKS);
    expect(completeTick(a, bolt.id)).toBeUndefined();
  });

  it("a fresh press on the firing tick supersedes the queued one", () => {
    const a = arena();
    step(a, cast(a, 1, channel));
    until(a, CHANNEL_TICKS - 2);
    step(a, cast(a, 1, bolt));
    until(a, CHANNEL_TICKS);
    step(a, cast(a, 1, zap));
    until(a, CHANNEL_TICKS + 20);
    expect(completeTick(a, zap.id)).toBe(CHANNEL_TICKS);
    expect(completeTick(a, bolt.id)).toBeUndefined();
  });

  it.each([true, false])("queued and fresh kicks on one tick resolve by actor id, never by press order (fresh first %s)", (freshFirst) => {
    const a = arena();
    step(a, cast(a, 1, channel), { tick: 0, actorId: 2, kind: "cast", abilityId: mending.id, targetId: 1 });
    until(a, CHANNEL_TICKS - 4);
    step(a, cast(a, 1, kick));
    until(a, CHANNEL_TICKS);
    const fresh = cast(a, 3, kick);
    const target: Command = { tick: a.state.tick, actorId: 3, kind: "target", targetId: 2 };
    step(a, ...(freshFirst ? [fresh, target] : [target, fresh]));
    const kicks = a.events.filter((e) => e.type === "interrupt");
    expect(kicks).toHaveLength(1);
    expect(kicks[0]!.tick).toBe(CHANNEL_TICKS);
    expect((kicks[0] as { sourceId?: number }).sourceId).toBe(1);
  });

  it("a queued kick that fires on the enemy cast's end tick is too late", () => {
    const a = arena();
    step(a, cast(a, 1, channel));
    step(a);
    const enemy = a.state.actors[2]!;
    enemy.castId = mending.id;
    enemy.castStartTick = a.state.tick - 50;
    enemy.castEndsTick = CHANNEL_TICKS;
    until(a, CHANNEL_TICKS - 3);
    step(a, cast(a, 1, kick));
    until(a, CHANNEL_TICKS + 2);
    expect(a.events.some((e) => e.type === "interrupt")).toBe(false);
    expect(completeTick(a, mending.id)).toBe(CHANNEL_TICKS);
  });

  it("an enemy press is never queued", () => {
    const a = arena();
    step(a, cast(a, 2, bolt, 1));
    until(a, GCD_TICKS - 2);
    step(a, cast(a, 2, zap, 1));
    until(a, GCD_TICKS + 5);
    expect(a.state.actors[2]!.queuedCast).toBeUndefined();
    expect(completeTick(a, zap.id)).toBeUndefined();
  });
});
