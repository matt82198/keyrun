/**
 * client/src/sim-host/metrics.ts
 *
 * Instrumentation for research/11-tick-rate-measurement.md (backlog item tick-rate-measure, D9).
 * A fixed-capacity ring buffer per series plus percentile math, exposed on
 * `window.__keyrunMetrics` and (with `?metrics=1`) as a one-line HUD readout, so a Playwright
 * script can drive the real playable and pull numbers without touching devtools.
 *
 * Four series, matched to what this architecture can actually measure (see worker.ts's own file
 * docstring: SimHost runs on the MAIN THREAD via a timer, not a real Worker -- there is no
 * postMessage hop to time):
 *   - simStepMs: wall-clock duration of one `SimHost.step()` call (stepRun + bookkeeping).
 *   - tickIntervalMs: real ms between successive tick callbacks, vs. the scheduler's target
 *     tickMs -- this main-thread-timer architecture's stand-in for "worker->main latency": it is
 *     scheduling jitter, not IPC, and is documented as such in research/11.
 *   - renderStalenessMs: when a render frame lands before the next snapshot has arrived, alpha
 *     (client/src/main.ts) exceeds 1 and interpolateSnapshots holds at the last known transform --
 *     this is the visible cost of that hold, in ms. Zero whenever snapshots keep pace with frames.
 *   - inputToTickMs: wall-clock ms from a keydown to the tick callback that samples it.
 *
 * Usage: `metrics.recordSimStep(ms)`, `metrics.recordTickInterval(ms, tickMs)`,
 * `metrics.recordRenderAlpha(alpha, tickMs)`, `metrics.noteKeyDown(code, atMs)` /
 * `metrics.noteInputSample(codes, atMs)`, `metrics.recordSnapshotBytes(n)`. `attachMetricsHud()`
 * mounts the HUD line iff `location.search` carries `metrics=1`; safe to call unconditionally.
 */

const RING_CAPACITY = 4096;

export class Ring {
  private buf: number[] = [];
  push(v: number): void {
    this.buf.push(v);
    if (this.buf.length > RING_CAPACITY) this.buf.shift();
  }
  values(): readonly number[] {
    return this.buf;
  }
  count(): number {
    return this.buf.length;
  }
  sum(): number {
    return this.buf.reduce((a, b) => a + b, 0);
  }
  mean(): number {
    return this.buf.length === 0 ? 0 : this.sum() / this.buf.length;
  }
  /** Nearest-rank percentile (p in [0, 100]); 0 on an empty ring. */
  percentile(p: number): number {
    if (this.buf.length === 0) return 0;
    const sorted = [...this.buf].sort((a, b) => a - b);
    const rank = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));
    return sorted[rank]!;
  }
  clear(): void {
    this.buf.length = 0;
  }
}

export interface MetricSummary {
  count: number;
  p50: number;
  p95: number;
  p99: number;
  mean: number;
}

export function summarize(r: Ring): MetricSummary {
  return { count: r.count(), p50: r.percentile(50), p95: r.percentile(95), p99: r.percentile(99), mean: r.mean() };
}

export class KeyrunMetrics {
  readonly simStepMs = new Ring();
  readonly tickIntervalMs = new Ring();
  readonly renderStalenessMs = new Ring();
  readonly inputToTickMs = new Ring();
  readonly snapshotBytes = new Ring();
  hz = 20;
  startedAtMs = typeof performance !== "undefined" ? performance.now() : 0;
  private pendingInputAt = new Map<string, number>();

  recordSimStep(ms: number): void {
    this.simStepMs.push(ms);
  }
  /** `elapsedMs` is the real time since the previous tick callback; jitter = elapsedMs - tickMs. */
  recordTickInterval(elapsedMs: number, tickMs: number): void {
    this.tickIntervalMs.push(elapsedMs - tickMs);
  }
  recordRenderAlpha(alpha: number, tickMs: number): void {
    if (alpha > 1) this.renderStalenessMs.push((alpha - 1) * tickMs);
  }
  recordSnapshotBytes(n: number): void {
    this.snapshotBytes.push(n);
  }
  noteKeyDown(code: string, atMs: number): void {
    if (!this.pendingInputAt.has(code)) this.pendingInputAt.set(code, atMs);
  }
  noteInputSample(codes: readonly string[], atMs: number): void {
    for (const code of codes) {
      const queuedAt = this.pendingInputAt.get(code);
      if (queuedAt !== undefined) {
        this.inputToTickMs.push(atMs - queuedAt);
        this.pendingInputAt.delete(code);
      }
    }
  }

