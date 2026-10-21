<#
.SYNOPSIS
  Keyrun per-step lane check: incremental build of game/Keyrun.sln (the compiler is the
  first gate), then only the xUnit projects the diff vs the merge base can affect.

.DESCRIPTION
  1. Collects every path changed since `git merge-base <Base> HEAD`: committed, staged,
     unstaged and untracked (a lane mid-step has uncommitted edits).
  2. Maps each path to test projects with the table below. Any path that no row matches,
     a git failure, or an empty change set selects ALL test projects (fail closed).
  3. Runs `dotnet build game/Keyrun.sln` (incremental; TreatWarningsAsErrors, nullable and the
     Keyrun.Sim banned-API analyzer all fail here), then `dotnet test --no-build` per project.
  Exit 0 only when the build and every selected test project pass. Any failure exits nonzero.

  This is the FAST per-step loop, not a replacement for anything: CI game-test still runs
  `dotnet test game/Keyrun.sln` in full, and the end-of-lane gates still run every suite.

.PARAMETER Base
  The ref to diff against. Default origin/main.

.PARAMETER All
  Run every test project regardless of the diff.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/fastcheck.ps1
#>
param(
    [string]$Base = 'origin/main',
    [switch]$All
)
$ErrorActionPreference = 'Stop'

# --- Path prefix -> test projects. First matching row wins; order specific before general. ---
# A row's project list must include every test project whose assemblies reference the changed
# code (Keyrun.Bots references Keyrun.Sim and Keyrun.Content, so Sim or Content source also runs
# Bots). An empty list means "build only" and is reserved for paths no xUnit project reads.
# Paths not listed (content/data JSON, game/src, game/scenes, game/assets, csproj/sln, anything
# else) run ALL projects, because the test projects read those trees from disk.
$Sim     = 'game/sim/Keyrun.Sim.Tests/Keyrun.Sim.Tests.csproj'
$Content = 'game/content/Keyrun.Content.Tests/Keyrun.Content.Tests.csproj'
$Bots    = 'game/bots/Keyrun.Bots.Tests/Keyrun.Bots.Tests.csproj'
$AllProjects = @($Content, $Sim, $Bots)
$Map = @(
    @{ Prefix = 'game/sim/Keyrun.Sim.Tests/';         Projects = @($Sim) },
    @{ Prefix = 'game/sim/Keyrun.Sim/';               Projects = @($Sim, $Bots) },
    @{ Prefix = 'tools/Keyrun.Replay/';               Projects = @($Sim) },
    @{ Prefix = 'game/content/Keyrun.Content.Tests/'; Projects = @($Content) },
    @{ Prefix = 'game/content/Keyrun.Content/';       Projects = @($Content, $Bots) },
    @{ Prefix = 'game/bots/Keyrun.Bots.Tests/';       Projects = @($Bots) },
    @{ Prefix = 'game/bots/Keyrun.Bots/';             Projects = @($Bots) },
    @{ Prefix = 'game/lanes/';                        Projects = @() },
    @{ Prefix = 'tools/fastcheck.ps1';                Projects = @() }
)

$sw = [Diagnostics.Stopwatch]::StartNew()
$repo = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $repo) {
    [Console]::Error.WriteLine('fastcheck: not inside a git work tree')
    exit 2
}
Set-Location -LiteralPath $repo

