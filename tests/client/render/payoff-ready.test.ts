/**
 * tests/client/render/payoff-ready.test.ts
 *
 * Window readability (D10 gap item 3): when an enemy carries an open Window that YOUR class can
 * close, the ability bar must light the key(s) whose Payoff is ready. Drives the real worker-side
 * display-info build (sim-host/display-info.ts) over the REAL content registry (payoff flags from
 * each ability's `{op:"payoff"}` effect, combo names from data/combos.json), then the pure
 * render/payoff-ready.ts selector and render/ability-bar-model.ts's per-slot `payoffReady`.
 * Content truth: Sundered is a warden setup that cinder closes as Shatter; Kindled is a cinder setup
 * that warden closes as Forge; a class never closes its own setup (no warden>warden combo).
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { buildDisplayInfo } from "../../../client/src/sim-host/display-info.js";
import { abilityBarModel } from "../../../client/src/render/ability-bar-model.js";
import { openPayoff } from "../../../client/src/render/payoff-ready.js";
import type { ActorSnapshot, ActorWindow } from "../../../client/src/sim-host/snapshot.js";

const registry = loadContent();
const cinder = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "cinder" });
const info = buildDisplayInfo(registry, cinder.content.abilities);
const slots = cinder.slotAbilities;

const SUNDERED: ActorWindow = { windowId: "sundered", setupClass: "warden", endsTick: 160, durationTicks: 60, lock: false };
const KINDLED: ActorWindow = { windowId: "kindled", setupClass: "cinder", endsTick: 160, durationTicks: 60, lock: false };

function actor(id: number, kind: "enemy" | "player", classId: string, window: ActorWindow | null = null, alive = true): ActorSnapshot {
  return {
    id, kind, role: kind === "enemy" ? "lieutenant" : "damage", classId, pos: { x: 0, y: 0, z: 0 }, yaw: 0,
    health: 10, maxHealth: 10, resource: 0, maxResource: 1, alive, targetId: null, cast: null, cooldowns: {}, window,
  };
}

describe("display info carries payoff flags and combo names", () => {
  it("flags exactly the content's payoff abilities", () => {
    expect(info.abilities["cinder.detonate"]?.payoff).toBe(true);
    expect(info.abilities["cinder.coldSnap"]?.payoff).toBe(true);
    expect(info.abilities["cinder.flare"]?.payoff).toBe(false);
    expect(info.abilities["cinder.emberBolt"]?.payoff).toBe(false);
  });

  it("names the burst combos by setup>payoff class", () => {
    expect(info.combos?.["warden>cinder"]).toBe("Shatter");
    expect(info.combos?.["cinder>warden"]).toBe("Forge");
    expect(info.combos?.["warden>warden"]).toBeUndefined();
  });
});

describe("openPayoff", () => {
  it("lights the cinder's payoff abilities on a warden's Sundered Window, naming the combo", () => {
    const open = openPayoff([actor(1, "player", "cinder"), actor(102, "enemy", "choir.lieutenant", SUNDERED)], 1, 120, info, slots);
    expect(open).not.toBeNull();
    expect([...open!.abilityIds].sort()).toEqual(["cinder.coldSnap", "cinder.detonate"]);
    expect(open!.comboName).toBe("Shatter");
    expect(open!.enemyId).toBe(102);
  });

  it("is null for your own class's setup (no self-combo)", () => {
    expect(openPayoff([actor(1, "player", "cinder"), actor(102, "enemy", "e", KINDLED)], 1, 120, info, slots)).toBeNull();
  });

  it("is null once the Window has closed, on a dead enemy, or without display info", () => {
    const you = actor(1, "player", "cinder");
    expect(openPayoff([you, actor(102, "enemy", "e", SUNDERED)], 1, 160, info, slots)).toBeNull();
    expect(openPayoff([you, actor(102, "enemy", "e", SUNDERED, false)], 1, 120, info, slots)).toBeNull();
    expect(openPayoff([you, actor(102, "enemy", "e", SUNDERED)], 1, 120, undefined, slots)).toBeNull();
  });

  // PR #415 veto: the Chanter bot now opens Attuned on enemies, so your own Kindled and another class's Window can be
  // open at once. Targeting the Kindled enemy must not shadow the other Window, and the Kindled must never be the one
  // named. A new assertion (no shipped openPayoff is known to have failed it).
  it("names another class's Window while your own Kindled is open on your target (Attuned beside Kindled)", () => {
    const ATTUNED: ActorWindow = { windowId: "attuned", setupClass: "chanter", endsTick: 160, durationTicks: 60, lock: false };
    const you = { ...actor(1, "player", "cinder"), targetId: 102 };
    for (const order of [[102, 104], [104, 102]]) {
      const enemies = order.map((id) => actor(id, "enemy", "e", id === 102 ? KINDLED : ATTUNED));
      const open = openPayoff([you, ...enemies], 1, 120, info, slots);
      expect(open?.enemyId).toBe(104);
      expect(open?.comboName).toBe("Flashfire");
      expect([...open!.abilityIds].sort()).toEqual(["cinder.coldSnap", "cinder.detonate"]);
    }
    expect(openPayoff([you, actor(102, "enemy", "e", KINDLED), actor(104, "enemy", "e", ATTUNED)], 1, 160, info, slots)).toBeNull();
  });

  it("prefers your current target when several enemies carry a closable Window", () => {
    const you = { ...actor(1, "player", "cinder"), targetId: 104 };
    const open = openPayoff([you, actor(102, "enemy", "e", SUNDERED), actor(104, "enemy", "e", SUNDERED)], 1, 120, info, slots);
    expect(open?.enemyId).toBe(104);
  });
});

describe("ability bar model: payoffReady", () => {
  it("is true only on ready slots whose ability is in the open payoff set", () => {
    const open = openPayoff([actor(1, "player", "cinder"), actor(102, "enemy", "e", SUNDERED)], 1, 120, info, slots)!;
    const bar = abilityBarModel(cinder.slotAbilities, { "cinder.coldSnap": 40 }, info, 0, open.abilityIds);
    const lit = bar.filter((s) => s.payoffReady).map((s) => s.abilityId);
    expect(lit).toEqual(["cinder.detonate"]); // coldSnap is on cooldown, so not lit
    expect(abilityBarModel(cinder.slotAbilities, {}, info, 0).some((s) => s.payoffReady)).toBe(false);
  });
});
