<#
.SYNOPSIS
  First step of every fresh Keyrun worktree: build the game assembly, then fill the Godot import cache once.

.DESCRIPTION
  Matt 2026-10-21: "Run a one-time Godot --import in the worktree setup script, so no capture ever runs without assets."
  (finding f-30dcbb08: a fresh worktree has no game/.godot/imported, so the KayKit .glb models render as capsules.)

  1. Builds <project>/Keyrun.csproj in Debug with the user's dotnet. DOTNET_ROOT resolves exactly as tools/godot/godot.ps1
     does: $env:DOTNET_ROOT, else %USERPROFILE%\.dotnet when it holds dotnet.exe, else dotnet on PATH. The build writes the
     assembly preflight.ps1 checks (<project>/.godot/mono/temp/bin/Debug).
  2. Runs tools/godot/import.ps1 only when Test-KeyrunImportCache reports a model unimported (Invoke-KeyrunImportIfMissing),
     so a second run is a no-op: import=cached.

  Output, last line on success:  SETUP done build=ok import=<ran|cached> files=<n>
  where n counts the files under <project>/.godot/imported.
  Lanes never run this script (it launches Godot when the cache is missing); the orchestrator runs it once per worktree.

.PARAMETER ProjectRel
  The Godot project folder, relative to the repo root. Default game.

.PARAMETER ImportScript
  The import script to run when the cache is missing. Default tools/godot/import.ps1 next to this script.

.NOTES
  Exit codes: 0 done; 3 the dotnet build failed ("SETUP failed build exit=<n>"); 6 the import failed or left the cache
  missing ("SETUP failed import" after the PREFLIGHT lines).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/setup-worktree.ps1
#>
param(
    [string]$ProjectRel = 'game',
    [string]$ImportScript = ''
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $ImportScript) { $ImportScript = Join-Path $PSScriptRoot 'import.ps1' }

# Same .NET and build-daemon environment as tools/godot/godot.ps1.
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
if (-not $env:MSBUILDDISABLENODEREUSE) { $env:MSBUILDDISABLENODEREUSE = '1' }
if (-not $env:UseSharedCompilation) { $env:UseSharedCompilation = 'false' }
if (-not $env:DOTNET_CLI_USE_MSBUILD_SERVER) { $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0' }

. (Join-Path $PSScriptRoot 'preflight.ps1')

$projectDir = $ProjectRel
if (-not [IO.Path]::IsPathRooted($ProjectRel)) { $projectDir = Join-Path $repo $ProjectRel }
$csproj = Get-ChildItem -LiteralPath $projectDir -Filter '*.csproj' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $csproj) {
    Write-Output ("SETUP failed build no .csproj under {0}" -f ($projectDir -replace '\\', '/'))
    exit 3
}

$dotnet = 'dotnet'
if ($dotnetRoot -and (Test-Path -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe') -PathType Leaf)) { $dotnet = Join-Path $dotnetRoot 'dotnet.exe' }
Write-Output ("SETUP build {0} {1} -c Debug" -f ($dotnet -replace '\\', '/'), ($csproj.FullName -replace '\\', '/'))
$ErrorActionPreference = 'Continue'
$buildOut = & $dotnet build $csproj.FullName -c Debug --nologo 2>&1
$buildCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$buildOut | Select-Object -Last 3 | ForEach-Object { Write-Output ("BUILD {0}" -f $_) }
if ($buildCode -ne 0) {
    Write-Output ("SETUP failed build exit={0}" -f $buildCode)
    exit 3
}

$import = Invoke-KeyrunImportIfMissing -RepoRoot $repo -ProjectRel $projectDir -ImportScript $ImportScript
foreach ($line in $import.Lines) { Write-Output $line }
if (-not $import.Ok) {
    Write-Output 'SETUP failed import'
    exit $import.Code
}

$cacheDir = Join-Path (Join-Path $projectDir '.godot') 'imported'
$files = 0
if (Test-Path -LiteralPath $cacheDir -PathType Container) {
    $files = @(Get-ChildItem -LiteralPath $cacheDir -Recurse -File).Count
}
$state = 'cached'
if ($import.Ran) { $state = 'ran' }
Write-Output ("SETUP done build=ok import={0} files={1}" -f $state, $files)
exit 0
