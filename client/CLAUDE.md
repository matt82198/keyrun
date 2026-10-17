# client/ — the 3D browser client (Three.js)

**Purpose**: render the sim and turn a human's input into commands. It never decides game rules.

## Rules
- Three.js from cdnjs (pinned version) in `client/index.html`; TypeScript in `client/src/` bundled by esbuild to
  `client/dist/`; no framework, no other libraries.
- Third-person camera: orbit behind the player capsule, mouse-look with pointer lock on right-click hold
  (WoW-style), scroll to zoom, WASD relative to camera yaw, Space = jump placeholder (no-op in sim for POC).
- Input → commands: the client samples input every render frame and emits at most one movement command and
  queued ability commands per sim tick (20 Hz), with the tick number attached. Keybinds: 1–5 rotational,
  Q/E/R/F utility, plus movement; ten total, configurable in one JSON.
- Rendering reads immutable sim snapshots and interpolates between the last two ticks; the render loop never
  blocks the sim loop. `client/src/render/` draws: ground plane grid, actor capsules coloured by role/class,
  health/resource bars as sprites, cast bars, telegraph decals (circles, cones, lines) from telegraph events
  with the tick horizon, floating damage numbers (text sprites), a target reticle, an ability bar with cooldown
  sweeps, the run timer.
- Everything is placeholder geometry and text. No models, no textures, no audio in the POC.
- Bots-first: the client runs the sim locally in a Web Worker with bots for the other party members; the same
  client later swaps the local sim for a server connection without changing the render code.

## Layout
`client/src/main.ts` (boot), `input/` (keys, pointer lock, keybinds.json), `camera/`, `render/` (scene, actors,
bars, telegraphs, hud), `sim-host/` (worker that runs `sim.step` + bots; later `net/` replaces it).

## Tests
Unit tests for input→command mapping (given key state and tick, expect commands), camera math, interpolation,
and the telegraph horizon math. Rendering is checked by a headless smoke test that builds the scene without a
canvas (jsdom + WebGL mock) and by a manual screenshot before merge.
- Event latching contract: the host may deliver several snapshots per render frame; `hud-wiring.ts` renders only
  the latest STATE but latches every snapshot's one-shot `events` in `render/event-latch.ts` (arrival order,
  bounded at `MAX_LATCHED_EVENTS`, oldest dropped) and drains them exactly once into the next frame's
  `ActorPool.sync`. Never read `nextSnapshot.events` directly (tests/client/hud-wiring-event-latch.test.ts).
- Window ring contract: `render/window-ring.ts` draws a floor ring around each live enemy whose snapshot
  `ActorSnapshot.window` (projected by `sim-host/snapshot-codec.ts`'s `toWindow`) is open; arc = remaining
  fraction, colour green -> amber -> red as it closes; removed on close or on a kick drained into
  `ActorPool.sync` events. Snapshot state only, called once from `ActorPool.sync` (tests/client/render/window-ring.test.ts).
- Window ring correction (supersedes "or on a kick" above): ring lifetime is driven ONLY by `ActorSnapshot.window`
  (present and `tick < endsTick` => ring; null/absent/expired => none). A kick never dismisses it -- only a Payoff
  closes a Window in the sim (`sim/combat/payoff.ts` removes the effect, so `toWindow` projects null that tick;
  tests/client/sim-host/snapshot-window.test.ts).
- Camera feel: every constant is in `CAMERA_TUNING` (`camera/cameraMath.ts`, pure, tests/client/camera-feel.test.ts):
  `followHalfLifeS` position-lag half-life, `zoomHalfLifeS` zoom easing, `snapDistanceM` teleport snap gap,
  `maxFrameDtS` dt cap, `mouseSensitivityRadPerPx` look speed, `pitchMinRad`/`pitchMaxRad` pitch clamp (max<0 keeps
  the camera above ground), `initialPitchRad`, `minDistanceM`/`maxDistanceM`/`initialDistanceM` zoom range,
  `zoomStepM` per wheel notch, `lookAtHeightM` aim point. Yaw is unsmoothed (it is the WASD basis). Space emits one
  `dash` per press; the sim moves `DASH_DISTANCE_MM` along this tick's move, else facing ([DECIDE] placeholder).
- Net dash parity: `net/mapping.ts` translates `dash` with `sim-host/player-commands.ts`'s `resolveDashDir` (the one
  direction authority): the last sent `move` if stamped this same tick, else the human's last-snapshot facing, both
  supplied by `net-client.ts` (tests/client/net/mapping.test.ts, net-parity.test.ts over a real ws room).
- Boss bar contract: `hud/boss-bar.ts` is a top-centre DOM bar, one stacked row per live actor that
  `roleColor.ts` colours `COLOR_BOSS` (classId ends ".boss"), ordered by actor id; name from classId, fill
  `COLOR_BOSS`, percent rounded up. Fed `snapshot.actors` via `GameLoopDeps.bossBar` (tests/client/hud/boss-bar.test.ts).
