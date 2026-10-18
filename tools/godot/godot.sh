#!/usr/bin/env sh
# Run the Godot 4.7.2 .NET console binary for Keyrun with the .NET SDK on PATH.
#
# Engine binary: $KEYRUN_GODOT, else the winget install under %LOCALAPPDATA%.
# .NET root:     $DOTNET_ROOT, else $HOME/.dotnet (when it holds a dotnet binary).
# DOTNET_ROOT is exported (as a native Windows path under Git Bash/MSYS) and
# prepended to PATH; all arguments pass through unchanged, including "--".
# Exit code is Godot's. Fails closed (exit 2) when the binary cannot be found.
#
# Example: tools/godot/godot.sh --headless --path game --quit-after 60 -- --smoke
set -eu

winget_rel='Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'

godot="${KEYRUN_GODOT:-}"
if [ -z "$godot" ] && [ -n "${LOCALAPPDATA:-}" ]; then
  local_app="$LOCALAPPDATA"
  if command -v cygpath >/dev/null 2>&1; then
    local_app="$(cygpath -u "$LOCALAPPDATA")"
  fi
  godot="$local_app/$winget_rel"
fi
if [ -z "$godot" ] || [ ! -f "$godot" ]; then
  echo "godot.sh: Godot binary not found at '$godot'. Set KEYRUN_GODOT to the Godot 4.7.2 mono console executable." >&2
  exit 2
fi

dotnet_root="${DOTNET_ROOT:-}"
if [ -z "$dotnet_root" ] && [ -n "${HOME:-}" ]; then
  if [ -f "$HOME/.dotnet/dotnet" ] || [ -f "$HOME/.dotnet/dotnet.exe" ]; then
    dotnet_root="$HOME/.dotnet"
  fi
fi
if [ -n "$dotnet_root" ]; then
  if command -v cygpath >/dev/null 2>&1; then
    PATH="$(cygpath -u "$dotnet_root"):$PATH"
    DOTNET_ROOT="$(cygpath -w "$dotnet_root")"
  else
    PATH="$dotnet_root:$PATH"
    DOTNET_ROOT="$dotnet_root"
  fi
  export PATH DOTNET_ROOT
fi

exec "$godot" "$@"
