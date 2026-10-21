# Keyrun — Project CLAUDE.md

**What**: a proof-of-concept co-op dungeon game that isolates the Mythic+ loop from WoW — queue up, run a timed
dungeon with a small party, kill a boss, come back to a simple character screen and get stronger — with the bloat
cut: 4–5 rotational abilities per class, 10 keybinds total, class synergy as the design goal. Art is CC0 asset packs only (D17; game/assets/ATTRIBUTION.md), no custom art. Public repo (MIT, since 2026-10-17). Working name; rename later.

## Stack (SUPERSEDED by D12 in STATE.md, 2026-10-18)
The engine is Godot 4.7.2 .NET with C# throughout, in `game/` (see game/CLAUDE.md). The TypeScript below is the
browser first-paint client: frozen at main 6e1548d; its oracle fixtures are recordings of the sim, regenerated when a sim
rule changes, not an independent parity check (STATE.md D28), until phase 4A deletes it.
Historical record of the old stack (STATE.md D1, now superseded):
TypeScript everywhere so the simulation is one shared, deterministic package: `sim/` (headless rules engine),
`content/` (classes, talents, gear, dungeon, boss as data + small scripts), `client/` (browser 3D via Three.js — third-person camera,
WASD + mouse-look, placeholder geometry; no engine), `server/` (Node, authoritative tick, later phase), `bots/` (party AI for bots-first play). Tests:
Vitest. No game engine beyond Three.js for rendering, no bundler beyond esbuild.

## Domain map — workers read exactly ONE domain CLAUDE.md; this file is navigation only
- **research/** — cited briefs from the research day; each answers one question and ends with a verdict.
- **design/** — the design bible: BIBLE.md (what the game is), classes, talents, gear, dungeon, boss, controls.
- **design/PLAYTESTERS.md** — agent playtesters that start without the answers, fail first, learn through per-playtester memory with receipts, and drive bot policies via tools/Keyrun.Playtest.
- **game/** - the Godot 4.7.2 .NET desktop game (D12, ENGINE-PLAN section 4). Read game/CLAUDE.md first.
  - **game/sim/** - Keyrun.Sim: pure net8.0 integer rules engine (no Godot reference), Keyrun.Sim.Tests (xUnit),
    oracle fixtures under Fixtures/oracle/. Parity contract: game/sim/ORACLE.md.
  - **game/content/** - Keyrun.Content: JSON loader, validator and sim adapter (pure net8.0). The loader is PR #111
    (open at the time of writing); check main before relying on it.
  - **game/src/**, **game/scenes/** - Godot views, HUD, input and the in-process sim host. Godot types live only here.
  - **game/bots/** - Keyrun.Bots, party AI, pure net8.0. Added in phase 3 (ENGINE-PLAN section 5).
- **tools/godot/** - engine wrapper scripts (godot.ps1 / godot.sh resolve the binary and DOTNET_ROOT) and the
  export-template installer (install-templates.ps1, SHA512-verified).
- **.github/workflows/game.yml** - `game-test` (dotnet test game/Keyrun.sln) and `game-e2e` (headless smoke and
  export). Both are non-required until phase 4A renames them `test` and `e2e`; tests.yml stays the required gate.
- **sim/** - the TypeScript rules engine. FROZEN (STATE.md D12): no feature work; it generates oracle traces until
  phase 4A deletes it. Read sim/CLAUDE.md if it exists (written after the bible).
- **content/data/**, **client/**, **bots/**, **server/** (TypeScript) - frozen with sim/. content/data/*.json is the
  content the C# loader embeds (one csproj glob) and stays verbatim through phase 4A.

## Invariants
1. Determinism: the sim is a pure function of (seed, command stream); replays must reproduce exactly.
2. Fun is measured, not assumed: every mechanic in the bible cites a game where it worked and says why.
3. Constraint is the design: ≤5 rotational abilities, ≤10 keybinds (rotation + movement + utility), synergy first.
4. Fail closed: a rule the sim cannot resolve deterministically is a bug, never a coin flip.
5. Tiny files, one purpose; tests before code; PRs to main only (protected).
- **tests/client/e2e/** - the five headless-browser `*.e2e.ts` specs. Read tests/client/e2e/CLAUDE.md first (run one spec, flake-fix proof bar).