  /** Mean sim-step ms times ticks/sec: CPU-ms spent simulating per one second of sim-time. */
  cpuMsPerSimSecond(): number {
    return this.simStepMs.mean() * this.hz;
  }
  bytesPerSecond(): number {
    const nowMs = typeof performance !== "undefined" ? performance.now() : 0;
    const elapsedS = (nowMs - this.startedAtMs) / 1000;
    return elapsedS > 0 ? this.snapshotBytes.sum() / elapsedS : 0;
  }
  /** How many whole ticks a `castSeconds`-long cast leaves to interrupt at this.hz. */
  interruptWindowTicks(castSeconds: number): number {
    return Math.floor(castSeconds * this.hz);
  }

  summary(): Record<string, unknown> {
    const nowMs = typeof performance !== "undefined" ? performance.now() : 0;
    return {
      hz: this.hz,
      elapsedMs: nowMs - this.startedAtMs,
      simStepMs: summarize(this.simStepMs),
      tickIntervalMs: summarize(this.tickIntervalMs),
      renderStalenessMs: summarize(this.renderStalenessMs),
      inputToTickMs: summarize(this.inputToTickMs),
      cpuMsPerSimSecond: this.cpuMsPerSimSecond(),
      snapshotBytesPerSecond: this.bytesPerSecond(),
      interruptWindowTicks1500ms: this.interruptWindowTicks(1.5),
    };
  }

  /** Clears every series and rebases the clock; call after warmup, before the measured window. */
  reset(hz?: number): void {
    if (hz !== undefined) this.hz = hz;
    this.simStepMs.clear();
    this.tickIntervalMs.clear();
    this.renderStalenessMs.clear();
    this.inputToTickMs.clear();
    this.snapshotBytes.clear();
    this.pendingInputAt.clear();
    this.startedAtMs = typeof performance !== "undefined" ? performance.now() : 0;
  }
}

export const metrics = new KeyrunMetrics();

declare global {
  interface Window {
    __keyrunMetrics?: KeyrunMetrics;
  }
}
if (typeof window !== "undefined") window.__keyrunMetrics = metrics;

function formatHudLine(): string {
  const s = summarize(metrics.simStepMs);
  const r = summarize(metrics.renderStalenessMs);
  const i = summarize(metrics.inputToTickMs);
  return (
    `hz=${metrics.hz} step p50/p95=${s.p50.toFixed(2)}/${s.p95.toFixed(2)}ms ` +
    `stall p95=${r.p95.toFixed(1)}ms input p50/p95=${i.p50.toFixed(1)}/${i.p95.toFixed(1)}ms ` +
    `cpu/s=${metrics.cpuMsPerSimSecond().toFixed(2)}ms bytes/s=${metrics.bytesPerSecond().toFixed(0)}`
  );
}

/** Mounts the `?metrics=1` HUD line as an independent floating element, driven by its own rAF
 * loop -- no coupling to client/src/main.ts's render loop or render/hud.ts's Hud class. */
export function attachMetricsHud(): void {
  if (typeof window === "undefined" || typeof document === "undefined") return;
  if (new URLSearchParams(window.location.search).get("metrics") !== "1") return;
  const el = document.createElement("div");
  el.id = "keyrun-metrics-hud";
  el.setAttribute(
    "style",
    "position:absolute;left:8px;bottom:8px;padding:4px 8px;background:rgba(6,8,12,0.75);" +
      "color:#eef2f6;font:11px/1.3 monospace;border-radius:4px;pointer-events:none;z-index:9999;",
  );
  document.body.appendChild(el);
  const tick = (): void => {
    el.textContent = formatHudLine();
    requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
}
