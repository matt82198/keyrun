/**
 * tests/bots/damage-behavior.test.ts
 *
 * damageDecide()'s Window/Combo play (design/BIBLE.md Sec 4) against real Warden/Cinder class
 * kits (content/data/classes/*.json via bots/src/kits.ts): prefer a Payoff when another class's
 * Window is open on the target (never the bot's own class's Window -- Sec 9 "The Lock"); prefer a
 * Setup when no Window is open and a Payoff-capable ally is in range; and Cinder's heat-overheat
 * resource-awareness (never casts into a forced Overheat/Chill when a heat dump -- Cold Snap -- is
 * ready).
 *
 * Usage: `npx vitest run tests/bots/damage-behavior.test.ts`
 */
import { describe, expect, it } from "vitest";
import "./real-content-setup.js";
import { damageDecide, pickResourceAwareRotation } from "@bots/src/roles.js";
import { WINDOW_EFFECT_PREFIX } from "@bots/src/shared/pings.js";
import type { EffectiveKit } from "@bots/src/kits.js";
import type { ActorId, ActorState, BotView, SimState } from "@bots/src/types.js";

const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 1,
    kind: "player",
    classId: "cinder",
    role: "damage",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 100,
    maxHealth: 100,
    resource: 0,
    maxResource: 100,
    alive: true,
    targetId: null,
    castId: null,
    castEndsTick: null,
    castStartTick: null,
    cooldowns: {},
    gcdReadyTick: 0,
    effects: [],
    threat: {},
    ...overrides,
  };
}

function makeView(actors: ActorState[], tick = 10): BotView {
  const table: Record<ActorId, ActorState> = {};
  for (const a of actors) table[a.id] = a;
  const state: SimState = { tick, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, telegraphs: [], classKit: NO_CLASS_KIT };
}

describe("Payoff play: only closes another class's open Window", () => {
  it("a Cinder bot targets and casts its Payoff on a Window a Warden opened", () => {
    const warden = makeActor({ id: 2, classId: "warden" });
    const enemy = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}sundered`, sourceId: 2, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    const cinder = makeActor({ id: 5, classId: "cinder", resource: 50 });
    const view = makeView([warden, enemy, cinder]);

    const commands = damageDecide(view, cinder);

    expect(commands).toEqual([
      { tick: 10, actorId: 5, kind: "target", targetId: 100 },
      { tick: 10, actorId: 5, kind: "cast", abilityId: "cinder.coldSnap", targetId: 100 },
    ]);
  });

  it("The Lock: a Cinder bot will not treat a Window another Cinder opened as closable", () => {
    const otherCinder = makeActor({ id: 2, classId: "cinder" });
    const enemy = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}kindled`, sourceId: 2, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    const cinder = makeActor({ id: 5, classId: "cinder", resource: 0 });
    const view = makeView([otherCinder, enemy, cinder]);

    const commands = damageDecide(view, cinder);

    // No Payoff-on-Window targeting happened (no "target" command); the bot falls back to its
    // plain rotation instead of re-targeting the uncombable Window.
    expect(commands.some((c) => c.kind === "target")).toBe(false);
  });
});

describe("Setup play: only opens a Window when a Payoff-capable ally is close enough", () => {
  it("a Warden bot Setups when a Cinder ally is within range and no Window is open", () => {
    const cinderAlly = makeActor({ id: 5, classId: "cinder", pos: { x: 10, y: 0, z: 0 } });
    const enemy = makeActor({ id: 100, kind: "enemy" });
    const warden = makeActor({ id: 2, classId: "warden", resource: 50, pos: { x: 0, y: 0, z: 0 } });
    const view = makeView([cinderAlly, enemy, warden]);

    const commands = damageDecide(view, warden);

    expect(commands).toEqual([
      { tick: 10, actorId: 2, kind: "target", targetId: 100 },
      { tick: 10, actorId: 2, kind: "cast", abilityId: "warden.strike", targetId: 100 },
    ]);
  });

  it("does not Setup when no Payoff-capable ally is in range (falls back to its own rotation)", () => {
    const farCinderAlly = makeActor({ id: 5, classId: "cinder", pos: { x: 10000, y: 0, z: 0 } });
    const enemy = makeActor({ id: 100, kind: "enemy" });
    const warden = makeActor({ id: 2, classId: "warden", resource: 50, pos: { x: 0, y: 0, z: 0 } });
    const view = makeView([farCinderAlly, enemy, warden]);

    const commands = damageDecide(view, warden);

    expect(commands.some((c) => c.kind === "cast" && (c.abilityId === "warden.strike" || c.abilityId === "warden.bulwark"))).toBe(
      false
    );
  });
});

