"""Guard: a PR that touches game paths must have a successful game-e2e run.

Enforcement for escape keyrun-game-e2e-export-hang: game-e2e was advisory, so
PR #117 (game/ + Keyrun.Content) merged while its export hung and was
cancelled at the 30-minute job timeout. The game-gate job in
.github/workflows/game.yml runs this module with needs.game-e2e.result and
fails unless that result is 'success' whenever the diff touches game/** or
tools/godot/**. The gate is also a required check on main, so a red,
cancelled or still-running game-e2e blocks merge of such PRs.

Exit codes: 0 = gate passes, 1 = gate fails (game paths without a green
game-e2e), 2 = gate cannot evaluate (fails closed).

Stdlib only. Usage:
    python tools/ci/game_gate.py --base <sha> --head <sha> --e2e-result <result>
"""
import argparse
import subprocess
import sys

GAME_PREFIXES = ("game/", "tools/godot/")
ZERO_SHA = "0" * 40
# Stands in for "every path changed" when the base sha cannot be resolved. It sits under
# game/ so evaluate() treats the change as game-touching and requires game-e2e success.
ALL_PATHS_UNRESOLVED_BASE = "game/<all paths: base sha unresolvable, assumed changed>"


class GateError(Exception):
    """The gate could not evaluate; callers must treat this as a failure."""


def is_game_path(path):
    """True when a repo-relative path lies under a game-gated prefix."""
    return path.replace("\\", "/").startswith(GAME_PREFIXES)


def evaluate(changed_paths, e2e_result):
    """Return (ok, reason) for a diff's changed paths and the game-e2e result."""
    game_paths = sorted(p for p in changed_paths if is_game_path(p))
    if not game_paths:
        return True, "no game paths changed; game-e2e not required"
    if e2e_result == "success":
        return True, "game paths changed (%d); game-e2e success" % len(game_paths)
    return False, (
        "game paths changed (%d, e.g. %s) but game-e2e result is %r; "
        "it must be 'success' (cancelled, failed, skipped or missing blocks merge)"
        % (len(game_paths), game_paths[0], e2e_result)
    )


def changed_paths(base, head, repo_dir):
    """List repo-relative paths in base...head. Raises GateError on any failure.

    An unresolvable base (empty, or all zeros as on a first push where
    github.event.before is zeros) yields a single sentinel game path, meaning
    "every path is assumed changed": the gate then requires game-e2e success.
    """
    if not head:
        raise GateError("head sha missing; cannot compute the diff")
    if not base or base == ZERO_SHA:
        return [ALL_PATHS_UNRESOLVED_BASE]
    # --no-renames: with rename detection a move out of game/ lists only the new path,
    # which hid the game/ side of the change (known bypass, see RenameBypassTest).
    cmd = ["git", "-C", repo_dir, "diff", "--no-renames", "--name-only", "-z",
           "%s...%s" % (base, head)]
    try:
        proc = subprocess.run(cmd, capture_output=True, check=False)
    except OSError as exc:
        raise GateError("git not runnable: %s" % exc)
    if proc.returncode != 0:
        raise GateError("git diff failed: %s" % proc.stderr.decode("utf-8", "replace").strip())
    raw = proc.stdout.decode("utf-8", "replace")
    return [p for p in raw.split("\0") if p]


def main(argv=None):
    parser = argparse.ArgumentParser(description="game-e2e required for game-path PRs")
    parser.add_argument("--base", default="")
    parser.add_argument("--head", default="")
    parser.add_argument("--e2e-result", default="", dest="e2e_result")
    parser.add_argument("--repo", default=".", help="git repo directory")
    args = parser.parse_args(argv)
    try:
        paths = changed_paths(args.base, args.head, args.repo)
    except GateError as exc:
        print("game-gate: ERROR (fail closed): %s" % exc)
        return 2
    ok, reason = evaluate(paths, args.e2e_result)
    print("game-gate: %s: %s" % ("PASS" if ok else "FAIL", reason))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
