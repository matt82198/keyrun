/**
 * tests/client/render/actor-status.test.ts
 *
 * Items 5 + 12 through the REAL render/actors.ts ActorPool (into tests/client/support/fake-three.ts):
 *  - status chips: each ActorSnapshot.effects entry draws a chip ("STUN 1.0s") on the chip sprite
 *    above the health bar; none -> the sprite is hidden,
 *  - the name tag's second line names the top status ("STUNNED" / "TAUNTED" / "BRACED") when the
 *    actor is not casting; a Taunt cast event tags its target TAUNTED for TAUNT_TAG_TICKS,
 *  - Downed: a dead PLAYER stays at its death position as a grey "DOWNED" tag (body, bars hidden);
 *    a dead enemy is still removed from view (a cleared pull does not litter the floor).
 * Plus the pure render/status-chips.ts model those draw from.
 */
import { describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("../support/fake-three.js"));

import * as THREE from "three";
import type { FakeCanvas } from "../support/fake-three.js";
import { ActorPool, TAUNT_TAG_TICKS } from "../../../client/src/render/actors.js";
import { statusChips, statusTag, DOWNED_COLOR } from "../../../client/src/render/status-chips.js";
import type { ActorSnapshot, CastSnapshotEvent } from "../../../client/src/sim-host/snapshot.js";
import type { InterpolatedActor } from "../../../client/src/sim-host/interpolate.js";

function actor(over: Partial<ActorSnapshot>): ActorSnapshot {
  return { id: 1, kind: "player", role: "tank", classId: "warden", pos: { x: 1000, y: 0, z: 2000 }, yaw: 0, health: 100, maxHealth: 100, resource: 0, maxResource: 100, alive: true, targetId: null, cast: null, cooldowns: {}, ...over };
}
const interp = (a: ActorSnapshot): InterpolatedActor => ({ id: a.id, pos: a.pos, yaw: a.yaw, health: a.health, maxHealth: a.maxHealth, resource: a.resource, maxResource: a.maxResource, alive: a.alive, targetId: a.targetId, cast: a.cast });

interface Group { children: { visible: boolean; material?: { map?: { image: FakeCanvas } } }[]; visible: boolean; position: { x: number; z: number } }

function setup(actors: ActorSnapshot[]) {
  const scene = new THREE.Scene();
  const pool = new ActorPool(scene as never);
  pool.configure(1, undefined);
  const sync = (list: ActorSnapshot[], tick: number, events: CastSnapshotEvent[] = []) => pool.sync(list.map(interp), new Map(list.map((a) => [a.id, a])), tick, events);
  sync(actors, 10);
  const groupOf = (i: number) => (scene as unknown as { children: Group[] }).children.filter((c) => "children" in c && (c as Group).children.length >= 8)[i]!;
  const texts = (g: Group, child: number) => g.children[child]!.material!.map!.image.ctx.texts;
  return { pool, sync, groupOf, texts };
}

describe("status chips model (render/status-chips.ts)", () => {
  it("names each effect with a short label and its seconds left; unknown ids are humanized", () => {
    expect(statusChips([{ id: "stunned", ticks: 20 }, { id: "braceSelf", ticks: 30 }, { id: "someNewThing", ticks: 5 }]).map((c) => c.text)).toEqual(["STUN 1.0s", "BRACED 1.5s", "SOME NEW 0.3s"]);
  });
  it("the tag names the most urgent status: STUNNED over TAUNTED over BRACED", () => {
    expect(statusTag([{ id: "braced", ticks: 9 }], false)).toBe("BRACED");
    expect(statusTag([{ id: "braced", ticks: 9 }], true)).toBe("TAUNTED");
    expect(statusTag([{ id: "braced", ticks: 9 }, { id: "stunned", ticks: 2 }], true)).toBe("STUNNED");
    expect(statusTag([{ id: "burning", ticks: 9 }], false)).toBe("");
  });
});

