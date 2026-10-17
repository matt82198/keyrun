/**
 * tests/bots/heal-pick.test.ts
 *
 * Chanter healer heals by SHAPE (bots/src/kit-heals.ts + roles/heal-pick.ts), real content kit:
 * Refrain is self-shaped (sim heals only the caster), Chorus is a circle around the caster,
 * Cadence (Mend) is a targeted HoT. Before the fix the bot cast Refrain "at" a dying tank 10m
 * away -- healing itself -- and the tank died untouched (tests/sim/integration/bots-pull0.test.ts).
 *
 * Usage: `npx vitest run tests/bots/heal-pick.test.ts`
 */
import { describe, expect, it } from "vitest";
import "./real-content-setup.js";
import { healerDecide } from "@bots/src/roles.js";
import { getClassKit } from "@bots/src/kits.js";
import type { ActorId, ActorState, BotView, SimState } from "@bots/src/types.js";

const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 3, kind: "player", classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 0 }, yaw: 0,
    health: 250, maxHealth: 250, resource: 1, maxResource: 1, alive: true, targetId: null,
    castId: null, castEndsTick: null, castStartTick: null, cooldowns: {}, gcdReadyTick: 0,
    effects: [], threat: {}, ...overrides,
  };
}

function makeView(actors: ActorState[]): BotView {
  const table: Record<ActorId, ActorState> = {};
  for (const a of actors) table[a.id] = a;
  const state: SimState = { tick: 10, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, telegraphs: [], classKit: NO_CLASS_KIT };
}

describe("Chanter shape-aware heals", () => {
  it("classifies Refrain as self, Chorus as group (5000mm), Cadence as the targeted heal", () => {
    expect(getClassKit("chanter")?.heals).toEqual({
      selfHealAbilityId: "chanter.refrain",
      groupHealAbilityId: "chanter.chorus",
      groupHealRadiusMm: 5000,
      targetHealAbilityId: "chanter.cadence",
      secondGroupHeal: { abilityId: "chanter.hymn", radiusMm: 2500 },
    });
  });

  it("heals a hurt tank 10m away with the targeted heal, never the self-shaped Refrain", () => {
    const healer = makeActor({ cooldowns: { "chanter.hymn": 999 } });
    const tank = makeActor({ id: 2, classId: "warden", role: "tank", health: 100, maxHealth: 400, pos: { x: 0, y: 0, z: 10000 } });
    const cmds = healerDecide(makeView([healer, tank]), healer);
    expect(cmds).toEqual([{ tick: 10, actorId: 3, kind: "cast", abilityId: "chanter.cadence", targetId: 2 }]);
  });

  it("heals itself with the self heal", () => {
    const healer = makeActor({ health: 50 });
    const cmds = healerDecide(makeView([healer]), healer);
    expect(cmds).toEqual([{ tick: 10, actorId: 3, kind: "cast", abilityId: "chanter.refrain" }]);
  });

  it("uses the group heal for an ally inside its radius", () => {
    const healer = makeActor({});
    const tank = makeActor({ id: 2, classId: "warden", role: "tank", health: 100, maxHealth: 400, pos: { x: 0, y: 0, z: 3000 } });
    const cmds = healerDecide(makeView([healer, tank]), healer);
    expect(cmds).toEqual([{ tick: 10, actorId: 3, kind: "cast", abilityId: "chanter.chorus" }]);
  });

  // Pull-1 wipe (tests/sim/integration/bots-pull1.test.ts): Chorus shadowed Hymn, so the bot never
  // cast its heal-over-time pool and the tank under five Paired Warriors out-damaged its heals.
  it("casts Hymn (the second group heal) when Chorus is down and the ally stands in Hymn's radius", () => {
    const healer = makeActor({ cooldowns: { "chanter.chorus": 999 } });
    const tank = makeActor({ id: 2, classId: "warden", role: "tank", health: 100, maxHealth: 400, pos: { x: 0, y: 0, z: 1500 } });
    const cmds = healerDecide(makeView([healer, tank]), healer);
    expect(cmds).toEqual([{ tick: 10, actorId: 3, kind: "cast", abilityId: "chanter.hymn" }]);
  });

  it("walks toward a hurt ally outside Hymn's radius while Hymn is ready", () => {
    const healer = makeActor({ cooldowns: { "chanter.chorus": 999 } });
    const tank = makeActor({ id: 2, classId: "warden", role: "tank", health: 100, maxHealth: 400, pos: { x: 0, y: 0, z: 10000 } });
    const cmds = healerDecide(makeView([healer, tank]), healer);
    expect(cmds.some((c) => c.kind === "cast")).toBe(false);
    const move = cmds.find((c) => c.kind === "move");
    expect(move && move.kind === "move" ? move.dir.z : 0).toBeGreaterThan(0);
  });

  it("never walks out of its own cast for Hymn", () => {
    const healer = makeActor({ cooldowns: { "chanter.chorus": 999 }, castId: "chanter.cadence", castEndsTick: 20, castStartTick: 0 });
    const tank = makeActor({ id: 2, classId: "warden", role: "tank", health: 100, maxHealth: 400, pos: { x: 0, y: 0, z: 10000 } });
    const cmds = healerDecide(makeView([healer, tank]), healer);
    expect(cmds.some((c) => c.kind === "move")).toBe(false);
  });
});
