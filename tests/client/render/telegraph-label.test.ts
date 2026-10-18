/**
 * tests/client/render/telegraph-label.test.ts
 *
 * D10 readability: a SPREAD or STACK telegraph must say what the player should DO, not only show a
 * coloured circle. Drives the real render/telegraph-label.ts (pure model + TelegraphLabelPool) and
 * the real render/telegraphs.ts TelegraphPool against the recording fake of "three"
 * (tests/client/support/fake-three.ts, whose fake canvas logs every fillText and fillRect), and
 * asserts what was actually drawn: "SPREAD: move apart" / "STACK: group up" plus a countdown and a
 * shrinking bar, a label that fades over the last ticks before resolve, and no label at all for
 * avoid / boss / unclassed telegraphs.
 */
import { describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("../support/fake-three.js"));

import * as THREE from "three";
import { TelegraphPool, type RenderTelegraph } from "../../../client/src/render/telegraphs.js";
import {
  LABEL_FADE_TICKS,
  LABEL_MERGE_M,
  TELEGRAPH_LABEL_RENDER_ORDER,
  TelegraphLabelPool,
  telegraphCue,
  telegraphLabelModel,
} from "../../../client/src/render/telegraph-label.js";
import { TELEGRAPH_STYLES } from "../../../client/src/render/telegraphFill.js";
import type { TelegraphClass } from "../../../client/src/sim-host/telegraph-map.js";
import type { FakeCanvas, Sprite } from "../support/fake-three.js";

function tg(id: string, telegraphClass: TelegraphClass | undefined, resolvesTick = 100): RenderTelegraph {
  const base: RenderTelegraph = { id, shape: "circle", center: { x: 4000, z: -2000 }, radius: 3000, yaw: 0, resolvesTick, danger: telegraphClass !== "stack" };
  return telegraphClass ? { ...base, telegraphClass } : base;
}

function sprites(scene: THREE.Scene): Sprite[] {
  return (scene as unknown as { children: unknown[] }).children.filter((c): c is Sprite => c instanceof (THREE as unknown as { Sprite: typeof Sprite }).Sprite);
}

function canvasOf(s: Sprite): FakeCanvas {
  return (s.material.map as unknown as { image: FakeCanvas }).image;
}

function world(): { scene: THREE.Scene; pool: TelegraphPool } {
  const scene = new THREE.Scene();
  return { scene, pool: new TelegraphPool(scene, new TelegraphLabelPool(scene)) };
}

describe("telegraphCue: what each class tells the player to do", () => {
  it("spread says SPREAD / move apart, stack says STACK / group up", () => {
    expect(telegraphCue("spread")).toEqual({ title: "SPREAD", cue: "move apart" });
    expect(telegraphCue("stack")).toEqual({ title: "STACK", cue: "group up" });
  });

  it("avoid, boss and an unclassed (server wire) telegraph get no label", () => {
    expect(telegraphCue("avoid")).toBeNull();
    expect(telegraphCue("boss")).toBeNull();
    expect(telegraphCue(undefined)).toBeNull();
  });
});

describe("telegraphLabelModel: countdown, bar and fade from the remaining ticks", () => {
  it("counts down in seconds to resolvesTick", () => {
    expect(telegraphLabelModel("spread", 50, 100)!.countdown).toBe("2.5s");
    expect(telegraphLabelModel("stack", 98, 100)!.countdown).toBe("0.1s");
    expect(telegraphLabelModel("stack", 120, 100)!.countdown).toBe("0.0s");
  });

  it("the bar's remaining fraction shrinks as resolve approaches", () => {
    const early = telegraphLabelModel("spread", 50, 100)!.remaining;
    const late = telegraphLabelModel("spread", 90, 100)!.remaining;
    expect(early).toBeGreaterThan(late);
    expect(telegraphLabelModel("spread", 100, 100)!.remaining).toBe(0);
  });

  it("is fully opaque until the last LABEL_FADE_TICKS, then fades to 0 at resolve", () => {
    expect(telegraphLabelModel("spread", 0, 100)!.opacity).toBe(1);
    expect(telegraphLabelModel("spread", 100 - LABEL_FADE_TICKS, 100)!.opacity).toBe(1);
    const mid = telegraphLabelModel("spread", 100 - LABEL_FADE_TICKS / 2, 100)!.opacity;
    expect(mid).toBeGreaterThan(0);
    expect(mid).toBeLessThan(1);
    expect(telegraphLabelModel("spread", 100, 100)!.opacity).toBe(0);
  });

  it("returns null for classes that get no label", () => {
    expect(telegraphLabelModel("avoid", 0, 100)).toBeNull();
    expect(telegraphLabelModel("boss", 0, 100)).toBeNull();
  });
});

