#!/bin/sh
# prepush-secret-scan.test.sh - behavioural tests for hooks/pre-push (finding f-9e6f6f46).
#
# Contract under test: hooks/pre-push reads git pre-push stdin lines
# "<local ref> <local sha> <remote ref> <remote sha>", runs the secret scanner
# over the pushed range, and exits 1 on any finding or a missing scanner.
#
# Scanner resolution (test and hook alike): SECRET_SCAN, else $HOME/scripts/secret_scan.py.
# When neither exists the test prints a SKIP line and exits 0 without running cases.
#
# Exit codes: 0 = all cases pass or SKIP, 1 = at least one case failed.

set -eu

readonly ZERO_SHA='0000000000000000000000000000000000000000'

SCRIPT_DIR="$(cd -- "$(dirname -- "$0")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
HOOK="$REPO_ROOT/hooks/pre-push"

FAILURES=0
TMP_ROOT="$(mktemp -d)"
trap 'rm -rf -- "$TMP_ROOT"' EXIT

# log_line PREFIX MESSAGE - one line per event, to stdout.
log_line() {
  printf '%s %s\n' "$1" "$2"
}

# resolve_scanner - print the real scanner path: SECRET_SCAN, else $HOME default.
# Prints nothing when neither exists.
resolve_scanner() {
  if [ -n "${SECRET_SCAN:-}" ]; then
    printf '%s\n' "$SECRET_SCAN"
  elif [ -n "${HOME:-}" ] && [ -f "$HOME/scripts/secret_scan.py" ]; then
    printf '%s\n' "$HOME/scripts/secret_scan.py"
  fi
}

# new_repo DIR - create an empty git repo with a fixed identity.
new_repo() {
  git init -q -- "$1"
  git -C "$1" config user.name 'test'
  git -C "$1" config user.email 'test@example.invalid'
  git -C "$1" config commit.gpgsign false
}

# commit_file REPO NAME CONTENT - write NAME, stage it, commit, print the new sha.
commit_file() {
  printf '%s\n' "$3" >"$1/$2"
  git -C "$1" add -- "$2"
  git -C "$1" commit -q -m "add $2"
  git -C "$1" rev-parse HEAD
}

# run_hook REPO SHA SCANNER - feed one new-branch pre-push line to the hook from inside REPO.
# Prints the hook exit status. A missing hook file yields 127 from sh.
run_hook() {
  local status=0
  (
    cd -- "$1" || exit 99
    printf 'refs/heads/x %s refs/heads/x %s\n' "$2" "$ZERO_SHA" \
      | SECRET_SCAN="$3" sh "$HOOK" origin 'https://example.invalid/keyrun.git' \
      >/dev/null 2>&1
  ) || status=$?
  printf '%s\n' "$status"
}

# run_hook_unterminated REPO LOCAL_SHA REMOTE_SHA SCANNER - one pre-push line with NO trailing newline.
# Prints the hook exit status.
run_hook_unterminated() {
  local status=0
  (
    cd -- "$1" || exit 99
    printf 'refs/heads/x %s refs/heads/x %s' "$2" "$3" \
      | SECRET_SCAN="$4" sh "$HOOK" origin 'https://example.invalid/keyrun.git' \
      >/dev/null 2>&1
  ) || status=$?
  printf '%s\n' "$status"
}

# assert_status NAME EXPECTED ACTUAL - record PASS or FAIL for one case.
assert_status() {
  if [ "$2" = "$3" ]; then
    log_line 'PASS' "$1 (exit $3)"
  else
    log_line 'FAIL' "$1 (expected exit $2, got $3)"
    FAILURES=$((FAILURES + 1))
  fi
}

# Precondition: the hook under test must exist. Without it every case below
# would report a misleading status from sh, so fail loudly here instead.
if [ ! -f "$HOOK" ]; then
  log_line 'FAIL' "hooks/pre-push does not exist at $HOOK"
  FAILURES=$((FAILURES + 1))
fi

REAL_SCANNER="$(resolve_scanner)"
if [ -z "$REAL_SCANNER" ]; then
  log_line 'SKIP' 'no secret scanner: set SECRET_SCAN or install $HOME/scripts/secret_scan.py'
  exit 0
fi

# Build the fake secret at runtime so the literal never appears in this source.
# Shape: AWS access key id (AKIA + 16 uppercase/digit characters).
SECRET_HEAD='AKIA'
SECRET_TAIL='ABCDEFGHIJKLMNOP'
FAKE_SECRET="${SECRET_HEAD}${SECRET_TAIL}"

# Case 1: a commit carrying the fake secret must block the push (exit 1).
REPO_DIRTY="$TMP_ROOT/dirty"
new_repo "$REPO_DIRTY"
BASE_SHA="$(commit_file "$REPO_DIRTY" 'base.txt' 'base')"
DIRTY_SHA="$(commit_file "$REPO_DIRTY" 'config.txt' "aws_key = $FAKE_SECRET")"
assert_status 'secret commit blocks push' 1 \
  "$(run_hook "$REPO_DIRTY" "$DIRTY_SHA" "$REAL_SCANNER")"

# Case 2: a clean commit must allow the push (exit 0).
REPO_CLEAN="$TMP_ROOT/clean"
new_repo "$REPO_CLEAN"
commit_file "$REPO_CLEAN" 'base.txt' 'base' >/dev/null
CLEAN_SHA="$(commit_file "$REPO_CLEAN" 'notes.txt' 'nothing sensitive here')"
assert_status 'clean commit allows push' 0 \
  "$(run_hook "$REPO_CLEAN" "$CLEAN_SHA" "$REAL_SCANNER")"

# Case 3: a missing scanner must fail closed (exit 1), even for a clean commit.
MISSING_SCANNER="$TMP_ROOT/does-not-exist/secret_scan.py"
assert_status 'missing scanner fails closed' 1 \
  "$(run_hook "$REPO_CLEAN" "$CLEAN_SHA" "$MISSING_SCANNER")"

# Case 4a: secret in the ROOT commit, second commit clean, no origin/main
# (fallback base is the root). The root must be scanned too (exit 1).
REPO_ROOT_DIR="$TMP_ROOT/root-secret"
new_repo "$REPO_ROOT_DIR"
commit_file "$REPO_ROOT_DIR" 'config.txt' "aws_key = $FAKE_SECRET" >/dev/null
ROOT_SECRET_HEAD="$(commit_file "$REPO_ROOT_DIR" 'notes.txt' 'clean second commit')"
assert_status 'root-commit secret blocks root-based push' 1 \
  "$(run_hook "$REPO_ROOT_DIR" "$ROOT_SECRET_HEAD" "$REAL_SCANNER")"

# Case 4b: single-commit repo carrying the secret (base == local == root) must block (exit 1).
REPO_SOLO="$TMP_ROOT/solo"
new_repo "$REPO_SOLO"
SOLO_SHA="$(commit_file "$REPO_SOLO" 'config.txt' "aws_key = $FAKE_SECRET")"
assert_status 'single-commit secret blocks push' 1 \
  "$(run_hook "$REPO_SOLO" "$SOLO_SHA" "$REAL_SCANNER")"

# Case 4c: final stdin line without a trailing newline carrying a secret push must block (exit 1).
assert_status 'unterminated final stdin line still scanned' 1 \
  "$(run_hook_unterminated "$REPO_DIRTY" "$DIRTY_SHA" "$BASE_SHA" "$REAL_SCANNER")"

if [ "$FAILURES" -ne 0 ]; then
  log_line 'SUMMARY' "$FAILURES case(s) failed"
  exit 1
fi
log_line 'SUMMARY' 'all cases passed'
exit 0
