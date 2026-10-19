"""Ratchet: wall-clock waits in e2e specs may only go down.

Escape e2e-window-ping-flake: tests/client/e2e/window-ping.e2e.ts paces its ping presses
with sleep(1_100) (wall clock), but the client's PingRateLimiter is tick-based (20 sim
ticks). On a slow runner the sim advances fewer ticks in that wall time, derivePingEvent
silently returns null, and the single 3 s waitForFunction has no retry, so the press is
lost with no observable signal. This guard counts sleep( and waitForTimeout( calls per
e2e spec and compares each count to tools/ci/e2e-wait-baseline.json. A file's count may
not rise, and a new spec may not add one. The fix is to wait on exposed state
(waitForFunction) with a bounded timeout instead.

Exit codes: 0 = ratchet holds, 1 = a count rose or a new spec adds a wait,
2 = cannot evaluate (baseline missing or invalid, or no specs found; fails closed).

Stdlib only. Usage:
    python tools/ci/e2e_wait_ratchet.py [--root <repo root>] [--baseline <json>]
"""
import argparse
import json
import re
import sys
from pathlib import Path

WAIT_PATTERN = re.compile(r"\b(?:sleep|waitForTimeout)\s*\(")
SPEC_GLOB = "tests/client/e2e/*.e2e.ts"
DEFAULT_BASELINE = Path(__file__).resolve().with_name("e2e-wait-baseline.json")
DEFAULT_ROOT = Path(__file__).resolve().parents[2]


class RatchetError(Exception):
    """The baseline or the spec set cannot be evaluated; callers must fail closed."""


def count_waits(text):
    """Number of wall-clock wait calls in one spec's source text."""
    return len(WAIT_PATTERN.findall(text))


def load_baseline(path):
    """Return {relative spec path: allowed wait count} or raise RatchetError."""
    try:
        raw = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, ValueError) as exc:
        raise RatchetError(f"cannot read baseline {path}: {exc}") from exc
    if not isinstance(raw, dict) or not isinstance(raw.get("counts"), dict):
        raise RatchetError(f"baseline {path} needs a 'counts' object")
    counts = {}
    for name, value in raw["counts"].items():
        if not isinstance(name, str) or isinstance(value, bool) or not isinstance(value, int) or value < 0:
            raise RatchetError(f"invalid baseline entry {name!r}: {value!r}")
        counts[name] = value
    total = raw.get("total")
    if isinstance(total, bool) or not isinstance(total, int) or total != sum(counts.values()):
        raise RatchetError(f"baseline 'total' must equal the sum of 'counts' ({sum(counts.values())})")
    return counts


def scan(root):
    """Return {repo-relative posix path: wait count} for every e2e spec under root."""
    root = Path(root)
    counts = {}
    for spec in sorted(root.glob(SPEC_GLOB)):
        counts[spec.relative_to(root).as_posix()] = count_waits(spec.read_text(encoding="utf-8"))
    if not counts:
        raise RatchetError(f"no e2e specs matched {SPEC_GLOB} under {root}")
    return counts


def compare(baseline, current):
    """Return (violations, notes) for a current scan against the baseline."""
    violations = []
    notes = []
    for name, now in sorted(current.items()):
        before = baseline.get(name, 0)
        if now > before:
            violations.append(f"{name}: {before} -> {now} wall-clock waits (sleep/waitForTimeout)")
        elif now < before:
            notes.append(f"{name}: {before} -> {now}; lower the baseline entry to lock in the gain")
    for name in sorted(set(baseline) - set(current)):
        notes.append(f"{name}: in baseline but not on disk; remove the entry")
    return violations, notes


def main(argv=None):
    parser = argparse.ArgumentParser(description="Ratchet wall-clock waits in tests/client/e2e specs.")
    parser.add_argument("--root", default=str(DEFAULT_ROOT), help="repository root (default: this checkout)")
    parser.add_argument("--baseline", default=str(DEFAULT_BASELINE), help="baseline JSON path")
    args = parser.parse_args(argv)
    try:
        baseline = load_baseline(args.baseline)
        current = scan(args.root)
    except (RatchetError, OSError) as exc:
        print(f"e2e-wait-ratchet: cannot evaluate: {exc}", file=sys.stderr)
        return 2
    violations, notes = compare(baseline, current)
    for note in notes:
        print(f"e2e-wait-ratchet: note: {note}")
    if violations:
        for violation in violations:
            print(f"e2e-wait-ratchet: FAIL: {violation}", file=sys.stderr)
        print(
            "e2e-wait-ratchet: wait on exposed state with waitForFunction and a bounded timeout instead of a "
            "wall-clock sleep. Do not raise the baseline.",
            file=sys.stderr,
        )
        return 1
    print(
        f"e2e-wait-ratchet: OK ({sum(current.values())} wall-clock waits across {len(current)} specs; "
        f"baseline {sum(baseline.values())})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
