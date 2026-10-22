# tools/ - domain notes

Read this file before changing anything under tools/.

## tools/godot/
Engine wrapper scripts (godot.ps1 / godot.sh resolve the binary and DOTNET_ROOT) and the SHA512-verified
export-template installer (install-templates.ps1). Changes here are game-gated: a PR touching tools/godot/**
needs a green game-e2e run (see tools/ci/).

Single launch path (2026-10-20 box freeze): `launch-lock.ps1` is the one lock, cap and tree kill for every engine
launch. godot.ps1, screens.ps1 and the multi-launch game/src harnesses dot-source it; godot.sh delegates to godot.ps1 on
Windows. Lock file `%LOCALAPPDATA%\keyrun\godot-launch.lock` (override `KEYRUN_LAUNCH_LOCK_FILE`), stale only when its
owner PID is gone (start time checked against PID reuse), never by age. Busy: godot.ps1 exits 4, screens.ps1 5, a
harness 2. Cap: `KEYRUN_GODOT_TIMEOUT_SEC` (default 300; game-e2e sets 660), then the job object and taskkill /T end the
whole tree, exit 124. Tests: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/launch-lock-test.ps1`
(stub binary only; never launches Godot). Lanes never launch the game; the orchestrator does.

## tools/Keyrun.Playtest/ (+ Keyrun.Playtest.Tests)
Agent playtesters: headless Hollow Vault runs driven by a playtester's plan file, so agents can fail, learn and push
keys (Matt 2026-10-20). GUIDE.md is the playtester's how-to-play guide (embedded, `playtest guide`); it names no enemy
and no enemy ability (GuideTests). PolicyBot does only what the plan says (no plan field = never done) with seeded human
Hands; PlaytestSession plays the ledger's key (game/playtest/ledger.json), requires a plan file, and appends the run to
game/playtest/players/ID.json (PlaytesterMemory: runs, recap lines, lessons that cite a run, SHA-256 receipts of the
summary and plan). KeyLedger: timed +1/+2/+3 (par, 80%, 60%), untimed stays. No policy at all (off-ledger) is the
scripted party, byte-identical to the host bot run (NoPolicyParityTests). HostBackfillRunTests steps HudSceneTest
runs F and G without Godot (game/src/Host/ScriptedWarden.cs and BotCommandSource.cs linked, Backfill.Step every tick)
and holds HudSceneTest's -AbandonSeat default, so a change that moves the Choir clear fails in game-test. Both projects are in game/Keyrun.sln, so
game-test runs them; they read the game/sim, game/content and game/bots public APIs only and never edit them.

## tools/fastcheck.ps1
Per-step lane check: incremental `dotnet build game/Keyrun.sln`, then `dotnet test --no-build` for only the test
projects the path table at the top of the script maps the diff (vs `git merge-base origin/main HEAD`, plus uncommitted
and untracked files) to. Fails closed: an unmapped path, a git failure or an empty diff runs all three projects. Exit
nonzero on any build error or test failure. A speed tool for lanes only; CI game-test still runs the full solution.

## tools/ci/
Stdlib-only CI guards. Tests: `python -m unittest discover -s tools/ci -p "test_*.py"` (fixtures live in tempfile
dirs; no cwd pollution).

- **game_gate.py** - enforcement for escape `keyrun-game-e2e-export-hang`. Any PR whose diff touches `game/**` or
  `tools/godot/**` must have `needs.game-e2e.result == 'success'`. Exit 0 pass, 1 fail, 2 cannot evaluate (fails closed).
  Wired as the `game-gate` job in `.github/workflows/game.yml` (`if: always()`, `needs: [game-test, game-e2e]`) and
  listed as a required status check on `main` via branch protection, so a red, cancelled or still-running game-e2e
  blocks merge of game-path PRs. Escape-reproduction tests live in `test_game_gate.py` (`EscapeReproductionTest`).
  The diff uses `--no-renames` so a move out of `game/` still reports its game-side path (`RenameBypassTest`). An
  empty or all-zero base (first push, `github.event.before` zeros) is treated as all paths changed, so the gate
  requires game-e2e success instead of crashing. A missing head or an unknown sha exits 2 (fail closed).
- **e2e_wait_ratchet.py** - kept as a tool, not wired. It was the enforcement for escape `e2e-window-ping-flake`
  and scanned the `*.e2e.ts` Playwright suite of the TypeScript client, which PR #553 removed. No workflow
  runs it now (`.github/workflows/tests.yml` `e2e` has no suite behind it). Its tests are `test_e2e_wait_ratchet.py`.
- The export/build steps in game.yml carry `timeout-minutes: 12` so a stall fails visibly instead of consuming the
  30-minute job timeout. The root cause (Godot child-process lifetime keeping the step open) is NOT fixed by this
  guard; it only makes the stall fail visibly and blocks merge.