# Same resolution as tools/godot/godot.ps1: DOTNET_ROOT, then %USERPROFILE%\.dotnet, then PATH.
$dotnet = 'dotnet'
$dotnetRoot = $env:DOTNET_ROOT
if (-not $dotnetRoot -and $env:USERPROFILE) {
    $candidate = Join-Path $env:USERPROFILE '.dotnet'
    if (Test-Path -LiteralPath (Join-Path $candidate 'dotnet.exe') -PathType Leaf) { $dotnetRoot = $candidate }
}
if ($dotnetRoot -and (Test-Path -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe') -PathType Leaf)) {
    $env:DOTNET_ROOT = $dotnetRoot
    $dotnet = Join-Path $dotnetRoot 'dotnet.exe'
}
# No lingering build daemons holding a piped stdout open (see tools/godot/godot.ps1). Defaults only.
if (-not $env:MSBUILDDISABLENODEREUSE) { $env:MSBUILDDISABLENODEREUSE = '1' }
if (-not $env:UseSharedCompilation) { $env:UseSharedCompilation = 'false' }
if (-not $env:DOTNET_CLI_USE_MSBUILD_SERVER) { $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0' }

# --- Select projects (fail closed: anything uncertain selects all). ---
$selected = [System.Collections.Generic.List[string]]::new()
$reason = ''
if ($All) {
    $reason = '-All'
} else {
    $mergeBase = (& git merge-base $Base HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $mergeBase) {
        $reason = "no merge base with $Base"
        $All = $true
    } else {
        $changed = @(& git diff --name-only --no-renames $mergeBase 2>$null)
        $diffOk = ($LASTEXITCODE -eq 0)
        $untracked = @(& git ls-files --others --exclude-standard 2>$null)
        if (-not $diffOk -or $LASTEXITCODE -ne 0) {
            $reason = 'git diff failed'
            $All = $true
        } else {
            $paths = @($changed + $untracked | Where-Object { $_ } | Sort-Object -Unique)
            if ($paths.Count -eq 0) {
                $reason = 'no changed paths'
                $All = $true
            }
            foreach ($p in $paths) {
                $row = $Map | Where-Object { $p.StartsWith($_.Prefix, [StringComparison]::Ordinal) } | Select-Object -First 1
                if (-not $row) {
                    $reason = "unmapped path $p"
                    $All = $true
                    break
                }
                foreach ($proj in $row.Projects) { if (-not $selected.Contains($proj)) { $selected.Add($proj) } }
            }
            if (-not $All) { $reason = "$($paths.Count) changed path(s) vs $Base" }
        }
    }
}
if ($All) { $selected = [System.Collections.Generic.List[string]]::new([string[]]$AllProjects) }
Write-Output "fastcheck: tests -> $(if ($selected.Count) { $selected -join ', ' } else { '(build only)' })  [$reason]"

# --- Build: the compiler gate. ---
& $dotnet build game/Keyrun.sln --nologo -v q -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) {
    Write-Output "fastcheck: FAIL build (exit $LASTEXITCODE) after $([int]$sw.Elapsed.TotalSeconds)s"
    exit 1
}
Write-Output "fastcheck: build ok at $([int]$sw.Elapsed.TotalSeconds)s"

# --- Tests. ---
$failed = @()
foreach ($proj in $selected) {
    # Category=Balance facts (fork parity) are reported below, not gated; every other assertion gates.
    & $dotnet test $proj --no-build --nologo -v q --filter 'Category!=Balance'
    if ($LASTEXITCODE -ne 0) { $failed += $proj }
}
# balance: reported, never part of the exit code (CI runs it in the non-required `balance` job).
$balanceRed = $false
if ($selected.Contains($Bots)) {
    Write-Output 'balance: (reported, not a gate) Keyrun.Bots.Tests --filter Category=Balance'
    & $dotnet test $Bots --no-build --nologo -v q --filter 'Category=Balance'
    if ($LASTEXITCODE -ne 0) { $balanceRed = $true }
    Write-Output "balance: $(if ($balanceRed) { 'RED (reported, not a gate failure: open a finding)' } else { 'green' })"
}
$secs = [int]$sw.Elapsed.TotalSeconds
if ($failed.Count) {
    Write-Output "fastcheck: FAIL tests in $($failed -join ', ') after ${secs}s"
    exit 1
}
Write-Output "fastcheck: PASS in ${secs}s$(if ($balanceRed) { ' (balance red, reported)' })"
exit 0
