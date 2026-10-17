/**
 * tests/client/hud/boss-bar.test.ts
 *
 * client/src/hud/boss-bar.ts: the top-centre boss HP bar (D10). Pure model (which actors get a bar,
 * fraction/percent math, stable ordering), the DOM view (appear/disappear, one bar per boss body,
 * COLOR_BOSS fill) through a minimal fake document, the REAL hollow-vault content's boss, and the
 * hud-wiring.ts composition root handing every snapshot's actors to the bar.
 */
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("three", () => import("../support/fake-three.js"));

import { loadContent } from "@content/index";
import { bossBarModel, bossDisplayName, BossBarView, BOSS_BAR_COLOR } from "../../../client/src/hud/boss-bar.js";
import { COLOR_BOSS } from "../../../client/src/render/roleColor.js";
import type { ActorSnapshot, Snapshot } from "../../../client/src/sim-host/snapshot.js";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";
import { runGameLoop, type GameLoopDeps } from "../../../client/src/hud-wiring.js";
import type { HostHandle } from "../../../client/src/net/host-handle.js";

function actor(id: number, over: Partial<ActorSnapshot> = {}): ActorSnapshot {
  return {
    id,
    kind: "enemy",
    role: "damage",
    classId: "choir.melee",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 1000,
    maxHealth: 1000,
    resource: 0,
    maxResource: 0,
    alive: true,
    targetId: null,
    cast: null,
    cooldowns: {},
    ...over,
  };
}

const boss = (id: number, over: Partial<ActorSnapshot> = {}): ActorSnapshot => actor(id, { classId: "hollowWarden.boss", ...over });

describe("hud/boss-bar: bossBarModel appear/disappear", () => {
  it("has no rows when no '.boss' actor is in the snapshot", () => {
    expect(bossBarModel([actor(1), actor(2, { kind: "player", classId: "warden" })])).toEqual([]);
  });

  it("has one row for a live enemy whose classId ends with '.boss'", () => {
    const rows = bossBarModel([actor(1), boss(9)]);
    expect(rows.map((r) => r.id)).toEqual([9]);
    expect(rows[0]!.name).toBe("Hollow Warden");
  });

  it("drops the row once the boss is dead", () => {
    expect(bossBarModel([boss(9, { alive: false, health: 0 })])).toEqual([]);
  });

  it("ignores a player whose classId happens to end with '.boss' (same rule as the actor pool's colour)", () => {
    expect(bossBarModel([actor(3, { kind: "player", classId: "odd.boss" })])).toEqual([]);
  });
});

describe("hud/boss-bar: fraction math", () => {
  it("fraction = health / maxHealth, percent rounded up so a live boss never reads 0%", () => {
    expect(bossBarModel([boss(9, { health: 750, maxHealth: 1000 })])[0]).toMatchObject({ fraction: 0.75, percent: 75 });
    expect(bossBarModel([boss(9, { health: 1, maxHealth: 1000 })])[0]).toMatchObject({ fraction: 0.001, percent: 1 });
    expect(bossBarModel([boss(9, { health: 1000, maxHealth: 1000 })])[0]).toMatchObject({ fraction: 1, percent: 100 });
  });

  it("clamps overheal to 1 and a zero/negative maxHealth to 0", () => {
    expect(bossBarModel([boss(9, { health: 1200, maxHealth: 1000 })])[0]).toMatchObject({ fraction: 1, percent: 100 });
    expect(bossBarModel([boss(9, { health: 50, maxHealth: 0 })])[0]).toMatchObject({ fraction: 0, percent: 0 });
    expect(bossBarModel([boss(9, { health: -5, maxHealth: 100 })])[0]).toMatchObject({ fraction: 0, percent: 0 });
  });
});

describe("hud/boss-bar: two-body boss and ordering", () => {
  it("gives a two-body boss two rows, ordered by actor id whatever the snapshot order", () => {
    const a = boss(21, { classId: "sunderedTwins.boss", health: 400 });
    const b = boss(20, { classId: "sunderedTwins.boss", health: 900 });
    expect(bossBarModel([a, actor(1), b]).map((r) => [r.id, r.percent])).toEqual([[20, 90], [21, 40]]);
    expect(bossBarModel([b, a]).map((r) => r.id)).toEqual([20, 21]);
  });

  it("keeps the surviving body's row (and its position) when the other dies", () => {
    const rows = bossBarModel([boss(20, { alive: false, health: 0 }), boss(21)]);
    expect(rows.map((r) => r.id)).toEqual([21]);
  });

  it("derives the display name from the classId", () => {
    expect(bossDisplayName("hollowWarden.boss")).toBe("Hollow Warden");
    expect(bossDisplayName("sunderedTwins.boss")).toBe("Sundered Twins");
    expect(bossDisplayName("warden.boss")).toBe("Warden");
  });
});