describe("ActorPool status chips + tags (item 5)", () => {
  it("a stunned enemy draws a STUN chip above its health bar and STUNNED under its name", () => {
    const enemy = actor({ id: 100, kind: "enemy", role: "none", classId: "choir.acolyte", effects: [{ id: "stunned", ticks: 20 }] });
    const h = setup([actor({}), enemy]);
    expect(h.pool.statusOf(100)).toEqual({ chips: ["STUN 1.0s"], tag: "STUNNED" });
    const g = h.groupOf(1);
    expect(g.children[7]!.visible).toBe(true);
    expect(h.texts(g, 7)).toContain("STUN 1.0s");
    expect(h.texts(g, 4)).toContain("STUNNED");
  });

  it("no effects -> the chip sprite is hidden and the tag line is empty", () => {
    const h = setup([actor({})]);
    expect(h.groupOf(0).children[7]!.visible).toBe(false);
    expect(h.pool.statusOf(1)).toEqual({ chips: [], tag: "" });
  });

  it("YOUR Brace shows a BRACED chip and tag", () => {
    const h = setup([actor({ effects: [{ id: "braceSelf", ticks: 40 }] })]);
    expect(h.pool.statusOf(1)).toEqual({ chips: ["BRACED 2.0s"], tag: "BRACED" });
  });

  it("a Taunt cast event tags its target TAUNTED for TAUNT_TAG_TICKS, then the tag clears", () => {
    const enemy = actor({ id: 100, kind: "enemy", role: "none", classId: "choir.acolyte" });
    const h = setup([actor({}), enemy]);
    const taunt: CastSnapshotEvent = { type: "cast", tick: 10, actorId: 1, abilityId: "warden.taunt", shape: "target", origin: { x: 0, z: 0 }, yaw: 0, targetId: 100, hitIds: [], taunt: true };
    h.sync([actor({}), enemy], 11, [taunt]);
    expect(h.pool.statusOf(100).tag).toBe("TAUNTED");
    expect(h.pool.statusOf(100).chips[0]).toMatch(/^TAUNTED /);
    h.sync([actor({}), enemy], 10 + TAUNT_TAG_TICKS + 1);
    expect(h.pool.statusOf(100).tag).toBe("");
  });
});

describe("ActorPool Downed tag (item 12)", () => {
  it("a dead party member stays at its death position as a grey DOWNED tag; body and bars hidden", () => {
    const bot = actor({ id: 2, role: "healer", classId: "chanter", pos: { x: -2100, y: 0, z: 4000 } });
    const h = setup([actor({}), bot]);
    h.sync([actor({}), { ...bot, alive: false, health: 0 }], 11);
    const g = h.groupOf(1);
    expect(g.visible).toBe(true);
    expect(g.position.x).toBeCloseTo(-2.1);
    expect(g.position.z).toBeCloseTo(4);
    expect(g.children[0]!.visible).toBe(false); // capsule
    expect(g.children[1]!.visible).toBe(false); // health bar
    expect(g.children[4]!.visible).toBe(true); // name tag
    expect(h.texts(g, 4)).toEqual(expect.arrayContaining(["DOWNED", "[H] Chanter"]));
    expect(h.pool.tagTitles()[2]).toBe("DOWNED");
    expect(h.pool.downedColor(2)).toBe(DOWNED_COLOR);
  });

  it("a revived (alive again) actor gets its body back; a dead enemy is hidden, not tagged", () => {
    const enemy = actor({ id: 100, kind: "enemy", role: "none", classId: "choir.acolyte" });
    const h = setup([actor({}), enemy]);
    h.sync([{ ...actor({}), alive: false }, { ...enemy, alive: false }], 11);
    expect(h.groupOf(1).visible).toBe(false);
    h.sync([actor({}), { ...enemy, alive: false }], 12);
    expect(h.groupOf(0).children[0]!.visible).toBe(true);
    expect(h.pool.tagTitles()[1]).toBe("YOU: Warden");
  });
});
