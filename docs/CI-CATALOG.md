# CI catalog: keyrun recurring reds

A failing gate's message is a lookup key in this file. Find the row whose signature matches the log line, apply the recipe once, and do not retry the gate.

Compiled 2026-10-22 from `gh` run logs of open PRs and main (Game and Tests workflows). Signatures are verbatim first lines. Each row says whether the red is infrastructure (fixable once at the root) or branch-owned (the PR's own change is wrong). Rows marked pending need a decision or a branch change; this sweep does not paper over them.

## Summary

- Infrastructure or gate defects found: none that a single root fix on main would clear. The runner, the row-file gate, the game gate and the fastcheck summary all behave as written. The game gate's `FAIL:` lines that look alarming in a log are self-test output from `tools/ci/test_game_gate.py`; the verdict is the last `game-gate: FAIL: required job(s)` line.
- Main was red on Game (`game-test` and `balance`) at 2026-10-22 07:28Z and 16:44Z, and is green again: Game was SUCCESS on main at 1dd1e867 (2026-10-22 21:30Z). K4 reds are branch-level: a branch's behaviour change moves pinned trajectory outcomes, so the red belongs to the branch that moved them. The K4 tuning-versus-par call still needs Matt's decision, not a gate change.

## Table

| id | category | signature line (verbatim) | root cause | recipe (apply once) | fixed-by | recurrence guard |
|---|---|---|---|---|---|---|
| CI-K1 | ROW-SHAPE (branch-owned) | `AssertionError: <row>.md: expected [ 'line count 13, expected 2' ] to deeply equal []` or `... expected [ 'line 1 is not a lane bullet', ...(1) ] to deeply equal []` | The PR's own `game/lanes/<branch-with-dashes>.md` is not exactly two non-empty lines (lane bullet, then BUILDLOG line). Seen on feat-aoe-trash-kit (13 lines), docs-bible-pivot-stale-lines, test-pack-density-r-packdensity-1, fix-untaken-branch-walkable. | In the PR's own worktree, rewrite that row file with Write to exactly two lines: `- <lane bullet>` then `BUILDLOG: <one line>`. Commit, push. Do not touch BUILDLOG.md. | pending: branch owners (no edit to other PRs in this sweep). | `tests/lanes/row-files.test.ts` (existing). |
| CI-K2 | GAME-GATE cascade | `game-gate: FAIL: required job(s) not 'success': game-test is 'failure'` (also `game-e2e is 'failure'`, `is 'cancelled'`) | Downstream verdict. The gate correctly refuses a PR whose required jobs are not green. Cancelled or missing e2e also blocks by design. | None on the gate. Fix the upstream job (K1, K3, K4, K5, K6, K7) and re-run. Ignore earlier `game-gate: FAIL: game paths changed...` and `ERROR (fail closed): git diff failed: ... deadbeef...` lines: those come from `tools/ci/test_game_gate.py` negative cases, not from the run. | n/a (downstream) | `tools/ci/test_game_gate.py` (existing). |
| CI-K3 | BOT ORACLE property (branch behaviour) | `bots-hv-k2-s7 property slams-are-telegraphed-first FAILED: {"abilityId":"hollowWarden.slam",...,"type":"cast_complete"} has no earlier {"type":"telegraph"}` | A cast_complete for hollowWarden.slam is emitted with no earlier telegraph event for the same ability. Seen on feat-aoe-trash-kit at dae6c2aa. Main be432c95 already changed scripted-cast telegraph ordering (R-BOSS-5), so the branch may be stale. | Merge-forward the PR onto origin/main. If the property still fails, the branch's sim change must draw the telegraph before the cast completes; do not edit the oracle property. | pending: merge-forward check on the branch owner's PR (unverified until re-run). | `game/bots/Keyrun.Bots.Tests/RolesOracleTraceTests.cs` property asserts (existing). |
| CI-K4 | BALANCE / PAR assertions (branch-owned, decision needed) | `Seeded branches (key 2, ...): the party clears it to the boss within par` (Expected Timed, Actual Depleted); `Aggro chase autorun (k2 seed 1): the bots still clear the key, timed within par` (Expected "timed", Actual "depleted"); `Run C careless seat (3@300:pair): ...` (Expected Timed, Actual null); `Kick ledger gap: ... no Choral Mending resolves` ; `Kick reaction window: seeds 1-20 ... seed 6: cast 1448..1528 kicked 62 ticks in, past 60`; `Seed bloom: a Combo lands in the Seed's Death Bloom Window ... no Combo landed` | Seed-specific pinned trajectory outcomes (for example "CLEAVE-ANSWER key 10 seed 7", "Kick reaction window seeds 1-20", "Run K post-run Enters", "Chain pull Hollow Vault") that a branch's behaviour change moves. Main was SUCCESS on Game at 1dd1e867 (2026-10-22 21:30Z), so K4 reds are branch-level, not main-red. Evidence: #642 re-cut (branch fix/bot-filler-variety-v2, 7 K4 names); #660 bisect. | The lane hands back BLOCKED-K4 with the failing names and the delta (expected, actual, seed, key). Never edit an assertion, par or fixture to go green. The tuning-versus-par call is Matt's; record it in STATE.md when made. | pending: Matt's tuning-versus-par call; #642 is the K4 carrier. | The named property tests (existing). Balance job is non-required (fastcheck reports balance red, not a gate failure). |
| CI-K5 | PACK DENSITY R-PACKDENSITY-1 (test-first, content pending) | None on main. No file on origin/main prints an `R-PACKDENSITY-1` violation; the only `R-PACKDENSITY` hits are this file (lines 20, 59) and `tools/Keyrun.Playtest.Tests/BotChainPullTests.cs:133`, which names the density work `R-PACKDENSITY-2`. | Test-first PRs (test/pack-density-*) land the red density rule before the pack placement that satisfies it. The red is the deliverable until the content PR (feat/pack-spread and the pack placement work) lands. | None in CI. Land the pack placement that gives every pack a neighbour within 1.5x its aggro radius, or move the rule with a recorded decision. | pending: content PR (feat/pack-spread, not in this sweep). | None on main. No PackDensity test file exists on origin/main (`git ls-tree -r origin/main` lists none), so the body-pull variant is not on main. |
| CI-K6 | UNTAKEN BRANCH walkable (branch-owned) | `seed 1: 8 of 19 rooms not walkable in this run (walked chain 0>2>...; untaken rooms 1,3,7,...; nav boxes 33)` and `seed 1: move into untaken room 1 clamped to Vec3 {...}` | The run's walkable nav set omits untaken-branch rooms, so the bot clamps moves into them. fix/untaken-branch-walkable is the PR whose job is to fix this; it is still red on its own change. | None in CI. The PR's nav build must include every generated room of both branches for every segment. | pending: PR 614 owner. | `game/bots/Keyrun.Bots.Tests/UntakenBranchWalkableTests.cs` (existing). |
| CI-K7 | E2E SCENE / HUD (trace or check drift) | `HudRunA-SceneTest: FAIL (1)` (also `HudRunC-SceneTest: FAIL (8)`, `HudSceneTest: FAIL (1)`, `scene tests failed (1 of 31): HudRunK-SceneTest exited 1`). The `game-e2e` line `ARENA error ... startHeading must be a finite number` with `HudSceneTest: FAIL (1)` (seen on #295) is K7 too. | The scene's own check fails and prints only a count. The failing check is the last `--- ... ---` or `HUD ...` block before the `<Name>: FAIL` marker. Proven cause (#581, #661): a `*SceneTest.ps1` run in the default 64x64 headless window gives the HUD a 32 px Slack slot, so `HudLayout.SlackSplit` (game/content/Keyrun.Content/Model/HudLayout.cs:168-169) yields a 0 px timer box. | Viewport recipe: pass `--view-size 1280x720` in the scene's Invoke-Arena call (precedent game/src/Host/scenes/HudTooltip-SceneTest.ps1:73-75; the fix is in #661, game/src/Host/scenes/HudRunA-SceneTest.ps1). Otherwise read the failing scene's group in the log: the line before `<Name>: FAIL` names the state. If a sim change is intended, re-record that scene's fixture (needs R-ORACLE-1 scope decision); if not, fix the branch. Do not loosen the scene check. The game-gate line `game paths changed ... but game-e2e result is 'failure'` is the K2 cascade. | pending: decision on fixture re-record (R-ORACLE-1). The harness itself prints the group output on failure; it does not drop it. | Scene step in `.github/workflows/game.yml` (existing) fails the job on any non-PASS. |
| CI-LINEAR-BASE | PR BASE (branch-owned, gate) | `R-LINEAR-1: PRs on <repo> target main only (base "<x>" refused)` | A PR was opened against another branch (a chained PR), so the pr-base-main gate refuses it. | Open the PR against main. A dependency on an unmerged PR is a WAITING-ON <n> hand-back or folds into that PR's lane; never a chained PR. | n/a (gate working as designed). | pr-base-main gate denial (brain hook, existing). |
| CI-LITERAL-PATHS | WORKTREE PATH (lane-side, gate) | `cannot resolve the path given to git worktree add/remove ("$X/...") : write the literal absolute path` | A shell variable path the gate cannot resolve. | Write the literal absolute path. A same-command plain literal assignment (`S=C:/x; git ... $S/wt`) now resolves; anything else (unset, `$(...)`, backtick, `~`, `NAME=$OTHER`, across a pipe or subshell) stays denied. | n/a (gate working as designed). | worktree path gate denial (brain hook, existing). |
| CI-SCENE-LIVE-WEEK | scene test depends on the live affix week | A scene that passed last week fails with no `HUD runend` line while the headless derivation still passes. | A scene run with no `--rotation` takes its affix rotation from the live UTC clock (SimDriver.cs:240-245, AffixWeek.ForRun), while the headless anchors are derived at rotation 0 (HostRunSetup.Build default). | Pin `--rotation 0` in HudSceneCommon (every autorun scene launch); derive anchors and run scenes at the same index. | pending: this lane's uncommitted edit (HudSceneCommon.ps1 and the autorun scene launches). | none yet. |

## Superseded PRs closed under R-STALE-2 (2026-10-22)

- #581 superseded by #661 (K7 harness fix).
- #295 and #327 superseded by e87cade0 (#449).
- #418 premise gone: layout.json replaced by layout/hollow-vault/packs/*.json.
- #362 superseded: ArmorBp pipeline on main, run.json warden 3500.
- #599 superseded: ForcesPlanner (#606, #637) and AutorunPullToHundredForcesTests.
- #660 closed: no green content subset; #643 is the carrier.
- #614 left open as a deliberate K6 receipt.
- #642 left open as the K4 carrier (re-cut preserved on fix/bot-filler-variety-v2).

## Open PR reconciliation (status at compile time, no edits to other PRs)

| PR | head | red checks | catalog ids | does this sweep fix it? |
|---|---|---|---|---|
| 643 | feat/aoe-trash-kit dae6c2aa | Tests (row-files), Game (game-test, game-e2e, game-gate) | K1, K3, K7, K2 | No. Branch: rewrite `game/lanes/feat-aoe-trash-kit.md` to two lines; merge-forward for K3. |
| 642 | fix/filler-least-recently-cast bbd7e8c2 | Game (game-test, game-e2e, game-gate) | K4 (seed bloom, kick window), K2 | No. Game-e2e not classified in this pass. |
| 639 | docs/bible-pivot-stale-lines 32bc0335 | Tests (row-files) | K1 | No. Branch: its row file, line 1 is not a lane bullet. |
| 621 | test/pack-density-body-pull 30371529 | Game (game-test, game-gate) | K5, K2 | No. Test-first; content PR owns the fix. |
| 617 | test/pack-density-r-packdensity-1 3acea94f | Tests (row-files), Game (game-test, game-gate) | K1, K5, K2 | No. Branch: row file; content PR owns K5. |
| 614 | fix/untaken-branch-walkable 50c346ac | Tests (row-files), Game (game-test, balance, game-gate) | K1, K6, K4 (fork parity balance), K2 | No. Branch: row file and nav set. |
| 599 | fix/bots-pull-to-100-forces a9bf39a2 | Game (game-test, game-e2e, game-gate) | K4 (kick ledger gap), K2 | No. Decision on K4 needed. |
| 581 | fix/hud-timer-label-pileup f7e30765 | Game (game-e2e, game-gate) | K7, K2 | No. Branch: scene check. |
| 576 | fix/finish-needs-100-forces 79de5bac | Game (game-test, game-e2e, game-gate) | K4 (aggro chase autorun), K7 | No. |
| 418 | feat/pack-spread 7cca4080 | Game (game-test, game-e2e, game-gate), run date 2026-10-21 | K7 (HudSceneTest FAIL 2 on 10-21), K4 possible | No. Stale run; re-run before classifying. |
| 362 | feat/tank-survivability ad429611 | Game (game-test, game-e2e, game-gate), run date 2026-10-21 | K4 (Run C careless seat) | No. Stale run. |
| 327 | feat/cross-class-kick-order da3efc43 | Game (game-e2e, game-gate), run date 2026-10-20 | K7 (HudSceneTest FAIL 1) | No. Stale run. |
| 295 | fix/bot-kick-timing aceb6666 | Game (game-e2e, game-gate), run date 2026-10-20 | K7 (HudSceneTest FAIL 1) | No. Stale run. |

## Decisions needed (Matt)

1. CI-K4: which side moves when a bot or forces change breaks a par or kick assertion on main (sim tuning versus bible par). Branches that move these outcomes stay red until this is decided.
2. CI-K7 and K3: whether intended recorded-input or oracle changes may re-record fixtures (R-ORACLE-1 scope).
3. CI-K5: confirm pack placement is the next content task, or move the R-PACKDENSITY-1 rule with a recorded decision.

## Guards that already exist

- `tests/lanes/row-files.test.ts`: row-file shape (K1).
- `tools/ci/test_game_gate.py`: game-gate verdicts, including the fail-closed and deadbeef negative cases (K2).
- `game/bots/Keyrun.Bots.Tests/*` property and par tests (K3, K4, K6).
- Scene step in `.github/workflows/game.yml` (K7).