describe("Cinder heat-overheat resource-awareness", () => {
  it("pickResourceAwareRotation: never lets a builder overflow the resource when a ready dump exists", () => {
    const kit: EffectiveKit = {
      rotation: ["builder", "dump"],
      interruptAbilityId: null,
      defensiveAbilityId: null,
      healAbilityId: null,
      selfSustainAbilityId: null,
      stanceSwapAbilityId: null,
      dispelAbilityId: null,
      setupAbilityIds: [],
      payoffAbilityIds: ["dump"],
      windowIdBySetupAbility: {},
      costByAbility: { builder: 0, dump: 0 },
      resourceGainByAbility: { builder: 30, dump: -50 },
      resourceDumpAbilityIds: ["dump"],
      resourceBuilderAbilityIds: ["builder"],
      loopKind: "heat-overheat",
      resource: "heat",
      resourceMax: 100,
    };
    const self = makeActor({ resource: 90, cooldowns: {} });

    expect(pickResourceAwareRotation(kit, self, 10)).toBe("dump");
  });

  it("pickResourceAwareRotation: accepts the builder (and the coming Overheat) when no dump is ready", () => {
    const kit: EffectiveKit = {
      rotation: ["builder", "dump"],
      interruptAbilityId: null,
      defensiveAbilityId: null,
      healAbilityId: null,
      selfSustainAbilityId: null,
      stanceSwapAbilityId: null,
      dispelAbilityId: null,
      setupAbilityIds: [],
      payoffAbilityIds: ["dump"],
      windowIdBySetupAbility: {},
      costByAbility: { builder: 0, dump: 0 },
      resourceGainByAbility: { builder: 30, dump: -50 },
      resourceDumpAbilityIds: ["dump"],
      resourceBuilderAbilityIds: ["builder"],
      loopKind: "heat-overheat",
      resource: "heat",
      resourceMax: 100,
    };
    const self = makeActor({ resource: 90, cooldowns: { dump: 999 } });

    expect(pickResourceAwareRotation(kit, self, 10)).toBe("builder");
  });

  it("integration: a Cinder bot with Cold Snap ready never picks a heat-builder", () => {
    const cinder = makeActor({ id: 5, classId: "cinder", resource: 90, targetId: 100, cooldowns: { "cinder.detonate": 999 } });
    const enemy = makeActor({ id: 100, kind: "enemy" });
    const view = makeView([cinder, enemy]);

    const commands = damageDecide(view, cinder);

    expect(commands).toEqual([{ tick: 10, actorId: 5, kind: "cast", abilityId: "cinder.coldSnap", targetId: 100 }]);
  });

  it("integration: with no dump or finisher ready, a Cinder bot still builds Heat (push-your-luck is intentional)", () => {
    const cinder = makeActor({
      id: 5,
      classId: "cinder",
      resource: 90,
      targetId: 100,
      cooldowns: { "cinder.detonate": 999, "cinder.coldSnap": 999, "cinder.cinderfield": 999 },
    });
    const enemy = makeActor({ id: 100, kind: "enemy" });
    const view = makeView([cinder, enemy]);

    const commands = damageDecide(view, cinder);

    expect(commands).toEqual([{ tick: 10, actorId: 5, kind: "cast", abilityId: "cinder.flare", targetId: 100 }]);
  });
});
