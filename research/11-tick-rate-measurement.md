# 11 — 20 Hz vs 30 Hz on the real playable: measuring D9

## 1. The question

STATE.md D9 locked 20 Hz for the POC pending a measured revisit: "research/07 argues 30 Hz;
revisit after the first playable with the number measured, not argued." PR #36 landed the
playable (`client/`, bots-first, Hollow Vault). This brief drives that real build in a real
browser at both rates, same seed, same scripted input, and reports what changed with numbers.

## 2. Method

### 2.1 Architecture discovery: which constant is actually "the tick rate"

`sim/core/tick.ts` exports `TICK_HZ = 20` with the comment "the single source of truth for tick
rate." It is not. Grepping the whole repo for `TICK_HZ`/`TICK_MS` outside `sim/` and its own tests
finds zero importers — `sim/index.ts` only imports `step` itself. The constant that actually
paces the playable in real time is a **separate, client-only** constant:
`client/src/sim-host/snapshot.ts`'s `SIM_HZ = 20` / `SIM_TICK_MS`, consumed by
`client/src/sim-host/worker.ts`'s `setInterval` loop and by `client/src/main.ts`'s render-alpha
calculation. The two constants currently agree (both 20) but are wired independently — nothing
enforces that they stay in sync. This is a real, if currently latent, footgun and is called out as
a recommendation in §5.

Because the real pacing knob lives entirely in `client/`, this measurement lane could add a
`?hz=` URL override (`boot-params.ts`'s `parseHz`, threaded into `SimHost`'s constructor as an
optional `tickMs` override in `worker.ts`) **without touching a single byte under `sim/`** —
satisfying the brief's conditional ("a `?hz=` param... if the sim exposes a way to construct with
a different tick rate without editing `sim/`") by construction: the sim's own tick counter
(`state.tick`) is agnostic to wall-clock pacing; only the client's scheduler cares, and the
client is this lane's own territory. No throwaway worktree was needed.

### 2.2 The caveat this discovery forces: content is authored in ticks, not milliseconds

`content/data/TUNING.md`: *"Ticks are 20 Hz (50 ms); GCD is fixed at 30 ticks (1.5 s)."* Every
cast/GCD/cooldown/buff duration in `content/data/*.json` is a raw tick count, not a real-time
value converted through `TICK_HZ`. Flipping the real-time pacing to 30 Hz **without also
rescaling every content tick constant by 1.5x** does not preserve gameplay pacing — it speeds
the whole game up by 1.5x in wall-clock time, because a "30-tick cast" is still 30 ticks at either
rate, just compressed into less real time. `content/**` is out of scope for this lane (measurement
only, per the dispatch brief), so this run measures the **as-authored** behavior, not a
rescaled one — and that is itself the load-bearing finding for D9 (§4).

Directly observed: `cinder.flare` (`content/data/abilities/cinder.json`) has `castTicks: 30`. At
20 Hz that is a 1.500 s cast; at 30 Hz, the same ability, same content, same code, is a **1.000 s**
cast — 33% faster, not finer-grained at constant duration. The run timer itself shows this: both
arms ran the identical scripted-input script for the identical wall-clock duration after an
identical warmup; the in-game clock read **0:14** on 20 Hz's short smoke pass vs **1:46** on 30
Hz's full pass against **1:10** on 20 Hz's full pass for the same real elapsed time —
`106s / 70s = 1.51`, matching the tick-rate ratio (`30/20 = 1.5`) almost exactly
(`research/img/hz-20-hud.png`, `research/img/hz-30-hud.png`).

### 2.3 Instrumentation

`client/src/sim-host/metrics.ts` (new, 187 LOC): a fixed-capacity (4096-sample) ring buffer per
series with nearest-rank percentiles, exposed on `window.__keyrunMetrics` and, behind `?metrics=1`,
as a one-line HUD readout mounted independently of `render/hud.ts`. Four series, named for what
this architecture can actually measure — `worker.ts`'s own file docstring records that `SimHost`
runs on the **main thread via a timer, not a real `Worker`** (the build has no worker-splitting
support); there is no `postMessage` hop to time, so "worker→main latency" is redefined honestly as
scheduler jitter:

