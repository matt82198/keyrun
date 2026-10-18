<#
.SYNOPSIS
  Run the Godot 4.7.2 .NET console binary for Keyrun with the .NET SDK on PATH.

.DESCRIPTION
  Resolves the engine binary from $env:KEYRUN_GODOT, falling back to the winget
  install under %LOCALAPPDATA%. Resolves the .NET root from $env:DOTNET_ROOT,
  falling back to %USERPROFILE%\.dotnet, exports DOTNET_ROOT and prepends it to
  PATH, then runs Godot with every argument passed through unchanged. Exits with
  Godot's exit code. Fails closed (exit 2) when the binary cannot be found.

  Lanes and CI call this wrapper; nothing else hardcodes the engine path.

.NOTES
  PowerShell consumes a bare -- when it binds script arguments. To pass Godot's
  user-argument separator, quote it:  tools/godot/godot.ps1 --headless --path game '--' --smoke
  or use KEYRUN_USER_ARGS:  $env:KEYRUN_USER_ARGS='--smoke'  (appended after a -- separator).

.EXAMPLE
  pwsh tools/godot/godot.ps1 --headless --path game --build-solutions --quit
#>
$ErrorActionPreference = 'Stop'

$wingetRel = 'Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'

$godot = $env:KEYRUN_GODOT
if (-not $godot -and $env:LOCALAPPDATA) {
    $godot = Join-Path $env:LOCALAPPDATA $wingetRel
}
if (-not $godot -or -not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    [Console]::Error.WriteLine("godot.ps1: Godot binary not found at '$godot'. Set KEYRUN_GODOT to the Godot 4.7.2 mono console executable.")
    exit 2
}

$dotnetRoot = $env:DOTNET_ROOT
if (-not $dotnetRoot -and $env:USERPROFILE) {
    $candidate = Join-Path $env:USERPROFILE '.dotnet'
    if (Test-Path -LiteralPath (Join-Path $candidate 'dotnet.exe') -PathType Leaf) {
        $dotnetRoot = $candidate
    }
}
if ($dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = $dotnetRoot + [IO.Path]::PathSeparator + $env:PATH
}

$godotArgs = @($args)
if ($env:KEYRUN_USER_ARGS) {
    if ($godotArgs -notcontains '--') {
        $godotArgs += '--'
    }
    $godotArgs += ($env:KEYRUN_USER_ARGS -split '\s+' | Where-Object { $_ })
}

# Godot writes warnings to stderr; under 'Stop' a caller's 2>&1 would turn them
# into terminating NativeCommandErrors. The exit code is the verdict instead.
$ErrorActionPreference = 'Continue'
& $godot @godotArgs
exit $LASTEXITCODE
