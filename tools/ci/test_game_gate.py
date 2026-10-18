"""Tests for tools/ci/game_gate.py. Every repo fixture lives in a tempfile dir."""
import os
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import game_gate  # noqa: E402

GIT_ID = ["-c", "user.name=gate-test", "-c", "user.email=gate-test@example.invalid",
          "-c", "commit.gpgsign=false", "-c", "init.defaultBranch=main"]


def _git(repo, *args):
    proc = subprocess.run(["git", "-C", repo] + GIT_ID + list(args),
                          capture_output=True, text=True, check=True)
    return proc.stdout.strip()


def _write(repo, rel, text):
    full = os.path.join(repo, *rel.split("/"))
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)


def _pr_fixture(tmp, feature_files):
    """Base commit has game/src/Sim.cs and server/index.ts; the feature branch
    writes feature_files on top of it. Returns (repo, base_sha, head_sha)."""
    repo = os.path.join(tmp, "repo")
    os.makedirs(repo)
    _git(repo, "init", "-q")
    _write(repo, "game/src/Sim.cs", "class Sim {}\n")
    _write(repo, "server/index.ts", "export {};\n")
    _git(repo, "add", "-A")
    _git(repo, "commit", "-q", "-m", "base")
    base = _git(repo, "rev-parse", "HEAD")
    _git(repo, "checkout", "-q", "-b", "feature")
    for rel, text in feature_files.items():
        _write(repo, rel, text)
    _git(repo, "add", "-A")
    _git(repo, "commit", "-q", "--allow-empty", "-m", "feature")
    head = _git(repo, "rev-parse", "HEAD")
    return repo, base, head


class EscapeReproductionTest(unittest.TestCase):
    """Reproduces keyrun-game-e2e-export-hang: PR #117 touched game/ and
    Keyrun.Content while game-e2e hung and was cancelled at the job timeout."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.repo, self.base, self.head = _pr_fixture(self._tmp.name, {
            "game/content/Keyrun.Content/Items.cs": "class Items {}\n",
            "game/src/Sim.cs": "class Sim { int x; }\n",
        })

    def tearDown(self):
        self._tmp.cleanup()

    def test_cancelled_game_e2e_is_flagged(self):
        paths = game_gate.changed_paths(self.base, self.head, self.repo)
        self.assertIn("game/src/Sim.cs", paths)
        ok, reason = game_gate.evaluate(paths, "cancelled")
        self.assertFalse(ok)
        self.assertIn("cancelled", reason)

    def test_failed_and_missing_game_e2e_are_flagged(self):
        paths = game_gate.changed_paths(self.base, self.head, self.repo)
        self.assertFalse(game_gate.evaluate(paths, "failure")[0])
        self.assertFalse(game_gate.evaluate(paths, "")[0])
        self.assertFalse(game_gate.evaluate(paths, "skipped")[0])

    def test_cli_exit_code_is_nonzero_for_escape_state(self):
        rc = game_gate.main(["--base", self.base, "--head", self.head,
                             "--e2e-result", "cancelled", "--repo", self.repo])
        self.assertEqual(rc, 1)

    def test_success_passes(self):
        rc = game_gate.main(["--base", self.base, "--head", self.head,
                             "--e2e-result", "success", "--repo", self.repo])
        self.assertEqual(rc, 0)


class CleanStateTest(unittest.TestCase):
    """No false positives: non-game PRs must not need game-e2e."""

    def test_non_game_pr_passes_without_e2e(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, base, head = _pr_fixture(tmp, {"server/index.ts": "export const a = 1;\n"})
            paths = game_gate.changed_paths(base, head, repo)
            self.assertEqual(paths, ["server/index.ts"])
            ok, _ = game_gate.evaluate(paths, "cancelled")
            self.assertTrue(ok)
            self.assertEqual(game_gate.main(["--base", base, "--head", head,
                                             "--e2e-result", "", "--repo", repo]), 0)

    def test_game_pr_with_green_e2e_passes(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, base, head = _pr_fixture(tmp, {"game/src/Sim.cs": "class Sim { int y; }\n"})
            paths = game_gate.changed_paths(base, head, repo)
            self.assertTrue(game_gate.evaluate(paths, "success")[0])

    def test_empty_diff_passes(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, base, _ = _pr_fixture(tmp, {})
            self.assertEqual(game_gate.changed_paths(base, base, repo), [])
            self.assertTrue(game_gate.evaluate([], "")[0])

    def test_prefix_lookalikes_are_not_game_paths(self):
        for path in ("games/notes.md", "game-notes.md", "gamelist/x.txt",
                     "docs/game/README.md", "tools/godotx/a.ps1", "tools/ci/game_gate.py"):
            self.assertFalse(game_gate.is_game_path(path), path)

    def test_game_and_godot_tool_paths_are_game_paths(self):
        for path in ("game/src/Sim.cs", "tools/godot/godot.ps1", "tools\\godot\\godot.ps1"):
            self.assertTrue(game_gate.is_game_path(path), path)


class FailClosedTest(unittest.TestCase):
    def test_zero_or_missing_base_is_error_not_pass(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, _, head = _pr_fixture(tmp, {"game/src/Sim.cs": "class Sim { int z; }\n"})
            self.assertEqual(game_gate.main(["--base", game_gate.ZERO_SHA, "--head", head,
                                             "--e2e-result", "success", "--repo", repo]), 2)
            self.assertEqual(game_gate.main(["--base", "", "--head", head,
                                             "--e2e-result", "success", "--repo", repo]), 2)

    def test_unknown_sha_is_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, _, head = _pr_fixture(tmp, {"game/src/Sim.cs": "class Sim { int w; }\n"})
            self.assertEqual(game_gate.main(["--base", "deadbeef" * 5, "--head", head,
                                             "--e2e-result", "success", "--repo", repo]), 2)


if __name__ == "__main__":
    unittest.main()
