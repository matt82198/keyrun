import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { parseKeyLevel, resolveRunSetupOptions } from "../../../client/src/sim-host/boot-params.js";

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
