# game/lanes - one row file per lane

A lane never edits the game/CLAUDE.md lane list or BUILDLOG.md in its PR, because parallel lanes collide on those two files. Each lane instead writes `game/lanes/<branch-with-slashes-as-dashes>.md`, for example `game/lanes/chore-lane-row-files.md` for branch `chore/lane-row-files`.

A row file is exactly two non-empty lines:

1. One lane bullet in the same shape as the bullets in game/CLAUDE.md. Exemplar, the last bullet of that list as it stands (verbatim):

- GAME-YML-SCENE-TESTS game.yml runs the three scene tests (CI wiring only; no sim, content, host or view change): `.github/workflows/game.yml` adds RunEndBonusSceneTest (`game/src/Hud/`), GateViewSceneTest and GateCutawaySceneTest (`game/src/Host/`) steps right after the Screen scene test, in the same pwsh shape, so a regression in the end-of-run panel or the room gate view turns a PR red instead of relying on the lane local run.

2. `BUILDLOG: ` followed by the lane's one BUILDLOG sentence.

After the squash, the orchestrator dispatches a haiku fold lane. In one PR it appends each row file's bullet to the END of the lane list in game/CLAUDE.md, appends each BUILDLOG line to BUILDLOG.md with the merge sha, and `git rm`s the consumed row file. No data is lost: the content moves into the lane list and BUILDLOG.

Gate: `npx vitest run tests/lanes` (tests/lanes/row-files.test.ts).
