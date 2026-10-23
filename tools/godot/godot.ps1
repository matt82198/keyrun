<#
.SYNOPSIS
  Run the Godot 4.7.2 .NET console binary for Keyrun with the .NET SDK on PATH.

.DESCRIPTION
  Resolves the engine binary from $env:KEYRUN_GODOT, falling back to the winget
  install under %LOCALAPPDATA%. Resolves the .NET root from $env:DOTNET_ROOT,
  falling back to %USERPROFILE%\.dotnet, exports DOTNET_ROOT and prepends it to
  PATH, then runs Godot with every argument passed through unchanged. Exits with
  Godot's exit code. Fails closed (exit 2) when the binary cannot be found.

  CI and the orchestrator call this wrapper; nothing else hardcodes the engine path. Lanes never launch the game.

  Single instance (tools/godot/launch-lock.ps1): the launch takes the machine-wide launch lock first. While another
  live process holds it, this exits 4 at once with "another Godot instance holds the launch lock" and never queues.
  A launch that carries the holder's KEYRUN_LAUNCH_LOCK_TOKEN (a SceneTest running its launches in sequence) joins
  the held lock. A lock whose owner process is gone is recovered.

  Wall-clock cap: Godot runs inside a kill-on-close job object. After KEYRUN_GODOT_TIMEOUT_SEC seconds (default
  300) the whole process tree is killed and this exits 124. Killing this wrapper also kills the game it started.
  Godot's stdout and stderr lines are written to this script's output stream, in arrival order.

  Preflight (tools/godot/preflight.ps1, finding f-3ccab3b2): before the lock is taken, a launch that loads the project
  needs the project's .NET assembly under <project>/.godot/mono/temp/bin/Debug and newer than the .cs files under src, sim,
  bots and content. Otherwise this prints "PREFLIGHT missing-assembly <path>" or "PREFLIGHT stale-assembly <path> newer=<file>",
  the build command, and exits 6 without launching. It never builds. The same exit 6 ("PREFLIGHT missing-dotnet <path>")
  covers a missing .NET runtime: no dotnet executable under the resolved DOTNET_ROOT and, with no root, none on PATH. Launches that build or export the assembly
  (--build-solutions, --export-release, --export-debug) and --version or --help load no project and are exempt.

.NOTES
  Exit codes: the Godot exit code, 2 Godot binary missing, 4 another Godot instance holds the launch lock,
  6 the .NET assembly is missing or stale (preflight; nothing was launched), 124 wall-clock cap reached.

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

# No lingering .NET build daemons. Godot's --build-solutions and --export-release
# run dotnet build/publish, which by default leave MSBuild worker nodes
# (/nodeReuse:true, 15 min idle) and the Roslyn VBCSCompiler server (10 min idle)
# running. Those daemons inherit the caller's stdout/stderr handles, so any caller
# that reads Godot's output through a pipe (GitHub Actions pwsh steps, `| Out-String`,
# `| Tee-Object`) blocks until they idle out: 10 to 15 minutes after Godot finished.
# That made game-e2e's build and export steps take exactly 10:00 / 15:00 extra and
# blow the job timeout once 1E added a second ProjectReference. Defaults only; an
# explicit caller value wins.
if (-not $env:MSBUILDDISABLENODEREUSE) { $env:MSBUILDDISABLENODEREUSE = '1' }
if (-not $env:UseSharedCompilation) { $env:UseSharedCompilation = 'false' }
if (-not $env:DOTNET_CLI_USE_MSBUILD_SERVER) { $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0' }

$godotArgs = @($args)
if ($env:KEYRUN_USER_ARGS) {
    if ($godotArgs -notcontains '--') {
        $godotArgs += '--'
    }
    $godotArgs += ($env:KEYRUN_USER_ARGS -split '\s+' | Where-Object { $_ })
}

# Preflight before the lock: the same check screens.ps1 runs (finding f-3ccab3b2). Self-building and non-project launches are exempt.
. (Join-Path $PSScriptRoot 'preflight.ps1')
$selfBuilding = @('--build-solutions', '--export-release', '--export-debug', '--version', '--help', '-h')
if (@($godotArgs | Where-Object { $selfBuilding -contains $_ }).Count -eq 0) {
    $projRel = 'game'
    for ($i = 0; $i -lt ($godotArgs.Count - 1); $i++) {
        if ($godotArgs[$i] -eq '--path') { $projRel = $godotArgs[$i + 1] }
    }
    $repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
    $preflight = Test-KeyrunDotnetRuntime -DotnetRoot $dotnetRoot
    if ($preflight.Ok) { $preflight = Test-KeyrunDotnetAssembly -RepoRoot $repoRoot -ProjectRel $projRel }
    # Import cache (finding f-30dcbb08): unimported .glb models render as capsules. The --import pass itself is exempt (tools/godot/import.ps1 runs it).
    if ($preflight.Ok -and @($godotArgs | Where-Object { $_ -eq '--import' }).Count -eq 0) { $preflight = Test-KeyrunImportCache -RepoRoot $repoRoot -ProjectRel $projRel }
    if (-not $preflight.Ok) {
        foreach ($line in $preflight.Lines) { Write-Output $line }
        exit $preflight.Code
    }
}

. (Join-Path $PSScriptRoot 'launch-lock.ps1')
try {
    $timeoutSec = Get-KeyrunGodotTimeoutSec
} catch {
    [Console]::Error.WriteLine("godot.ps1: $($_.Exception.Message)")
    exit 2
}

# Godot resolves a relative --path against the caller's working folder, not the repo root (finding: a foreign cwd made
# Godot fail on "game"). Only the argv handed to Godot gets the absolute project folder; the preflight messages above keep
# the relative form they always printed.
$repoRootAbs = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$launchArgs = @($godotArgs)
for ($i = 0; $i -lt ($launchArgs.Count - 1); $i++) {
    if ($launchArgs[$i] -eq '--path' -and -not [IO.Path]::IsPathRooted($launchArgs[$i + 1])) {
        $launchArgs[$i + 1] = Join-Path $repoRootAbs $launchArgs[$i + 1]
    }
}

# Lock-check stamp: the moment this launcher reaches the lock. tools/godot/launch-lock-test.ps1 case A reads it to separate
# the cold start (preflight, C# compile) from the fail-fast interval (lock check to exit). It is the only change to stdout.
Write-Output ('launch-lock: check at ' + [DateTime]::UtcNow.ToString('o'))
$launchLock = Enter-KeyrunLaunchLock -Owner ('godot.ps1 ' + ($godotArgs -join ' ')) -ExitCodeOnBusy 4
try {
    # Godot writes warnings to stderr; they arrive as plain output lines, so a caller's 2>&1 under 'Stop' cannot turn
    # them into terminating errors. The exit code is the verdict.
    $ErrorActionPreference = 'Continue'
    Invoke-KeyrunCapped -FilePath $godot -Arguments $launchArgs -TimeoutSec $timeoutSec -Label 'godot.ps1'
    $code = $script:KeyrunLastExitCode
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
exit $code
