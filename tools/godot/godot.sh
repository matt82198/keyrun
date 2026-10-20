#!/usr/bin/env sh
# Run the Godot 4.7.2 .NET console binary for Keyrun with the .NET SDK on PATH.
#
# Engine binary: $KEYRUN_GODOT (on Windows, else the winget install under %LOCALAPPDATA%, resolved by godot.ps1).
# .NET root:     $DOTNET_ROOT, else $HOME/.dotnet (when it holds a dotnet binary).
# All arguments pass through unchanged, including "--". Fails closed (exit 2) when the binary cannot be found.
#
# Single instance and wall-clock cap (lanes never launch the game; launches are the orchestrator's serial step):
#   Windows (Git Bash / MSYS / Cygwin): delegates to tools/godot/godot.ps1, so there is exactly one launch path and
#     one machine-wide lock (tools/godot/launch-lock.ps1). Exit 4 = another Godot instance holds the launch lock,
#     124 = killed at the KEYRUN_GODOT_TIMEOUT_SEC cap (default 300) with its whole process tree.
#   Elsewhere: the same contract with a mkdir lock at ${KEYRUN_LAUNCH_LOCK_FILE:-${TMPDIR:-/tmp}/keyrun-godot-launch.lock}.d
#     (owner PID + start time; stale only when the owner is gone) and GNU timeout, which kills the process group.
#
# Example: tools/godot/godot.sh --headless --path game --quit-after 60 -- --smoke
set -eu

script_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)

case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*)
    if [ -n "${KEYRUN_GODOT:-}" ]; then
      KEYRUN_GODOT="$(cygpath -w "$KEYRUN_GODOT")"
      export KEYRUN_GODOT
    fi
    if [ -n "${DOTNET_ROOT:-}" ]; then
      DOTNET_ROOT="$(cygpath -w "$DOTNET_ROOT")"
      export DOTNET_ROOT
    fi
    exec powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$(cygpath -w "$script_dir/godot.ps1")" "$@"
    ;;
esac

godot="${KEYRUN_GODOT:-}"
if [ -z "$godot" ] || [ ! -f "$godot" ]; then
  echo "godot.sh: Godot binary not found at '$godot'. Set KEYRUN_GODOT to the Godot 4.7.2 mono console executable." >&2
  exit 2
fi

timeout_sec="${KEYRUN_GODOT_TIMEOUT_SEC:-300}"
case "$timeout_sec" in
  ''|*[!0-9]*|0) echo "godot.sh: KEYRUN_GODOT_TIMEOUT_SEC='$timeout_sec' is not a positive whole number of seconds (there is no uncapped launch)" >&2; exit 2 ;;
esac
if ! command -v timeout >/dev/null 2>&1; then
  echo "godot.sh: GNU timeout not found; refusing an uncapped launch" >&2
  exit 2
fi

dotnet_root="${DOTNET_ROOT:-}"
if [ -z "$dotnet_root" ] && [ -n "${HOME:-}" ]; then
  if [ -f "$HOME/.dotnet/dotnet" ]; then
    dotnet_root="$HOME/.dotnet"
  fi
fi
if [ -n "$dotnet_root" ]; then
  PATH="$dotnet_root:$PATH"
  DOTNET_ROOT="$dotnet_root"
  export PATH DOTNET_ROOT
fi
: "${MSBUILDDISABLENODEREUSE:=1}" "${UseSharedCompilation:=false}" "${DOTNET_CLI_USE_MSBUILD_SERVER:=0}"
export MSBUILDDISABLENODEREUSE UseSharedCompilation DOTNET_CLI_USE_MSBUILD_SERVER

# ---- single-instance lock ------------------------------------------------------------------------------------------
lock_dir="${KEYRUN_LAUNCH_LOCK_FILE:-${TMPDIR:-/tmp}/keyrun-godot-launch.lock}.d"

start_of() { # process start time (clock ticks since boot), or nothing when /proc is not available
  if [ -r "/proc/$1/stat" ]; then
    sed 's/^.*) //' "/proc/$1/stat" | cut -d' ' -f20
  fi
}

owner_alive() { # the pid + start files in $lock_dir describe a running process
  o_pid=$(cat "$lock_dir/pid" 2>/dev/null || true)
  [ -n "$o_pid" ] || return 1
  kill -0 "$o_pid" 2>/dev/null || return 1
  o_start=$(cat "$lock_dir/start" 2>/dev/null || true)
  [ -z "$o_start" ] && return 0
  [ "$(start_of "$o_pid")" = "$o_start" ]
}

busy() {
  echo "godot.sh: another Godot instance holds the launch lock ($1; lock $lock_dir). Launches are serial: lanes never launch the game, the orchestrator runs launches and captures one at a time." >&2
  exit 4
}

nested=0
if ! mkdir "$lock_dir" 2>/dev/null; then
  if owner_alive; then
    if [ -n "${KEYRUN_LAUNCH_LOCK_TOKEN:-}" ] && [ "$(cat "$lock_dir/token" 2>/dev/null || true)" = "$KEYRUN_LAUNCH_LOCK_TOKEN" ]; then
      nested=1
    else
      busy "pid $(cat "$lock_dir/pid" 2>/dev/null || true), '$(cat "$lock_dir/owner" 2>/dev/null || true)'"
    fi
  else
    rm -rf "$lock_dir"
    mkdir "$lock_dir" 2>/dev/null || busy 'lost the stale-lock recovery race'
  fi
fi
if [ "$nested" -eq 0 ]; then
  token="$$-$(date +%s)-$(od -An -N8 -tx1 /dev/urandom 2>/dev/null | tr -d ' \n')"
  echo "$$" > "$lock_dir/pid"
  start_of "$$" > "$lock_dir/start"
  echo "$token" > "$lock_dir/token"
  echo "godot.sh $*" > "$lock_dir/owner"
  KEYRUN_LAUNCH_LOCK_TOKEN="$token"
  export KEYRUN_LAUNCH_LOCK_TOKEN
  trap 'if [ "$(cat "$lock_dir/token" 2>/dev/null || true)" = "$token" ]; then rm -rf "$lock_dir"; fi' EXIT
  trap 'exit 130' INT
  trap 'exit 143' TERM
fi

# ---- capped run: GNU timeout signals the whole process group, then SIGKILL 5s later --------------------------------
set +e
timeout --kill-after=5 "$timeout_sec" "$godot" "$@"
code=$?
set -e
if [ "$code" -eq 124 ] || [ "$code" -eq 137 ]; then
  echo "godot.sh: wall-clock cap of ${timeout_sec}s reached; killed the Godot process group. Set KEYRUN_GODOT_TIMEOUT_SEC to change the cap." >&2
  exit 124
fi
exit "$code"
