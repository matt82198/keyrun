"""Ratchet: wall-clock waits in e2e test code may only go down.

Escape e2e-window-ping-flake: tests/client/e2e/window-ping.e2e.ts paced its ping presses
with sleep(1_100) (wall clock), but the client's PingRateLimiter is tick-based (20 sim
ticks). On a slow runner the sim advances fewer ticks in that wall time, derivePingEvent
silently returns null, and the single 3 s waitForFunction had no retry, so the press was
lost with no observable signal.

This guard scans every source file under tests/client/e2e/ (nested directories included)
and counts EXECUTED wall-clock wait calls per file:
  - sleep(, waitForTimeout(, delay(, pause(, wait(, and setTimeout( in any form
    (bare, namespaced such as timers.setTimeout(, or new Promise(... setTimeout(...));
  - any alias of setTimeout (import { setTimeout as x }, const x = setTimeout) called as x(;
  - any local helper whose body waits on the clock (a wait-named function, or a body with
    no browser call that waits), called as helper(.
Comments and string literals are blanked before counting, so a comment never counts.

Each file's count is compared to tools/ci/e2e-wait-baseline.json. A count may not rise,
and a new file may not add one. The fix is to wait on exposed state (waitForFunction)
with a bounded timeout instead.

Exit codes: 0 = ratchet holds, 1 = a count rose or a new file adds a wait,
2 = cannot evaluate (baseline missing or invalid, or no e2e specs found; fails closed).

Stdlib only. Usage:
    python tools/ci/e2e_wait_ratchet.py [--root <repo root>] [--baseline <json>]
"""
import argparse
import json
import re
import sys
from pathlib import Path

E2E_DIR = "tests/client/e2e"
SPEC_GLOB = "tests/client/e2e/**/*.e2e.ts"
SOURCE_SUFFIXES = (".ts", ".tsx", ".mts", ".cts", ".js", ".mjs", ".cjs")
DEFAULT_BASELINE = Path(__file__).resolve().with_name("e2e-wait-baseline.json")
DEFAULT_ROOT = Path(__file__).resolve().parents[2]

# Names that are a wall-clock wait when called, whatever module they came from.
WAIT_NAMES = frozenset({"setTimeout", "waitForTimeout", "sleep", "delay", "pause", "wait"})
# A local helper whose name says it waits is a wait helper even when its body is not visible here.
WAIT_HELPER_NAME = re.compile(r"sleep|delay|pause|wait", re.IGNORECASE)
# A body that touches the browser is a browser step, not a pure wall-clock helper.
BROWSER_CALL = re.compile(r"\b(?:page|browser|context|locator|keyboard|mouse|evaluate|expect|waitForFunction)\b")

ALIAS_IMPORT = re.compile(r"\bsetTimeout\s+as\s+([A-Za-z_$][\w$]*)")
ALIAS_ASSIGN = re.compile(r"\b(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=\s*(?:[\w$]+\.)*setTimeout\b")
FUNC_DECL = re.compile(r"\bfunction\s+([A-Za-z_$][\w$]*)\s*\([^)]*\)\s*\{")
ARROW_DECL = re.compile(
    r"\b(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=\s*(?:async\s+)?(?:\([^)]*\)|[A-Za-z_$][\w$]*)\s*=>\s*"
)
FUNCTION_KEYWORD_BEFORE = re.compile(r"\bfunction\s+$")


class RatchetError(Exception):
    """The baseline or the spec set cannot be evaluated; callers must fail closed."""


def strip_comments_and_strings(text):
    """Return text with comments and string/template literal bodies blanked to spaces.

    Newlines survive so the shape of the file is kept. Code inside a template literal's
    ${...} expression is still code and is kept, so a wait hidden in an interpolation counts.
    """
    out = []
    mode = "code"
    quote = ""
    expr_braces = []  # one open-brace depth per ${ ... } currently open inside a template
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if mode == "code":
            if c == "/" and nxt == "/":
                mode = "line"
                out.append("  ")
                i += 2
            elif c == "/" and nxt == "*":
                mode = "block"
                out.append("  ")
                i += 2
            elif c in "\"'":
                mode = "str"
                quote = c
                out.append(" ")
                i += 1
            elif c == "`":
                mode = "tpl"
                out.append(" ")
                i += 1
            elif c == "{" and expr_braces:
                expr_braces[-1] += 1
                out.append(c)
                i += 1
            elif c == "}" and expr_braces:
                if expr_braces[-1] == 0:
                    expr_braces.pop()
                    mode = "tpl"
                    out.append(" ")
                else:
                    expr_braces[-1] -= 1
                    out.append(c)
                i += 1
            else:
                out.append(c)
                i += 1
        elif mode == "line":
            if c == "\n":
                mode = "code"
                out.append("\n")
            else:
                out.append(" ")
            i += 1
        elif mode == "block":
            if c == "*" and nxt == "/":
                mode = "code"
                out.append("  ")
                i += 2
            else:
                out.append("\n" if c == "\n" else " ")
                i += 1
        elif mode == "str":
            if c == "\\" and i + 1 < n:
                out.append("  " if nxt != "\n" else " \n")
                i += 2
            elif c == quote or c == "\n":
                mode = "code"
                out.append("\n" if c == "\n" else " ")
                i += 1
            else:
                out.append(" ")
                i += 1
        else:  # template literal body
            if c == "\\" and i + 1 < n:
                out.append("  " if nxt != "\n" else " \n")
                i += 2
            elif c == "`":
                mode = "code"
                out.append(" ")
                i += 1
            elif c == "$" and nxt == "{":
                mode = "code"
                expr_braces.append(0)
                out.append("  ")
                i += 2
            else:
                out.append("\n" if c == "\n" else " ")
                i += 1
    return "".join(out)


