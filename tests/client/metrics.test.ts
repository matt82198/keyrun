import { beforeEach, describe, expect, it } from "vitest";
import { Ring, summarize, KeyrunMetrics } from "../../client/src/sim-host/metrics.js";

describe("metrics: Ring", () => {
  it("computes count, sum and mean", () => {
    const r = new Ring();
    [1, 2, 3, 4].forEach((v) => r.push(v));
    expect(r.count()).toBe(4);
    expect(r.sum()).toBe(10);
    expect(r.mean()).toBe(2.5);
  });

  it("nearest-rank percentile matches a known distribution", () => {
    const r = new Ring();
    for (let i = 1; i <= 100; i += 1) r.push(i);
    expect(r.percentile(50)).toBe(50);
    expect(r.percentile(95)).toBe(95);
    expect(r.percentile(99)).toBe(99);
    expect(r.percentile(100)).toBe(100);
  });

  it("returns 0 for every stat on an empty ring", () => {
    const r = new Ring();
    expect(r.count()).toBe(0);
    expect(r.sum()).toBe(0);
    expect(r.mean()).toBe(0);
    expect(r.percentile(95)).toBe(0);
  });

  it("caps at its fixed capacity, dropping the oldest sample first (a ring, not an unbounded log)", () => {
    const r = new Ring();
    for (let i = 0; i < 4096; i += 1) r.push(0);
    r.push(999);
    expect(r.count()).toBe(4096);
    expect(r.percentile(100)).toBe(999);
    for (let i = 0; i < 4096; i += 1) r.push(0); // pushes the 999 sample out the front
    expect(r.percentile(100)).toBe(0);
  });

  it("clear() empties the ring", () => {
    const r = new Ring();
    r.push(1);
    r.push(2);
    r.clear();
    expect(r.count()).toBe(0);
  });
});

describe("metrics: summarize", () => {
  it("reports p50/p95/p99/mean/count together", () => {
    const r = new Ring();
    for (let i = 1; i <= 10; i += 1) r.push(i);
    const s = summarize(r);
    expect(s.count).toBe(10);
    expect(s.mean).toBeCloseTo(5.5);
    expect(s.p50).toBe(5);
  });
});

describe("metrics: KeyrunMetrics", () => {
  let m: KeyrunMetrics;

  beforeEach(() => {
    m = new KeyrunMetrics();
  });

  it("recordSimStep feeds simStepMs", () => {
    m.recordSimStep(1.5);
    m.recordSimStep(2.5);
    expect(m.simStepMs.count()).toBe(2);
    expect(summarize(m.simStepMs).mean).toBeCloseTo(2);
  });

  it("recordTickInterval stores elapsed-minus-target jitter, signed", () => {
    m.recordTickInterval(55, 50); // 5ms late
    m.recordTickInterval(48, 50); // 2ms early
    expect(m.tickIntervalMs.values()).toEqual([5, -2]);
  });

  it("recordRenderAlpha only records staleness when alpha overruns 1", () => {
    m.recordRenderAlpha(0.4, 50);
    m.recordRenderAlpha(1.0, 50);
    expect(m.renderStalenessMs.count()).toBe(0);
    m.recordRenderAlpha(1.2, 50); // 0.2 * 50ms = 10ms held stale
    expect(m.renderStalenessMs.count()).toBe(1);
    expect(m.renderStalenessMs.values()[0]).toBeCloseTo(10);
  });

  it("noteKeyDown + noteInputSample pairs the first sample that consumes a queued key", () => {
    m.noteKeyDown("Digit1", 1000);
    m.noteInputSample(["Digit1"], 1020);
    expect(m.inputToTickMs.values()).toEqual([20]);
    // A second sample with nothing pending for that code records nothing new.
    m.noteInputSample(["Digit1"], 1050);
    expect(m.inputToTickMs.count()).toBe(1);
  });

  it("noteKeyDown keeps the earliest timestamp if called twice before a sample consumes it", () => {
    m.noteKeyDown("KeyW", 100);
    m.noteKeyDown("KeyW", 140); // ignored: one is already pending
    m.noteInputSample(["KeyW"], 150);
    expect(m.inputToTickMs.values()).toEqual([50]);
  });

  it("cpuMsPerSimSecond multiplies mean step time by hz", () => {
    m.hz = 20;
    m.recordSimStep(2);
    m.recordSimStep(4);
    expect(m.cpuMsPerSimSecond()).toBeCloseTo(3 * 20);
  });

  it("interruptWindowTicks floors castSeconds*hz", () => {
    m.hz = 20;
    expect(m.interruptWindowTicks(1.5)).toBe(30);
    m.hz = 30;
    expect(m.interruptWindowTicks(1.5)).toBe(45);
  });

  it("recordSnapshotBytes accumulates for bytesPerSecond", () => {
    m.recordSnapshotBytes(100);
    m.recordSnapshotBytes(200);
    expect(m.snapshotBytes.sum()).toBe(300);
  });

  it("summary() returns every documented field", () => {
    m.recordSimStep(1);
    m.recordTickInterval(51, 50);
    m.recordRenderAlpha(1.1, 50);
    m.noteKeyDown("Digit1", 0);
    m.noteInputSample(["Digit1"], 10);
    m.recordSnapshotBytes(50);
    const s = m.summary();
    expect(s).toHaveProperty("hz");
    expect(s).toHaveProperty("simStepMs");
    expect(s).toHaveProperty("tickIntervalMs");
    expect(s).toHaveProperty("renderStalenessMs");
    expect(s).toHaveProperty("inputToTickMs");
    expect(s).toHaveProperty("cpuMsPerSimSecond");
    expect(s).toHaveProperty("snapshotBytesPerSecond");
    expect(s).toHaveProperty("interruptWindowTicks1500ms");
  });

  it("reset() clears every ring, resets pending input, and can rebase hz", () => {
    m.recordSimStep(1);
    m.recordTickInterval(51, 50);
    m.recordRenderAlpha(1.1, 50);
    m.noteKeyDown("Digit1", 0);
    m.recordSnapshotBytes(50);
    m.reset(30);
    expect(m.hz).toBe(30);
    expect(m.simStepMs.count()).toBe(0);
    expect(m.tickIntervalMs.count()).toBe(0);
    expect(m.renderStalenessMs.count()).toBe(0);
    expect(m.snapshotBytes.count()).toBe(0);
    // pending input map was cleared too: this sample now finds nothing queued.
    m.noteInputSample(["Digit1"], 1000);
    expect(m.inputToTickMs.count()).toBe(0);
  });

  it("reset() without an hz argument keeps the current hz", () => {
    m.hz = 30;
    m.reset();
    expect(m.hz).toBe(30);
  });
});