- Net window + dash yaw: `net/mapping.ts` copies `ActorSnapshot.window` through (absent/null -> null) so ?net=1 draws
  the Window ring; `resolveDashDir`'s facing fallback uses sim `faceDir` (yaw 0 = +x, 256 = +z, the yawFromComponents
  convention), round-tripped at 16 yaws (tests/client/sim-host/dash-dir.test.ts, net/net-parity.test.ts).
- Run-end overlay visibility: `.run-end-root[hidden] { display: none }` in `index.html` must outrank the class's `display: flex`, or the overlay dims the scene and eats every canvas pointer event all run. Guarded by the headless-Chromium `npm run test:e2e` (tests/client/e2e/run-end-overlay.e2e.ts; not in `npm test`), which reads boot.ts's read-only `window.__keyrunCamera`.
- Run readout (D10): `render/run-readout.ts` (pure) gives `formatPullReadout` -> 1-based "Pull <pullIndex+1>/<pullCount>" ("Pull <n>" if no count) and `partyMarkers` (player actors by id: alive, HP fraction); `Hud.updateRunReadout(snapshot)` draws the top-left `.hud-run-readout` (pointer-events none), called per snapshot from `hud-wiring.ts` (tests/client/render/run-readout.test.ts, e2e tests/client/e2e/run-readout.e2e.ts: no overlap with the ability bar or timer at 1280x800).
- First-run readability (D10, Matt's live play). Names: `sim-host/display-info.ts` builds ability names/cooldown/gcd/cast ticks + actor names (enemy defs, classes) worker-side, shipped as `ready.display`; ?net=1 has none, so labels fall back to `humanizeId`. Bar: `render/ability-bar.ts` (from `render/ability-bar-model.ts`) shows key badge, NAME, Q/E/R/F caption, cooldown sweep vs the ability's own cooldown, GCD dim from `ActorSnapshot.gcdRemainingTicks`, `data-state` ready|cooldown|gcd|empty, plus the `.hud-key-legend` line. Objective: `render/objective.ts` ("Clear pull n/N: 2x Choir Acolyte, ..." / "Boss: <name> <pct>%"). Onboarding: `hud/onboarding.ts`, once per boot, first keydown dismisses. Mount + read-only `window.__keyrunReadability`: `readability.ts`.
- Projections: `sim-host/cast-events.ts` turns stepRun's `cast_complete` (+ same-source damage/heal = `hitIds`) into one-shot `Snapshot.events` `{type:"cast"}`; `render/cast-fx.ts` draws each for 0.7 s (circle disc, cone sector, line strip, target beam + ring, self ring; YOU cyan, bots role colour, enemies red) and floats the ability name for YOU and enemies; `render/actors.ts` flashes `hitIds` (white hostile, green friendly). `sim-host/cast-feedback.ts` adds `{type:"cast_failed", reason}` for a human press that cast nothing ("Out of range", "Not ready yet"); `Hud` shows it over the bar. Local host only: server/src/snapshot.ts does not emit these yet.
- Ground frame: `render/ground-frame.ts` is the one decal orientation authority (local XY, forward +x, flatten then yaw under Euler "YXZ"). Telegraphs use the ENCOUNTER facing (yaw 0 = +z, sim/encounter/telegraphs.ts), casts the COMBAT facing (yaw 0 = +x, sim/combat/targeting.ts); tests/client/render/ground-frame.test.ts checks drawn points against both sim resolvers.
- Actors: `render/actor-view.ts` (child order [0] capsule [1] health [2] resource [3] cast bar [4] name tag [5] facing nose [6] YOU ring) + `render/actor-label.ts` (YOU / [T|H|D] bot / enemy name / BOSS, per-role scale, "casting X - KICK!" for an enemy's interruptible cast) + `render/name-tag.ts` (constant screen size). Human casts: `sim-host/player-commands.ts`'s `castTargetFor` sends a hostile single-target cast with no live enemy targeted at the nearest enemy (was: at yourself).
- Window + ping readability (D10): `render/window-ring.ts` floats `windowRingLabel` ("Warden Window: Sundered", "LOCK: alternate classes") over each ring; `render/payoff-ready.ts`'s `openPayoff` (combo pair "<setupClass>><your classId>" from `DisplayInfo.combos`, payoff abilities from `AbilityDisplay.payoff`, your target first) feeds `Hud.updateAbilityBar`'s `payoffIds`, and the bar marks those slots `data-payoff="ready"` with a green glow. `ping-wiring.ts`: spot pings land where the camera centre (crosshair) ray meets y=0 (`aimedGroundPoint`, else the pinger), target pings get "Kick!"/"Window!" context from the raw snapshot (`pingContextFor`), and read-only `window.__keyrunPings` lists drawn pings (tests/client/hud-wiring-ping-window.test.ts, render/payoff-ready.test.ts, render/window-ring-label.test.ts, e2e/window-ping.e2e.ts).
