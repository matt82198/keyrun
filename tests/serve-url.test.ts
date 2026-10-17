import { describe, expect, it } from "vitest";
// scripts/serve-url.mjs is plain JS with JSDoc types; tsconfig has no allowJs, so TS cannot read them.
// @ts-expect-error TS7016: untyped .mjs helper (see report: .d.mts sidecar is the alternative)
import { pickServeHost, playableUrl } from "../scripts/serve-url.mjs";

const PLAYABLE_SUFFIX = "/client/index.html?key=5&dungeon=hollow-vault&class=warden";

describe("pickServeHost", () => {
  it("prefers the first entry of hosts (esbuild >= 0.25 shape)", () => {
    expect(pickServeHost({ hosts: ["127.0.0.1"], port: 8791 })).toBe("127.0.0.1");
  });

  it("uses the first hosts entry when several are returned", () => {
    expect(pickServeHost({ hosts: ["10.0.0.226", "127.0.0.1"], port: 8791 })).toBe("10.0.0.226");
  });

  it("falls back to host for older esbuild shapes", () => {
    expect(pickServeHost({ host: "0.0.0.0", port: 8791 })).toBe("0.0.0.0");
  });

  it("falls back to 127.0.0.1 when neither field is present, never undefined", () => {
    expect(pickServeHost({ port: 8791 })).toBe("127.0.0.1");
  });

  it("ignores an empty hosts array and uses host", () => {
    expect(pickServeHost({ hosts: [], host: "0.0.0.0" })).toBe("0.0.0.0");
  });
});

describe("playableUrl", () => {
  it("builds the clickable client URL with the fixed query string", () => {
    expect(playableUrl("127.0.0.1", 8791)).toBe(`http://127.0.0.1:8791${PLAYABLE_SUFFIX}`);
  });

  it("never contains the literal text undefined for the host-less case", () => {
    const url = playableUrl(pickServeHost({ port: 8791 }), 8791);
    expect(url).not.toContain("undefined");
    expect(url).toBe(`http://127.0.0.1:8791${PLAYABLE_SUFFIX}`);
  });
});
