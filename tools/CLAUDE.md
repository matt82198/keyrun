# tools/ - domain notes

Read this file before changing anything under tools/.

## tools/godot/
Engine wrapper scripts (godot.ps1 / godot.sh resolve the binary and DOTNET_ROOT) and the SHA512-verified
export-template installer (install-templates.ps1). Changes here are game-gated: a PR touching tools/godot/**
needs a green game-e2e run (see tools/ci/).

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
- **e2e_wait_ratchet.py** - enforcement for escape `e2e-window-ping-flake`. Counts `sleep(` and `waitForTimeout(` per
  `tests/client/e2e/*.e2e.ts` and compares each count to `e2e-wait-baseline.json`. A count may not rise and a new spec
  may not add a wall-clock wait; the fix is `waitForFunction` on exposed state with a bounded timeout. Exit 0 pass,
  1 fail, 2 cannot evaluate (missing or invalid baseline, `total` not equal to the sum of `counts`, or no specs found;
  fails closed). Wired as a step in the `e2e` job of `.github/workflows/tests.yml` before `npm run test:e2e`.
  Escape-reproduction tests live in `test_e2e_wait_ratchet.py` (`EscapeReproductionTest`). The root cause is
  unconfirmed: the 20-tick PingRateLimiter can silently drop a G press when a slow runner advances fewer sim ticks
  than the wall-clock sleep allows, and this guard does not fix that. Lowering the baseline is the follow-up lane's
  job, scoped to `tests/client/e2e/**`.
- The export/build steps in game.yml carry `timeout-minutes: 12` so a stall fails visibly instead of consuming the
  30-minute job timeout. The root cause (Godot child-process lifetime keeping the step open) is NOT fixed by this
  guard; it only makes the stall fail visibly and blocks merge.