- **`simStepMs`** — wall-clock duration of one `SimHost.step()` call (`stepRun` + bookkeeping).
- **`tickIntervalMs`** — real ms between tick callbacks minus the target tick interval (signed
  jitter); the main-thread-timer stand-in for "worker→main latency."
- **`renderStalenessMs`** — when a render frame lands before the next snapshot has arrived,
  `main.ts`'s interpolation `alpha` exceeds 1 and the visual holds at the last known transform;
  this is that hold's duration. The practical "interpolation error" for a system with no true
  client-side prediction (see research/07 §4: phase 1 is "lockstep of one," not
  predict-and-reconcile).
- **`inputToTickMs`** — wall-clock ms from a `keydown` to the tick callback that samples it
  (`keyState.sampleAndClearJustPressed()`).

Plus `snapshotBytes` (`JSON.stringify(snapshot).length` per tick, for bytes/sec) and two derived
numbers: `cpuMsPerSimSecond = mean(simStepMs) * hz`, `interruptWindowTicks(castSeconds) =
floor(castSeconds * hz)`. 17 unit tests: `tests/client/metrics.test.ts`.

Wiring cost, in the files this lane touched beyond its three new files: `worker.ts` (+22 LOC:
`tickMs` field/override, step timing, tick-interval/snapshot-byte recording — 141→163 LOC, gate
limit 190), `boot-params.ts` (+9 LOC: `parseHz`), `main.ts` (net **+0** LOC — it was already at
the 190-LOC gate ceiling; every hook line was folded onto an existing statement and five blank
lines were removed to make room, e.g. `const alpha = ...; metrics.recordRenderAlpha(alpha,
simHost.tickMs);` on one physical line). `sim/`, `content/`, `bots/`, `screen/`, `tests/repo/**`:
untouched.

### 2.4 Runner

`scripts/measure-tick-rate.mjs` (new): spawns `node scripts/build.mjs --watch --serve` if nothing
answers on port 8791, launches headless Chromium via Playwright, and for each arm
(`hz=20`, `hz=30`):

1. Navigates to `http://127.0.0.1:8791/client/index.html?key=5&dungeon=hollow-vault&class=warden&hz=<HZ>&metrics=1`.
2. Waits for `window.__keyrunMetrics` to exist (boot complete), clicks the page to focus it for
   keyboard events, waits a 5 s warmup (registry fetch, first pull spawn, first few ticks settle),
   then calls `window.__keyrunMetrics.reset()` so the measured window starts clean.
3. Runs a fixed choreography for **65 s**: cycles WASD movement in 450 ms holds and presses
   1/2/3/4/5/Q/E/R/F round-robin roughly every 570 ms (move-key-down interval), for real,
   consumed keyboard input, not synthetic sim commands.
4. Waits 500 ms for trailing ticks/frames, reads `summary()`, screenshots the `?metrics=1` HUD to
   `research/img/hz-<HZ>-hud.png`.

Same seed for both arms trivially: `worker.ts`'s `SimHost` hardcodes `SEED = 1` — not a runner
parameter, so there was nothing to hold constant on purpose; it already is. Same dungeon
(Hollow Vault), same key level (5), same class (warden), same scripted input shape (identical
choreography code) for both arms. Raw output: `research/tick-rate-results.json`.

## 3. Results (65 s measured window each, after 5 s warmup)

Sample counts: 20 Hz produced 1317 ticks in 65.83 s (19.998 ticks/s — exact); 30 Hz produced 1983
ticks in 66.10 s (30.00 ticks/s — exact). Ratio 1983/1317 = 1.506, matching the intended 1.5x.

