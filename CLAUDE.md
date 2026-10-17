# Keyrun — Project CLAUDE.md

**What**: a proof-of-concept co-op dungeon game that isolates the Mythic+ loop from WoW — queue up, run a timed
dungeon with a small party, kill a boss, come back to a simple character screen and get stronger — with the bloat
cut: 4–5 rotational abilities per class, 10 keybinds total, class synergy as the design goal. No art assets: 3D
placeholder geometry (capsules, boxes, planes), text, and colour only. Public repo (MIT, since 2026-10-17). Working name; rename later.

## Stack (locked, STATE.md D1)
TypeScript everywhere so the simulation is one shared, deterministic package: `sim/` (headless rules engine),
`content/` (classes, talents, gear, dungeon, boss as data + small scripts), `client/` (browser 3D via Three.js — third-person camera,
WASD + mouse-look, placeholder geometry; no engine), `server/` (Node, authoritative tick, later phase), `bots/` (party AI for bots-first play). Tests:
Vitest. No game engine beyond Three.js for rendering, no bundler beyond esbuild.

## Domain map — workers read exactly ONE domain CLAUDE.md; this file is navigation only
- **research/** — cited briefs from the research day; each answers one question and ends with a verdict.
- **design/** — the design bible: BIBLE.md (what the game is), classes, talents, gear, dungeon, boss, controls.
- **sim/** — the rules engine. Read sim/CLAUDE.md (written after the bible).
- **content/**, **client/**, **bots/**, **server/** — added as phases open.

## Invariants
1. Determinism: the sim is a pure function of (seed, command stream); replays must reproduce exactly.
2. Fun is measured, not assumed: every mechanic in the bible cites a game where it worked and says why.
3. Constraint is the design: ≤5 rotational abilities, ≤10 keybinds (rotation + movement + utility), synergy first.
4. Fail closed: a rule the sim cannot resolve deterministically is a bug, never a coin flip.
5. Tiny files, one purpose; tests before code; PRs to main only (protected).
