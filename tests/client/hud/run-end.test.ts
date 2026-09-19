// Usage: client/src/hud/run-end.ts, post P2 (policy-inside-stepRun) -- this file no longer owns a
// run-end state machine (sim/index.ts's stepRun decides WHEN a run ends now; see that file's
// docstring), so there is nothing left to unit-test at that layer here. What remains: the static
// STUB_CARDS content and postRunEndChoice's outbound message shape (RunEndView itself needs a real
// DOM, which this repo has no jsdom/happy-dom for -- see tests/screen/dom-stub.ts's own docstring
// for why -- so it is instead verified visually via the `?net=1` Playwright screenshot).
import { describe, expect, it, vi } from "vitest";
import { postRunEndChoice, STUB_CARDS } from "../../../client/src/hud/run-end.js";

describe("hud/run-end: STUB_CARDS", () => {
  it("ships exactly three placeholder choice cards with distinct ids", () => {
    expect(STUB_CARDS).toHaveLength(3);
    expect(new Set(STUB_CARDS.map((c) => c.id)).size).toBe(3);
  });
});

describe("hud/run-end: postRunEndChoice", () => {
  it("posts the chosen card id and outcome via window.postMessage", () => {
    const postMessage = vi.fn();
    vi.stubGlobal("window", { postMessage });
    postRunEndChoice("gilded-echo", "depleted");
    expect(postMessage).toHaveBeenCalledWith({ type: "keyrun:run-end-choice", cardId: "gilded-echo", outcome: "depleted" }, "*");
    vi.unstubAllGlobals();
  });
});
