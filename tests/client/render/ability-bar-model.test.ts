/**
 * tests/client/render/ability-bar-model.test.ts
 *
 * First-run readability (D10, Matt's live-play "no abilities" report): the ability bar must say
 * WHAT each key casts, not just which key it is. Drives the real worker-side display-info build
 * (sim-host/display-info.ts) over the REAL content registry and the real SimRuntime, then reads
 * render/ability-bar-model.ts's per-slot model: name, cooldown fraction against the ability's own
 * cooldown, and the GCD state the sim really reports (ActorSnapshot.gcdRemainingTicks).
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";
import { buildDisplayInfo, humanizeId } from "../../../client/src/sim-host/display-info.js";
import { abilityBarModel } from "../../../client/src/render/ability-bar-model.js";

const registry = loadContent();
const setup = () => buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });

describe("ability bar model: every slot names its ability", () => {
  it("warden's nine slots carry the content display names, in key order", () => {
    const s = setup();
    const info = buildDisplayInfo(registry, s.content.abilities);
    const bar = abilityBarModel(s.slotAbilities, {}, info);
    expect(bar.map((slot) => slot.name)).toEqual([
      "Strike", "Guard Break", "Bulwark", "Reckoning", "Vigil", "Shield Bash", "Brace", "Taunt", "Charge",
    ]);
    expect(bar.every((slot) => slot.ready)).toBe(true);
  });

  it("with no display info (the ?net=1 path) the name falls back to the humanized ability id", () => {
    const bar = abilityBarModel(setup().slotAbilities, {});
    expect(bar.map((slot) => slot.name)).toEqual([
      "Strike", "Guard Break", "Bulwark", "Reckoning", "Vigil", "Shield Bash", "Brace", "Taunt", "Charge",
    ]);
    expect(humanizeId("hollowWarden.boss")).toBe("Hollow Warden");
  });

  it("display info names enemies and classes from content, never from the id", () => {
    const info = buildDisplayInfo(registry, setup().content.abilities);
    expect(info.actorNames["choir.lieutenant"]).toBe("Choir Cantor");
    expect(info.actorNames["hollowWarden.boss"]).toBe("The Hollow Warden");
    expect(info.actorNames["warden"]).toBe("Warden");
    expect(info.abilities["choir.healCast"]).toMatchObject({ name: "Choral Mending", castTicks: 80 });
  });
});

describe("ability bar model: cooldown and GCD state come from the real sim", () => {
  it("pressing 1 (Strike) starts the GCD: on-GCD slots dim, off-GCD Shield Bash/Brace stay ready", () => {
    const s = setup();
    const info = buildDisplayInfo(registry, s.content.abilities);
    const host = new SimRuntime(s);
    let snap = host.step();
    for (let i = 0; i < 12; i++) {
      host.applyCommand({ type: "move", dx: 0, dz: -180, tick: snap.tick });
      snap = host.step();
    }
    host.applyCommand({ type: "cast", abilityId: "warden.bulwark", tick: snap.tick });
    snap = host.step();
    const me = snap.actors.find((a) => a.id === s.humanActorId)!;
    expect(me.gcdRemainingTicks).toBeGreaterThan(0);
    const bar = abilityBarModel(s.slotAbilities, me.cooldowns, info, me.gcdRemainingTicks);
    const by = (action: string) => bar.find((slot) => slot.action === action)!;
    expect(by("ability1").gcdActive).toBe(true);
    expect(by("ability1").ready).toBe(false);
    expect(by("abilityQ").gcdActive).toBe(false);
    expect(by("abilityE").ready).toBe(true);
    // Bulwark itself: on its own 200-tick cooldown, fraction measured against that cooldown.
    expect(by("ability3").onCooldown).toBe(true);
    expect(by("ability3").cooldownFraction).toBeGreaterThan(0.9);
    expect(by("ability3").cooldownFraction).toBeLessThanOrEqual(1);
  });
});
