"""Tests for tools/ci/docs_scope.py. Every repo fixture lives in a tempfile dir."""
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import docs_scope  # noqa: E402
import game_gate  # noqa: E402
from test_game_gate import _git, _pr_fixture, _write  # noqa: E402


def _scope(repo, base, head, out_file):
    rc = docs_scope.main(["--base", base, "--head", head, "--repo", repo, "--output", out_file])
    with open(out_file, encoding="utf-8") as fh:
        return rc, fh.read()


class DocsOnlyTest(unittest.TestCase):
    """PR #572 (README + STATE only) must read as docs-only: code=false."""

    def test_readme_and_state_is_docs_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, base, head = _pr_fixture(tmp, {"README.md": "r\n", "STATE.md": "s\n"})
            rc, out = _scope(repo, base, head, os.path.join(tmp, "out"))
            self.assertEqual((rc, out), (0, "code=false\n"))

    def test_allowlisted_docs_paths(self):
        for path in ("STATE.md", "BUILDLOG.md", "README.md", "docs/x.png", "design/BIBLE.md",
                     "research/brief.md", "research/fig.svg", "game/lanes/ci-x.md", "notes/a.md"):
            self.assertTrue(docs_scope.is_docs_path(path), path)


class CodeTest(unittest.TestCase):
    """Anything outside the allowlist, or any path under a code root, is code: code=true."""

    def test_code_paths(self):
        for path in ("game/src/Sim.cs", "game/CLAUDE.md", "game/lanes/sub/x.md", "game/lanes/x.txt",
                     "tools/Keyrun.Playtest/GUIDE.md", ".github/CODEOWNERS", ".github/workflows/game.yml",
                     "bots/README.md", "client/a.md", "server/index.ts", "package.json"):
            self.assertFalse(docs_scope.is_docs_path(path), path)

    def test_mixed_pr_is_code(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, base, head = _pr_fixture(tmp, {"README.md": "r\n", "game/src/Sim.cs": "class Sim { int z; }\n"})
            rc, out = _scope(repo, base, head, os.path.join(tmp, "out"))
            self.assertEqual((rc, out), (0, "code=true\n"))

    def test_code_file_renamed_to_md_is_code(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, base, _ = _pr_fixture(tmp, {})
            _git(repo, "mv", "server/index.ts", "server/index.md")
            _git(repo, "commit", "-q", "-m", "rename")
            head = _git(repo, "rev-parse", "HEAD")
            rc, out = _scope(repo, base, head, os.path.join(tmp, "out"))
            self.assertEqual((rc, out), (0, "code=true\n"))


class FailClosedTest(unittest.TestCase):
    def test_empty_diff_is_code(self):
        self.assertTrue(docs_scope.is_code_change([]))

    def test_unresolvable_base_is_code(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, _, head = _pr_fixture(tmp, {"README.md": "r\n"})
            for base in ("", game_gate.ZERO_SHA):
                rc, out = _scope(repo, base, head, os.path.join(tmp, "out-%d" % len(base)))
                self.assertEqual((rc, out), (0, "code=true\n"), base)

    def test_unknown_sha_errors_and_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, _, head = _pr_fixture(tmp, {"README.md": "r\n"})
            out_file = os.path.join(tmp, "out")
            _write(tmp, "out", "")
            rc, out = _scope(repo, "deadbeef" * 5, head, out_file)
            self.assertEqual((rc, out), (2, ""))


if __name__ == "__main__":
    unittest.main()
