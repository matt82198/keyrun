"""Scope: is a diff docs-only, so the Game workflow's heavy Windows jobs have no code to run?

Single source of the docs-only decision for .github/workflows/game.yml. The `changes` job runs
this once on ubuntu and writes `code=true` or `code=false` to $GITHUB_OUTPUT. A docs-only PR then
runs game-test and game-e2e as one-line pass-throughs on ubuntu-latest instead of queueing behind
other runs for a windows-latest runner (PR #572 waited 30+ minutes for a pass-through).

Docs-only paths: STATE.md, BUILDLOG.md, docs/**, design/**, research/**, game/lanes/*.md (lane row
files; read only by the TypeScript row-file test in tests.yml), and *.md outside tools/, game/,
bots/, client/, .github/. Everything else is code, as is any path under those five roots
(tools/Keyrun.Playtest/GUIDE.md and CODEOWNERS are read by gates).

Fails closed: an unresolvable base counts as every path changed (code), and an empty diff is code.
Exit 0 with a `code=` line printed, or 2 when the diff cannot be computed (the step fails, the
`changes` job fails, and game-gate fails on its result).

Stdlib only. Usage:
    python tools/ci/docs_scope.py --base <sha> --head <sha> [--output $GITHUB_OUTPUT]
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from game_gate import GateError, changed_paths  # noqa: E402

DOCS_FILES = ("STATE.md", "BUILDLOG.md")
DOCS_PREFIXES = ("docs/", "design/", "research/")
CODE_PREFIXES = ("tools/", "game/", "bots/", "client/", ".github/")
LANE_ROWS = "game/lanes/"


def is_docs_path(path):
    """True when a repo-relative path is documentation no Game job reads."""
    p = path.replace("\\", "/")
    if p in DOCS_FILES or p.startswith(DOCS_PREFIXES):
        return True
    if p.startswith(LANE_ROWS):
        rest = p[len(LANE_ROWS):]
        return "/" not in rest and rest.endswith(".md")
    if p.startswith(CODE_PREFIXES):
        return False
    return p.endswith(".md")


def is_code_change(paths):
    """True unless every path is a docs path. An empty change set is code (fail closed)."""
    paths = [p for p in paths if p]
    if not paths:
        return True
    return not all(is_docs_path(p) for p in paths)


def main(argv=None):
    parser = argparse.ArgumentParser(description="docs-only scope for the Game workflow")
    parser.add_argument("--base", default="")
    parser.add_argument("--head", default="")
    parser.add_argument("--repo", default=".", help="git repo directory")
    parser.add_argument("--output", default="", help="file to append code=<bool> to")
    args = parser.parse_args(argv)
    try:
        paths = changed_paths(args.base, args.head, args.repo)
    except GateError as exc:
        print("docs-scope: ERROR (fail closed): %s" % exc)
        return 2
    code = is_code_change(paths)
    for p in paths:
        print("  %s %s" % ("docs" if is_docs_path(p) else "code", p))
    line = "code=%s" % ("true" if code else "false")
    print("docs-scope: %s (%d path(s))" % (line, len(paths)))
    if args.output:
        with open(args.output, "a", encoding="utf-8", newline="\n") as fh:
            fh.write(line + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
