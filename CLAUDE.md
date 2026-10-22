# Keyrun — Project CLAUDE.md

**What**: a proof-of-concept co-op dungeon game that isolates the Mythic+ loop from WoW — queue up, run a timed
dungeon with a small party, kill a boss, come back to a simple character screen and get stronger — with the bloat
cut: 4–5 rotational abilities per class, 10 keybinds total, class synergy as the design goal. Art is CC0 asset packs only (D17; game/assets/ATTRIBUTION.md), no custom art. Public repo (MIT, since 2026-10-17). Working name; rename later.

## Stack (D12 in STATE.md, 2026-10-18)
The engine is Godot 4.7.2 .NET with C# throughout, in `game/` (see game/CLAUDE.md). The TypeScript tree (the browser
client, the rules engine, the bots, the server and the Vitest suites) was removed in PR #553 (merge 07e8bdd2). What remains
of it is `content/data/` (the frozen oracle root, see content/CLAUDE.md) and `tests/lanes/` (the lane row-file gate).

## Domain map — workers read exactly ONE domain CLAUDE.md; this file is navigation only
- **research/** — cited briefs from the research day; each answers one question and ends with a verdict.
- **design/** — the design bible: BIBLE.md (what the game is), classes, talents, gear, dungeon, boss, controls.
- **design/PLAYTESTERS.md** — agent playtesters that start without the answers, fail first, learn through per-playtester memory with receipts, and drive bot policies via tools/Keyrun.Playtest.
- **game/** - the Godot 4.7.2 .NET desktop game (D12, ENGINE-PLAN section 4). Read game/CLAUDE.md first.
  - **game/sim/** - Keyrun.Sim: pure net8.0 integer rules engine (no Godot reference), Keyrun.Sim.Tests (xUnit),
    oracle fixtures under Fixtures/oracle/. Parity contract: game/sim/ORACLE.md.
  - **game/content/** - Keyrun.Content: JSON loader, validator and sim adapter (pure net8.0). The JSON loader,
    validator and render-only models are on main (PR #111 merged; game/content/Keyrun.Content/ lists the files).
  - **game/src/**, **game/scenes/** - Godot views, HUD, input and the in-process sim host. Godot types live only here.
  - **game/bots/** - Keyrun.Bots, party AI, pure net8.0. Added in phase 3 (ENGINE-PLAN section 5).
- **tools/godot/** - engine wrapper scripts (godot.ps1 / godot.sh resolve the binary and DOTNET_ROOT) and the
  export-template installer (install-templates.ps1, SHA512-verified).
  Fail closed (preflight.ps1, tests screens-preflight-test.ps1 + launch-lock-test.ps1 H + import-cache-test.ps1): exit 6 = no dotnet / missing or stale game assembly / import cache missing or import failed, nothing launched (godot.ps1 and screens.ps1); exit 7 = screens.ps1 run log never written (boot-scene-only run); exit 9 = screens.ps1 run had an actor on the capsule fallback; exit 10 = screens.ps1 model gate not evaluated (run log stayed locked; frames and mp4 kept, test screens-log-lock-test.ps1 K3).
  Every fresh keyrun worktree starts with `powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/setup-worktree.ps1` (Debug build, then the one-time Godot import when the cache is missing; prints `SETUP done build=ok import=<ran|cached> files=<n>`).
- **.github/workflows/game.yml** - owns the required checks `game-test` (dotnet test game/Keyrun.sln), `game-e2e`
  (headless smoke and export) and `game-gate`; `balance` is non-required. **.github/workflows/tests.yml** owns the
  required checks `test` (`npx vitest run tests/lanes`) and `e2e`. The required check `e2e` currently has no suite behind
  it: its one step fails only if a TypeScript e2e suite directory returns, and otherwise it passes.
- **content/data/** - the frozen oracle root: the JSON game content that the C# loader embeds (one csproj glob,
  game/content/Keyrun.Content/Keyrun.Content.csproj). It stays verbatim. See content/CLAUDE.md.

## Invariants
1. Determinism: the sim is a pure function of (seed, command stream); replays must reproduce exactly.
2. Fun is measured, not assumed: every mechanic in the bible cites a game where it worked and says why.
3. Constraint is the design: ≤5 rotational abilities, ≤10 keybinds (rotation + movement + utility), synergy first.
4. Fail closed: a rule the sim cannot resolve deterministically is a bug, never a coin flip.
5. Tiny files, one purpose; tests before code; PRs to main only (protected).
- **tests/lanes/** - the lane row-file gate (`npx vitest run tests/lanes`, `row-files.test.ts`), the only Vitest suite left.
- **tests/sim/golden/** - frozen golden replay fixtures from the removed TypeScript sim. See tests/sim/golden/README.md.
