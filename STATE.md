# STATE.md — Keyrun

**Intent (Matt, 2026-09-18):** "isolating m+ from WoW and just making it an open and queue game like Dark and Darker,
and let people manage their characters from a simple screen... study other mmos, dungeon games, gear progression, and
find specifically what people find exciting/fun, build talent trees, classes, the dungeon POC with initial enemies,
abilities, a boss. Mechanics like interrupts, but be more creative than blizzard devs... 4-5 rotational abilities per
class max, 10 total keybinds should be plenty for rotation, movement, utility... the goal is class synergy." No art
assets; pure POC.

## Locked decisions
- D1 **Stack**: TypeScript for sim, content, client, bots, server; Vitest; esbuild; Three.js for the 3D client; no engine.
- D2 **Bots-first**: the first playable is one human plus bot party against the dungeon; networking is phase 4.
- D3 **3D MVP** (Matt, 2026-09-18: "2d is not playable, please 3d mvp"): third-person camera, WASD + mouse-look,
  placeholder geometry only. The sim stays headless and deterministic; 3D is the renderer plus a flat-arena
  movement model (no navmesh in the POC). Readability of mechanics beats everything.
- D4 **Constraints as design**: ≤5 rotational abilities, ≤10 keybinds, synergy-first classes.
- D5 **Research before design**: no class, talent, gear, or boss mechanic enters the bible without a cited
  precedent for WHY it is fun — but the precedent is the reason, never the design.
- D6 **Original, not a copy** (Matt, 2026-09-18: "it does not need to be a copy of any of the big games, use your
  creative reasoning"): the bible must invent Keyrun's own classes, synergy system, mechanics vocabulary and loop.
  Fellowship (Chief Rebel, 2025) is the closest precedent — reviewed well while admittedly half-baked — and is studied
  specifically for the gap between what it promised and what it shipped.

- D7 **Bounded progression** (Matt, 2026-09-18: "there is an ending to progression, a technical bis that is hard to
  achieve... because otherwise characters scale infinitely and break the game"): power has a cap; best-in-slot is
  reachable but hard; post-cap progression is cosmetic, prestige, and new keys — never more stats. Progression is
  the most important system in the bible.

- D8 **North star** (Matt, 2026-09-18): "m+ at its core is extremely fun, but it lives in a game where every class is
  gutted, way too easy, and it is just boring right now. I miss being able to log in during late DF to early TWW and
  get into high keys instantly and play with people who were chill." Three tests every design decision must pass:
  (1) classes with real depth and keys that are genuinely hard; (2) from launch to a high key in under a minute,
  no gatekeeping; (3) the social defaults make strangers chill — mercy, backfill, no score-shaming.
- D9 **Tick rate — DECIDED 2026-09-18 (orchestrator, unattended; research/11):** stay at 20 Hz. Measured on the
  playable (65 s/arm, same seed): 30 Hz cut input→tick p95 from 51 ms to 34 ms but cost +43% CPU, +50% bytes/s,
  +31% render stalls, and content is authored in raw ticks so the flip alone runs the game 1.5x fast. Reverses if:
  a real Web Worker removes the scheduler-shaped stalls AND content constants are rescaled in lockstep AND the
  latency gain still shows in play tests.

## Phase plan
- Phase 1 (research day): 9 cited briefs (incl. Fellowship) → design/BIBLE.md v1 (Matt reviews the taste calls).
- Phase 2: sim/ rules engine (deterministic, tested) + content v1 (3 classes, 1 dungeon, 1 boss).
- Phase 3: bots/ + client/ (Three.js) → playable in a browser tab, one human + bots.
- Phase 4: server/ authoritative tick + queue/lobby + character screen → 5 humans.
- Phase 5: tuning with Matt playing.

## NEXT STEPS (2026-09-18 evening — phase 3 reached)
Playable: `npm run dev` → `http://127.0.0.1:8791/client/index.html?key=5&dungeon=hollow-vault&class=warden` (or cinder/chanter). Main a8d5e76, 534 tests, `test` check required on main (strict).
1. Land in-flight lanes: fix/aoe-faction (P0: area shapes hit own side), fix/encounter-run-wiring (break bar + onDeath + enemy range through stepRun), feat/bots-floor-avoid, research/tick-rate-measure (decides D9 with numbers).
2. Then: chanter-stance needs Matt — `[DECIDE]` verse semantics (BIBLE §3/§5) before the sim gets a stance system; other BIBLE `[DECIDE]` items still parked (class names, combo numbers, key range/par time, Ascension visibility).
3. Phase 4 (server: authoritative tick, WebSocket, queue/lobby, backfill) is next once the sim seams above are closed; the client worker host is the contract the server replaces.
4. Session end: /refinesystem to zero verified defects — the day's pattern is layer-wiring defects that only public-path (`createRun`+`stepRun`) repros catch; every convergence audit must use that path.
