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
- D9 **Tick rate**: 20 Hz for the POC as a single `TICK_HZ` constant; research/07 argues 30 Hz; revisit after
  the first playable with the number measured, not argued.

## Phase plan
- Phase 1 (research day): 9 cited briefs (incl. Fellowship) → design/BIBLE.md v1 (Matt reviews the taste calls).
- Phase 2: sim/ rules engine (deterministic, tested) + content v1 (3 classes, 1 dungeon, 1 boss).
- Phase 3: bots/ + client/ (Three.js) → playable in a browser tab, one human + bots.
- Phase 4: server/ authoritative tick + queue/lobby + character screen → 5 humans.
- Phase 5: tuning with Matt playing.

## NEXT STEPS (2026-09-18, phase 3 in progress)
1. Land #14 (progression) and #15 (integrate-sim: `sim/index.ts` createRun/stepRun, client worker on the real sim, golden replay).
2. Land lanes content-sim-align (P0), bots-v1, apply-talents-facets; then dispatch playable-v1 (client + bots + content, Hollow Vault end to end).
3. Then clauses, window-radius-field, dedupe-target-resolution, screen panels for talents/facets, tick-rate-measure (D9).
4. Matt's `[DECIDE]` items in design/BIBLE.md (class names, combo numbers, key range/par time, Ascension visibility) stay parked until he weighs in.
5. Session end: /refinesystem to zero verified defects.
