/**
 * tests/client/sim-host/keybind-cast.test.ts
 *
 * P0 (D10): a key press in the 3D client must cast the ability in that key's slot. Before the fix,
 * input/keybinds.ts turned "abilityE" into the cast id "E", which sim/combat/rules.ts finds in no
 * ability table and silently drops, so no key ever cast and no ability-bar slot ever swept.
 *
 * Public path only (STATE.md standing rule): content/index.ts's real loadContent() -> run-setup.ts's
 * buildRunSetup -> the client worker host (worker.ts's real Worker entry with a fake `self`, and
 * SimRuntime, the class worker.ts steps) -> sim createRun/stepRun. Key presses go through
 * input/commands.ts's deriveAbilityCommands with the slot map the host itself reported; the HUD
 * side reads render/ability-bar-model.ts on the resulting snapshot. Expected ability ids are the
 * literal ids from content/data/classes/warden.json (BIBLE: 1-5 rotation, Q interrupt, E defensive,
 * R utility, F movement), NOT re-derived from the code under test.
 */
import { afterEach, describe, expect, it, vi } from "vitest";
import { loadContent } from "@content/index";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";
import { SimRuntime } from "../../../client/src/sim-host/sim-runtime.js";
import { deriveAbilityCommands } from "../../../client/src/input/commands.js";
import { DEFAULT_KEYBINDS } from "../../../client/src/input/keybinds.js";
import type { AbilityAction } from "../../../client/src/input/slot-abilities.js";
import { abilityBarModel } from "../../../client/src/render/ability-bar-model.js";
import type { ReadyMessage, SnapshotMessage, WorkerToMainMessage } from "../../../client/src/sim-host/wire.js";

vi.mock("../../../client/src/sim-host/content-registry.js", () => ({
  fetchContentRegistry: async () => loadContent(),
}));

const registry = loadContent();

const WARDEN_SLOTS: Record<AbilityAction, string> = {
  ability1: "warden.strike",
  ability2: "warden.guardBreak",
  ability3: "warden.bulwark",
  ability4: "warden.reckoning",
  ability5: "warden.vigil",
  abilityQ: "warden.shieldBash",
  abilityE: "warden.brace",
  abilityR: "warden.taunt",
  abilityF: "warden.charge",
};
const KEYS = Object.keys(WARDEN_SLOTS) as AbilityAction[];
// Slots whose ability has a cooldown and needs no target or resource at spawn, so one press from a
// fresh run must start it and set its cooldown (the others are covered at the cast-command level).
const CASTABLE_AT_SPAWN: AbilityAction[] = ["ability3", "abilityE", "abilityF"];

function wardenHost() {
  const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden" });
  return { setup, host: new SimRuntime(setup) };
}

describe("key press -> cast, through the worker host (warden)", () => {
  for (const action of KEYS) {
    it(`${DEFAULT_KEYBINDS[action]} sends exactly one cast of ${WARDEN_SLOTS[action]} and puts nothing else on cooldown`, () => {
      const { setup, host } = wardenHost();
      host.step();
      const cmds = deriveAbilityCommands(new Set([DEFAULT_KEYBINDS[action]]), DEFAULT_KEYBINDS, setup.slotAbilities, 1);
      expect(cmds).toEqual([{ type: "cast", abilityId: WARDEN_SLOTS[action], tick: 1 }]);
      for (const cmd of cmds) host.applyCommand(cmd);
      const me = host.step().actors.find((a) => a.id === setup.humanActorId)!;
      const others = Object.entries(me.cooldowns).filter(([id, left]) => id !== WARDEN_SLOTS[action] && left > 0);
      expect(others).toEqual([]);
    });
  }

  for (const action of CASTABLE_AT_SPAWN) {
    it(`${DEFAULT_KEYBINDS[action]} casts ${WARDEN_SLOTS[action]} once, sets its cooldown, and the HUD slot reports on-cooldown`, () => {
      const { setup, host } = wardenHost();
      host.step();
      for (const cmd of deriveAbilityCommands(new Set([DEFAULT_KEYBINDS[action]]), DEFAULT_KEYBINDS, setup.slotAbilities, 1)) {
        host.applyCommand(cmd);
      }
      const me = host.step().actors.find((a) => a.id === setup.humanActorId)!;
      const cd = registry.abilities.get(WARDEN_SLOTS[action])!.cooldownTicks;
      const left = me.cooldowns[WARDEN_SLOTS[action]] ?? 0;
      // Exactly one cast: one cooldown's worth (minus the tick just stepped), never two stacked.
      expect(left).toBeGreaterThan(cd - 3);
      expect(left).toBeLessThanOrEqual(cd);

      const bar = abilityBarModel(setup.slotAbilities, me.cooldowns);
      expect(bar.filter((s) => s.onCooldown).map((s) => s.action)).toEqual([action]);
      expect(bar.find((s) => s.action === action)!.abilityId).toBe(WARDEN_SLOTS[action]);
    });
  }

  it("every class resolves each key to an ability in its own kit (never a slot label)", () => {
    for (const humanClassId of ["warden", "cinder", "chanter"]) {
      const setup = buildRunSetup(registry, { keyLevel: 5, dungeonId: "hollow-vault", humanClassId });
      const kit = Object.values(registry.classes.get(humanClassId)!.abilities);
      for (const action of KEYS) expect(kit).toContain(setup.slotAbilities[action]);
    }
  });
});

describe("key press -> cast, through the real worker.ts entry (fake self/postMessage)", () => {
  afterEach(() => {
    delete (globalThis as unknown as { self?: unknown }).self;
  });

  it("ready reports warden's slot map; KeyE then sets warden.brace's cooldown and sweeps the E slot", async () => {
    vi.resetModules();
    const posted: WorkerToMainMessage[] = [];
    let handler: ((ev: { data: unknown }) => void) | null = null;
    (globalThis as unknown as { self: unknown }).self = {
      addEventListener: (_t: string, l: (ev: { data: unknown }) => void) => (handler = l),
      postMessage: (m: WorkerToMainMessage) => posted.push(m),
    };
    await import("../../../client/src/sim-host/worker.js");
    const fire = (data: unknown) => handler!({ data });
    const waitFor = async (pred: () => boolean) => {
      const t0 = Date.now();
      while (!pred()) {
        if (Date.now() - t0 > 10_000) throw new Error("timed out");
        await new Promise((r) => setTimeout(r, 5));
      }
    };

    fire({ type: "init", search: "?key=5&dungeon=hollow-vault&class=warden", tickMs: 1 });
    await waitFor(() => posted.some((m) => m.type === "ready"));
    const ready = posted.find((m): m is ReadyMessage => m.type === "ready")!;
    expect(ready.slotAbilities).toEqual(WARDEN_SLOTS);

    for (const command of deriveAbilityCommands(new Set(["KeyE"]), DEFAULT_KEYBINDS, ready.slotAbilities, 1)) {
      fire({ type: "command", command });
    }
    const swept = () =>
      posted.some((m) => {
        if (m.type !== "snapshot") return false;
        const me = (m as SnapshotMessage).snapshot.actors.find((a) => a.id === ready.humanActorId);
        return !!me && abilityBarModel(ready.slotAbilities, me.cooldowns).some((s) => s.action === "abilityE" && s.onCooldown);
      });
    await waitFor(swept);
    fire({ type: "command", command: { type: "abandon", tick: 2 } }); // ends the run so worker.ts stops its own interval
  });
});