| Metric | 20 Hz p50 | 20 Hz p95 | 20 Hz p99 | 30 Hz p50 | 30 Hz p95 | 30 Hz p99 |
|---|---|---|---|---|---|---|
| Sim step duration (ms) | 0.10 | 0.30 | 0.30 | 0.10 | 0.20 | 0.30 |
| Tick-interval jitter (ms, signed) | 0.0 | 17.0 | 31.0 | -0.33 | 16.67 | 19.67 |
| Render staleness (ms, alpha>1 events) | 3.30 | 19.70 | 35.70 | 4.07 | 21.57 | 22.77 |
| Input → tick latency (ms) | 18.40 | 51.20 | 64.40 | 16.80 | 33.50 | 49.70 |

| Derived | 20 Hz | 30 Hz | Ratio (30/20) |
|---|---|---|---|
| CPU time per second of sim (mean step × hz) | 2.867 ms/s | 4.091 ms/s | 1.427x |
| Snapshot bytes per second | 44,546 B/s | 66,581 B/s | 1.495x |
| Render-staleness event count (of ~65s of frames) | 296 | 387 | 1.31x |
| Interrupt-window ticks, idealized (`floor(1.5s * hz)`) | 30 | 45 | 1.5x |
| Interrupt-window ticks, **as-authored content** (`cinder.flare`, `castTicks:30`) | 30 ticks / 1.500 s real | 30 ticks / 1.000 s real | 1.0x ticks, 0.667x real time |
| In-game run clock after identical real elapsed time | 1:10 | 1:46 | 1.51x |

Screenshots of the live `?metrics=1` HUD for both arms: `research/img/hz-20-hud.png`,
`research/img/hz-30-hud.png`.

## 4. Reading the numbers

**CPU cost scales sub-linearly with a slight per-tick discount, not superlinearly.** Mean sim-step
duration actually *dropped* slightly at 30 Hz (0.143 ms → 0.136 ms per tick — noise-level, this POC
content is tiny), so the 1.427x CPU-per-sim-second increase is almost entirely the 1.5x tick-count
increase, not per-tick overhead growth. At ~4 ms of CPU per second of sim-time, absolute cost is a
non-issue for this POC either way (well under 1% of a core) — CPU is not the argument against 30
Hz here, at this content scale.

**Bytes/sec scales ~linearly with tick rate (1.495x for a 1.5x rate increase)** because each tick
serializes a full-actor-state snapshot (no delta compression yet — that's a `server/`-phase
concern per research/07 §4). This matters once phase 4 puts this over a real WebSocket to 5
clients: 30 Hz costs ~1.5x the eventual network bandwidth of 20 Hz for the same content, before any
wire-format optimization.

**Input → tick latency genuinely improves at the tail.** p50 drops modestly (18.4ms → 16.8ms,
tracking the smaller half-tick-interval expectation: 25ms → 16.7ms theoretical), but p95 drops
substantially (51.2ms → 33.5ms, -34.6%) and p99 similarly (64.4ms → 49.7ms, -22.8%). This is
research/07's actual argument for finer granularity showing up as a real, measured number: less
worst-case wait for a keypress to reach the sim.

**Render staleness is a measured counter-argument, not a fabricated one.** Both mean (4.63ms →
6.88ms) and event count (296 → 387, +31%) got *worse* at 30 Hz, not better, in this specific
scaffold. The likely cause is `worker.ts`'s own documented deviation: `SimHost` runs on a
main-thread `setInterval`, not a real Worker or `requestAnimationFrame`-synced loop; a tighter
33.3ms target interval is proportionally harder for `setInterval`'s coarse timer resolution and
main-thread contention (rendering + input handling sharing the same thread) to hit exactly than a
50ms one, so more render frames land in the gap. This is very likely a property of *this
scaffold's scheduler*, not of 30Hz simulation in general — a real `Worker` (this POC's own stated
eventual target, per `worker.ts`'s docstring) would remove main-thread contention as a jitter
source entirely. Flagged honestly rather than smoothed over: at 30 Hz, *on this scaffold*, the
visible cost of a missed frame is slightly more frequent, even though each individual instance is
about the same size (p95/p99 in the same ballpark, ~20-23ms either way).

