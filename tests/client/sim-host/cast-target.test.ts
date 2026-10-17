/**
 * tests/client/sim-host/cast-target.test.ts
 *
 * D10 first-run defect (live play, before/after screenshots): the human spawns with no target, and
 * player-commands.ts sent every cast at `targetId ?? humanId` -- so pressing 1 (warden.strike, a
 * hostile single-target hit) struck the Warden HIMSELF for 80 and put the yellow target ring under
 * his own feet. A hostile single-target cast with no live enemy targeted must auto-target the
 * nearest live enemy. Driven through the real client host (SimRuntime over real content).
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";

const registry = loadContent();

describe("human cast targeting", () => {
  it("pressing Strike with no target hits the nearest enemy, never yourself", () => {
    const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
    const host = new SimRuntime(setup);
    let snap = host.step();
    const me = () => snap.actors.find((a) => a.id === setup.humanActorId)!;
    const startHp = me().health;
    for (let i = 0; i < 12; i++) {
      host.applyCommand({ type: "move", dx: 0, dz: -180, tick: snap.tick }); // into melee range of the acolytes
      snap = host.step();
    }
    host.applyCommand({ type: "cast", abilityId: "warden.strike", tick: snap.tick });
    snap = host.step();
    const strike = (snap.events ?? []).find((e) => e.type === "cast" && e.actorId === setup.humanActorId && e.abilityId === "warden.strike");
    expect(strike).toBeDefined();
    if (strike?.type !== "cast") throw new Error("no strike cast event");
    expect(strike.hitIds).not.toContain(setup.humanActorId);
    expect(strike.hitIds.length).toBe(1);
    const victim = snap.actors.find((a) => a.id === strike.hitIds[0])!;
    expect(victim.kind).toBe("enemy");
    expect(me().targetId).toBe(victim.id);
    expect(me().health).toBeLessThanOrEqual(startHp); // enemies may hit back; Strike itself never does
  });
});