describe("TelegraphPool + TelegraphLabelPool: the label actually drawn in the scene", () => {
  it("draws exactly one label per spread / stack telegraph, none for avoid / boss / unclassed", () => {
    const { scene, pool } = world();
    pool.sync([tg("a", "avoid"), tg("s", "spread"), tg("k", "stack"), tg("b", "boss"), tg("u", undefined)], 50);
    const texts = sprites(scene).map((s) => canvasOf(s).ctx.texts.join(" | "));
    expect(texts).toHaveLength(2);
    expect(texts[0]).toContain("SPREAD: move apart");
    expect(texts[1]).toContain("STACK: group up");
    for (const t of texts) expect(t).toContain("2.5s");
  });

  it("sits on the telegraph's centre (metres) and redraws the countdown + a shrinking bar", () => {
    const { scene, pool } = world();
    pool.sync([tg("s", "spread")], 50);
    const label = sprites(scene)[0]!;
    expect([label.position.x, label.position.z]).toEqual([4, -2]);
    // Drawn over name tags and bars (createCanvasSprite's renderOrder 10) and damage numbers
    // (damageNumbers.ts's 20), so a clumped pull never crosses out the instruction.
    expect(label.renderOrder).toBe(TELEGRAPH_LABEL_RENDER_ORDER);
    expect(TELEGRAPH_LABEL_RENDER_ORDER).toBeGreaterThan(20);
    const spreadHex = `#${TELEGRAPH_STYLES.spread.color.toString(16).padStart(6, "0")}`;
    const bar = (): number => canvasOf(label).ctx.rects.find((r) => r.style === spreadHex)!.w;
    const before = bar();
    expect(before).toBeGreaterThan(0);
    pool.sync([tg("s", "spread")], 80);
    expect(canvasOf(label).ctx.texts.join(" ")).toContain("1.0s");
    expect(bar()).toBeLessThan(before);
  });

  it("fades the label sprite with the remaining ticks", () => {
    const { scene, pool } = world();
    pool.sync([tg("k", "stack")], 10);
    const label = sprites(scene)[0]!;
    expect(label.material.opacity).toBe(1);
    pool.sync([tg("k", "stack")], 100 - LABEL_FADE_TICKS / 2);
    expect(label.material.opacity).toBeGreaterThan(0);
    expect(label.material.opacity).toBeLessThan(1);
  });

  it("removes the label when its telegraph is gone", () => {
    const { scene, pool } = world();
    pool.sync([tg("s", "spread"), tg("k", "stack")], 50);
    expect(sprites(scene)).toHaveLength(2);
    pool.sync([tg("k", "stack")], 51);
    expect(sprites(scene).map((s) => canvasOf(s).ctx.texts.join(" "))).toEqual([expect.stringContaining("STACK")]);
    pool.sync([], 52);
    expect(sprites(scene)).toHaveLength(0);
  });

  it("merges same-class labels whose centres are within LABEL_MERGE_M into one (a clumped pull)", () => {
    const { scene, pool } = world();
    const at = (id: string, cls: TelegraphClass, x: number): RenderTelegraph => ({ ...tg(id, cls), center: { x, z: 0 } });
    pool.sync([at("s1", "spread", 0), at("s2", "spread", 1000), at("s3", "spread", (LABEL_MERGE_M + 1) * 1000), at("k1", "stack", 500)], 50);
    const texts = sprites(scene).map((s) => canvasOf(s).ctx.texts[0]);
    expect(texts).toEqual(["SPREAD: move apart", "SPREAD: move apart", "STACK: group up"]);
    expect(sprites(scene).map((s) => s.position.x)).toEqual([0, LABEL_MERGE_M + 1, 0.5]);
  });

  it("a TelegraphPool built without a label pool draws decals only (headless callers)", () => {
    const scene = new THREE.Scene();
    new TelegraphPool(scene).sync([tg("s", "spread")], 50);
    expect(sprites(scene)).toHaveLength(0);
  });
});