// ---- minimal fake DOM (no jsdom in this repo): only what boss-bar.ts's view uses ----
class FakeEl {
  children: FakeEl[] = [];
  className = "";
  hidden = false;
  textContent = "";
  style: Record<string, string> = {};
  constructor(readonly tagName: string) {}
  appendChild(c: FakeEl): FakeEl {
    this.children.push(c);
    return c;
  }
  removeChild(c: FakeEl): FakeEl {
    this.children = this.children.filter((x) => x !== c);
    return c;
  }
  all(cls: string): FakeEl[] {
    const out: FakeEl[] = [];
    const walk = (e: FakeEl): void => {
      if (e.className === cls) out.push(e);
      e.children.forEach(walk);
    };
    walk(this);
    return out;
  }
}

function mountView(): { container: FakeEl; view: BossBarView } {
  vi.stubGlobal("document", { createElement: (t: string) => new FakeEl(t) });
  const container = new FakeEl("div");
  return { container, view: new BossBarView(container as unknown as HTMLElement) };
}

afterEach(() => vi.unstubAllGlobals());

describe("hud/boss-bar: BossBarView", () => {
  it("matches the actor pool's COLOR_BOSS", () => {
    expect(BOSS_BAR_COLOR).toBe(`#${COLOR_BOSS.toString(16).padStart(6, "0")}`);
    expect(BOSS_BAR_COLOR).toBe("#9b59b6");
  });

  it("is hidden until a boss appears, shows name + bar + percent, then hides when it dies", () => {
    const { container, view } = mountView();
    const root = container.children[0]!;
    expect(root.hidden).toBe(true);

    view.update([actor(1), boss(9, { health: 600 })]);
    expect(root.hidden).toBe(false);
    expect(root.all("boss-bar-name").map((e) => e.textContent)).toEqual(["Hollow Warden"]);
    expect(root.all("boss-bar-percent").map((e) => e.textContent)).toEqual(["60%"]);
    const fill = root.all("boss-bar-fill")[0]!;
    expect(fill.style.width).toBe("60%");
    expect(fill.style.background).toBe(BOSS_BAR_COLOR);

    view.update([boss(9, { alive: false, health: 0 })]);
    expect(root.hidden).toBe(true);
    expect(root.all("boss-bar-row")).toHaveLength(0);
  });

  it("stacks two bars for a two-body boss in stable id order across snapshots", () => {
    const { container, view } = mountView();
    const root = container.children[0]!;
    const twin = (id: number, health: number): ActorSnapshot => boss(id, { classId: "sunderedTwins.boss", health });
    view.update([twin(31, 500), twin(30, 1000)]);
    expect(root.all("boss-bar-percent").map((e) => e.textContent)).toEqual(["100%", "50%"]);
    const firstRow = root.all("boss-bar-row")[0];
    view.update([twin(30, 800), twin(31, 250)]);
    expect(root.all("boss-bar-percent").map((e) => e.textContent)).toEqual(["80%", "25%"]);
    expect(root.all("boss-bar-row")[0]).toBe(firstRow); // rows reused, not rebuilt every snapshot
    expect(root.all("boss-bar-fill").map((e) => e.style.width)).toEqual(["80%", "25%"]);
  });
});

describe("hud/boss-bar: real content", () => {
  it("no bar before the boss pull; the real boss pull yields exactly one full Hollow Warden bar", () => {
    const setup = buildRunSetup(loadContent(), { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    expect(bossBarModel(new SimRuntime(setup).step().actors)).toEqual([]); // trash pulls first, no boss yet
    const bossPull = setup.dungeon.pulls.find((p) => p.id === "boss")!;
    const snap = new SimRuntime({ ...setup, dungeon: { ...setup.dungeon, pulls: [bossPull] } }).step();
    expect(bossBarModel(snap.actors).map((r) => [r.id, r.name, r.percent])).toEqual([[bossPull.spawns[0]!.actorId, "Hollow Warden", 100]]);
  });
});

describe("hud/boss-bar: hud-wiring composition root", () => {
  it("runGameLoop hands every snapshot's actors to deps.bossBar", () => {
    vi.stubGlobal("requestAnimationFrame", () => 0);
    vi.stubGlobal("window", { addEventListener: () => undefined });
    let emit: ((s: Snapshot) => void) | null = null;
    const host = { tickMs: 50, applyCommand: () => undefined, start: (cb: (s: Snapshot) => void) => ((emit = cb), () => undefined) } as unknown as HostHandle;
    const noop = (): void => undefined;
    const update = vi.fn();
    runGameLoop({
      simHost: host,
      humanActorId: 1,
      hud: { updateTimer: noop, updateSlack: noop, updateAbilityBar: noop },
      runEndView: { show: noop },
      damageNumbers: { spawn: noop },
      actorPool: { worldPositionOf: () => null },
      bossBar: { update },
    } as unknown as GameLoopDeps);
    const actors = [actor(1, { kind: "player", classId: "warden" }), boss(9)];
    emit!({ tick: 1, actors, telegraphs: [], events: [], runTimerTicks: 0, timerLimitTicks: 100 } as unknown as Snapshot);
    expect(update).toHaveBeenCalledWith(actors);
  });
});