**The decisive number is §2.2/§3's last two rows, not the tick-count table.** Bumping the real-time
pacing constant to 30 Hz without touching `content/` does not deliver "30Hz feel with unchanged
pacing" — it delivers the *same* tick-count game running 1.5x fast in wall-clock time (the run
timer directly demonstrates this: 1:46 vs 1:10 for identical real elapsed time). The
"floor(1.5s*hz)" idealized interrupt-window row (30→45 ticks) is what research/07 originally
proposed and is only true if every content tick constant is rescaled by 1.5x in lockstep — a
`content/` change explicitly out of this lane's scope, and not yet done anywhere in the repo.

## 5. Recommendation for D9

**Keep `TICK_HZ = 20` for now; do not flip the constant alone.** The measured case *for* 30 Hz is
real but narrow: input-latency tail (-23% to -35%) and, in principle, finer interrupt-window
granularity *if* content is rescaled to match. The measured case *against* flipping it today is
concrete: bytes/sec (+49.5%, a real cost once `server/` ships), CPU (+42.7%, currently trivial but
non-zero), more frequent render-staleness events on the current main-thread-timer scaffold
(+31%), and — the disqualifying one — every cast/GCD/cooldown/buff in `content/data/*.json` is
authored in raw ticks assuming 20 Hz (`TUNING.md`'s own words), so flipping `TICK_HZ` today changes
game pacing by 1.5x as a side effect, not a controlled variable. That is not "30 Hz, measured
better"; it is "a different, unintended game, not yet measured against the bible's design intent
(GCD feel, cast timings, D8's Slack/par-time tuning) at all."

**What would reverse this recommendation:** (1) a `content/` pass that rescales every
`castTicks`/`gcdTicks`/`cooldownTicks`/duration constant by exactly 1.5x (30→45 GCD, 20→30 cast,
etc.) so 30 Hz preserves today's real-time pacing while gaining tick resolution — at that point the
idealized interrupt-window row (45 vs 30 ticks in the same 1.5s) becomes the real comparison, and
the input-latency tail improvement (this brief's strongest positive number) is retained without a
pacing side effect; and/or (2) `worker.ts` moving off the main-thread-timer deviation onto a real
`Worker` (already the stated eventual target), which should remove render-staleness as an argument
against 30 Hz — that regression looks scheduler-shaped, not tick-rate-shaped, and should be
re-measured once it's gone. Absent both, D9 stays 20 Hz: the measured wins for 30 Hz do not yet
outweigh a pacing change nobody decided to make.

## 6. Caveats

- One 65 s scripted-input run per arm, not a distribution of runs — `tickIntervalMs`/
  `renderStalenessMs` counts (296 vs 387) are large enough to read directionally but this is not a
  statistically powered comparison; a repeat-N-times version of this runner would tighten the
  render-staleness finding in §4 specifically.
- Scripted input is a fixed choreography (WASD chords + round-robin ability presses), not a human
  or a bot's decision policy — representative of input *cadence*, not of real play patterns
  (target selection, movement toward telegraphs, etc.).
- `renderStalenessMs`/`inputToTickMs` are both shaped by `worker.ts`'s documented main-thread-timer
  deviation (§4); neither number should be read as "what a shipped server-authoritative build would
  show," only as "what this playable shows today."

## 7. Follow-up: real Worker

§4/§5 flagged the render-staleness regression at 30Hz as "very likely a property of this
scaffold's scheduler, not of 30Hz simulation in general," pointing at `worker.ts`'s own documented
deviation: `SimHost` ran on the main thread via `setInterval`, not a real Worker, so ticking
competed with rendering and input handling for the same thread. This lane (`refactor/client-sim-host`,
PR: client `sim-hz-single-source` / `real-web-worker` / `split-client-main` backlog items) moved the
fixed-timestep sim loop onto an actual `Worker` (bundled as its own esbuild entry,
`client/src/sim-host/worker.ts`; main thread <-> worker talk only through `wire.ts`'s postMessage
shapes) and re-measured render staleness with the exact same method as §2.3-2.4: `scripts/measure-tick-rate.mjs`'s
own scripted-input choreography (WASD chords + round-robin ability presses), 65s measured window
after a 5s warmup, same seed (`SimRuntime`'s hardcoded `SEED = 1`), same dungeon/key/class
(Hollow Vault, key 5, warden), hz=20 (the locked default — this follow-up does not revisit D9's
20Hz-vs-30Hz decision, only whether moving off the main-thread-timer scaffold fixes the jitter
source §4 blamed for the 30Hz regression).

| Metric (hz=20, 65s window) | Before (main-thread timer, §3) | After (real Worker) | Change |
|---|---|---|---|
| Render-staleness event count | 296 | 107 | -63.9% |
| Render staleness p50 (ms) | 3.30 | 3.90 | +18.2% |
| Render staleness p95 (ms) | 19.70 | 18.70 | -5.1% |
| Render staleness p99 (ms) | 35.70 | 21.60 | -39.5% |
| Tick count (of ~65s) | 1317 | 1319 | matches (19.99-20.00 ticks/s both arms) |
| Sim step p50/p95 (ms) | 0.10 / 0.30 | 0.10 / 0.30 | unchanged |
| CPU time per sim-second (mean step x hz) | 2.867 ms/s | 2.908 ms/s | +1.4%, noise-level |

**Stalls dropped, substantially, on the count that matters most.** The render-staleness *event
count* -- how often a render frame lands before the next snapshot has arrived at all -- fell from
296 to 107 (-63.9%) at the same 20Hz tick rate, with the same scripted input, the same content. This
is the number §4 predicted moving off the main-thread-timer scaffold should improve, since it
removes ticking as a source of main-thread contention with rendering/input handling, and it did.

**The per-event size (p50/p95/p99 ms) did not move as cleanly, and honestly should be read as a
wash, not a further win.** p99 dropped a real amount (35.70ms -> 21.60ms, -39.5%) and p95 dropped
slightly (19.70ms -> 18.70ms, -5.1%), but p50 went *up* (3.30ms -> 3.90ms, +18.2%) -- the real
Worker has fewer stall events, but a "typical" stall among the ones that still happen is not
smaller. Sim step cost and CPU/s are unchanged (both arms ran the identical `SimRuntime.step()`
logic; only where it runs changed), which is the expected result and a useful sanity check that
this migration did not silently change simulation behavior.

Screenshots: `research/img/hz-20-hud.png` (before, from §3) vs `research/img/hz-20-real-worker-hud.png`
(after, this follow-up) -- both are the live `?metrics=1` HUD after a 65s scripted run at hz=20.

**Reading this against §5's "what would reverse this recommendation" list:** item (2) there --
"`worker.ts` moving off the main-thread-timer deviation onto a real `Worker`... should remove
render-staleness as an argument against 30Hz... that regression looks scheduler-shaped, not
tick-rate-shaped, and should be re-measured once it's gone" -- is now done. This follow-up only
re-measured the 20Hz arm (the locked default); it does NOT re-run the 30Hz arm, so it does not by
itself flip D9. A future lane re-measuring 30Hz against this real-Worker build, alongside item (1)
(a `content/` pass rescaling tick constants by 1.5x), is what would actually revisit D9 -- this
entry only closes out the scheduler half of §5's list and records that the fix worked at the
rate D9 already locked.

**Caveat, same shape as §6:** one 65s run, not a distribution; this is directional, not
statistically powered. Input-sampling now runs on its own main-thread scheduler
(`sim-host-client.ts`) decoupled from the worker's stepping scheduler (`worker.ts`) -- there is no
synchronous round-trip across the postMessage boundary, which is architecturally closer to what a
real client/server split will look like, but is a real behavioral difference from the pre-Worker
scaffold's single combined loop, not just an implementation detail.
