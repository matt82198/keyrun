/**
 * tests/bots/banked-charge-spend.test.ts
 *
 * CHARGE-SPEND (ruling R-PILE-7d, finding f-f44791bc). Twin of
 * game/bots/Keyrun.Bots.Tests/BankedChargeSpendTests.cs: the WoW Mythic+ rule for a charged heal. With every charge
 * full the healer spends one (never let charges cap); otherwise the spare is banked and spent only on a real trigger,
 * the target under the emergency threshold (heal-pick.ts CHARGE_EMERGENCY_PCT) or no other heal reaching the target
 * ready this GCD. A one-charge ability's choice is unchanged.
 *
 * Usage: `npx vitest run tests/bots/banked-charge-spend.test.ts`
 */
import "./real-content-setup.js";
import { describe, expect, it } from "vitest";
import { healCommands } from "@bots/src/roles/heal-pick.js";
import { resolveKit, type EffectiveKit } from "@bots/src/kits.js";
import type { ActorId, ActorState, BotView, Command, SimState } from "@bots/src/types.js";

const NO_VIEW_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };
const CHORUS = "chanter.chorus";
const CADENCE = "chanter.cadence";

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 2, kind: "player", classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 100,
    maxHealth: 100, resource: 0, maxResource: 0, alive: true, targetId: null, castId: null, castEndsTick: null,
    castStartTick: null, cooldowns: {}, gcdReadyTick: 0, effects: [], threat: {}, ...overrides,
  };
}

function makeView(actors: ActorState[]): BotView {
  const table: Record<ActorId, ActorState> = {};
  for (const a of actors) table[a.id] = a;
  const state: SimState = { tick: 700, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, telegraphs: [], classKit: NO_VIEW_KIT };
}

/** The healer at tick 700, the tank 1000 mm away (in Chorus's circle; Hymn held on cooldown to isolate Chorus). */
function decide(encore: boolean, chorusReadyTick: number | null, tankHealth: number, cadenceReadyTick: number | null = null): Command[] {
  const base = resolveKit("chanter", NO_VIEW_KIT);
  const kit: EffectiveKit = encore
    ? { ...base, chargedAbilities: { ...base.chargedAbilities, [CHORUS]: { charges: 2, cooldownTicks: 100 } } }
    : base;
  const cooldowns: Record<string, number> = { "chanter.hymn": 999 };
  if (chorusReadyTick !== null) cooldowns[CHORUS] = chorusReadyTick;
  if (cadenceReadyTick !== null) cooldowns[CADENCE] = cadenceReadyTick;
  const healer = makeActor({ cooldowns });
  const tank = makeActor({ id: 1, classId: "warden", role: "tank", health: tankHealth, pos: { x: 1000, y: 0, z: 0 } });
  return healCommands(makeView([tank, healer]), healer, kit, tank);
}

function castOf(cmds: Command[]): string | undefined {
  expect(cmds).toHaveLength(1);
  const c = cmds[0]!;
  return c.kind === "cast" ? c.abilityId : undefined;
}

describe("charged heal spend policy (R-PILE-7d)", () => {
  it("holds a banked Encore charge for a tank at 65 percent just healed", () => {
    expect(castOf(decide(true, 792, 65))).toBe(CADENCE);
  });

  it("spends a banked Encore charge on a tank under the emergency threshold", () => {
    expect(castOf(decide(true, 792, 30))).toBe(CHORUS);
  });

  it("spends one charge at full charges so the recharge is not wasted", () => {
    expect(castOf(decide(true, null, 65))).toBe(CHORUS);
    expect(castOf(decide(true, 700, 65))).toBe(CHORUS);
  });

  it("spends a banked charge when no other heal reaches the target this GCD", () => {
    expect(castOf(decide(true, 792, 65, 999))).toBe(CHORUS);
  });

  it("leaves a one-charge Chorus decision unchanged", () => {
    expect(resolveKit("chanter", NO_VIEW_KIT).chargedAbilities?.[CHORUS]).toBeUndefined();
    expect(castOf(decide(false, null, 65))).toBe(CHORUS);
    expect(castOf(decide(false, 700, 65))).toBe(CHORUS);
    expect(castOf(decide(false, 812, 65))).toBe(CADENCE);
    expect(castOf(decide(false, 812, 30))).toBe(CADENCE);
  });
});
