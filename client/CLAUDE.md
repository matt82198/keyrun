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
