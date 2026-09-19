import { describe, expect, it, vi } from "vitest";
import { loadContent } from "@content/index";
import { parseClauseId, parseKeyLevel, resolveRunSetupOptions } from "../../../client/src/sim-host/boot-params.js";

const registry = loadContent();

describe("boot-params: parseKeyLevel", () => {
  it("clamps to the 2-20 range", () => {
    expect(parseKeyLevel("1")).toBe(2);
    expect(parseKeyLevel("25")).toBe(20);
    expect(parseKeyLevel("10")).toBe(10);
  });

  it("defaults to 5 for missing/invalid input", () => {
    expect(parseKeyLevel(null)).toBe(5);
    expect(parseKeyLevel("banana")).toBe(5);
  });

  it("truncates a fractional level", () => {
    expect(parseKeyLevel("7.9")).toBe(7);
  });
});

describe("boot-params: resolveRunSetupOptions", () => {
  it("passes through valid params", () => {
    const url = new URLSearchParams("key=8&dungeon=hollow-vault&class=cinder");
    expect(resolveRunSetupOptions(url, registry)).toEqual({ keyLevel: 8, dungeonId: "hollow-vault", humanClassId: "cinder" });
  });

  it("falls back to the only real dungeon for an unknown ?dungeon=", () => {
    const url = new URLSearchParams("dungeon=no-such-place");
    expect(resolveRunSetupOptions(url, registry).dungeonId).toBe("hollow-vault");
  });

  it("falls back to warden for an unknown/missing ?class=", () => {
    expect(resolveRunSetupOptions(new URLSearchParams("class=paladin"), registry).humanClassId).toBe("warden");
    expect(resolveRunSetupOptions(new URLSearchParams(""), registry).humanClassId).toBe("warden");
  });
});

// run-options-wiring: `?clause=` rides the Play URL (screen/src/clauses.ts's buildPlayUrl) but was
// previously never read by any host -- this is the P0 this lane fixes on the client side.
describe("boot-params: parseClauseId", () => {
  it("passes through a real content.clauses id", () => {
    const [firstId] = [...registry.clauses.keys()];
    expect(firstId).toBeDefined();
    expect(parseClauseId(firstId!, registry)).toBe(firstId);
  });

  it("returns undefined (no Clause) for an absent/empty param -- never a crash", () => {
    expect(parseClauseId(null, registry)).toBeUndefined();
    expect(parseClauseId("", registry)).toBeUndefined();
    expect(parseClauseId("   ", registry)).toBeUndefined();
  });

  it("falls back to undefined (with a console.warn) for an unknown clause id, same convention as ?class=/?dungeon=", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    expect(parseClauseId("not-a-real-clause", registry)).toBeUndefined();
    expect(warn).toHaveBeenCalledWith(expect.stringContaining("not-a-real-clause"));
    warn.mockRestore();
  });
});

describe("boot-params: resolveRunSetupOptions carries clauseId through", () => {
  it("includes a real ?clause= in the resolved options", () => {
    const [firstId] = [...registry.clauses.keys()];
    const url = new URLSearchParams(`key=8&class=cinder&clause=${firstId}`);
    expect(resolveRunSetupOptions(url, registry).clauseId).toBe(firstId);
  });

  it("omits clauseId entirely when absent (byte-identical to today's shape)", () => {
    const url = new URLSearchParams("key=8&dungeon=hollow-vault&class=cinder");
    expect(resolveRunSetupOptions(url, registry)).toEqual({ keyLevel: 8, dungeonId: "hollow-vault", humanClassId: "cinder" });
    expect("clauseId" in resolveRunSetupOptions(url, registry)).toBe(false);
  });
});