def _call_count(code, names):
    """Calls to any of names in code. A declaration such as `function delay(` is not a call."""
    if not names:
        return 0
    pattern = re.compile(r"\b(?:" + "|".join(re.escape(name) for name in sorted(names)) + r")\s*\(")
    total = 0
    for match in pattern.finditer(code):
        if FUNCTION_KEYWORD_BEFORE.search(code[max(0, match.start() - 40) : match.start()]):
            continue
        total += 1
    return total


def _brace_body(code, open_index):
    """The text of a {...} block starting at open_index (the opening brace), braces included."""
    depth = 0
    for j in range(open_index, len(code)):
        if code[j] == "{":
            depth += 1
        elif code[j] == "}":
            depth -= 1
            if depth == 0:
                return code[open_index : j + 1]
    return code[open_index:]


def _local_functions(code):
    """(name, body) for each local function declaration and arrow-function binding in code."""
    found = []
    for match in FUNC_DECL.finditer(code):
        found.append((match.group(1), _brace_body(code, match.end() - 1)))
    for match in ARROW_DECL.finditer(code):
        if match.end() < len(code) and code[match.end()] == "{":
            body = _brace_body(code, match.end())
        else:
            stop = re.search(r"[;\n]", code[match.end() :])
            body = code[match.end() : match.end() + (stop.start() if stop else len(code))]
        found.append((match.group(1), body))
    return found


def _wait_helpers(code, base_names):
    """Names of local helpers whose body waits on the clock, found to a fixed point."""
    functions = _local_functions(code)
    helpers = set()
    changed = True
    while changed:
        changed = False
        active = base_names | helpers
        for name, body in functions:
            if name in helpers or _call_count(body, active) == 0:
                continue
            if WAIT_HELPER_NAME.search(name) or not BROWSER_CALL.search(body):
                helpers.add(name)
                changed = True
    return helpers


def count_waits(text):
    """Number of executed wall-clock wait calls in one e2e source file."""
    code = strip_comments_and_strings(text)
    aliases = set(ALIAS_IMPORT.findall(code)) | set(ALIAS_ASSIGN.findall(code))
    base = set(WAIT_NAMES) | aliases
    return _call_count(code, base | _wait_helpers(code, base))


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
    """Return {repo-relative posix path: wait count} for every e2e source file under root."""
    root = Path(root)
    e2e_root = root / E2E_DIR
    if not e2e_root.is_dir() or not any(e2e_root.rglob("*.e2e.ts")):
        raise RatchetError(f"no e2e specs matched {SPEC_GLOB} under {root}")
    counts = {}
    for path in sorted(e2e_root.rglob("*")):
        if path.is_file() and path.suffix in SOURCE_SUFFIXES:
            counts[path.relative_to(root).as_posix()] = count_waits(path.read_text(encoding="utf-8"))
    return counts


def compare(baseline, current):
    """Return (violations, notes) for a current scan against the baseline."""
    violations = []
    notes = []
    for name, now in sorted(current.items()):
        before = baseline.get(name, 0)
        if now > before:
            violations.append(f"{name}: {before} -> {now} wall-clock waits (sleep/setTimeout/waitForTimeout)")
        elif now < before:
            notes.append(f"{name}: {before} -> {now}; lower the baseline entry to lock in the gain")
    for name in sorted(set(baseline) - set(current)):
        notes.append(f"{name}: in baseline but not on disk; remove the entry")
    return violations, notes


def main(argv=None):
    parser = argparse.ArgumentParser(description="Ratchet wall-clock waits in tests/client/e2e sources.")
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
            "wall-clock sleep, setTimeout or waitForTimeout. Do not raise the baseline.",
            file=sys.stderr,
        )
        return 1
    print(
        f"e2e-wait-ratchet: OK ({sum(current.values())} wall-clock waits across {len(current)} files; "
        f"baseline {sum(baseline.values())})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
