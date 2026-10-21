<#
.SYNOPSIS
  Tests for the import-cache preflight (Test-KeyrunImportCache in tools/godot/preflight.ps1, finding f-30dcbb08).

.DESCRIPTION
  Never launches Godot and never runs an import. Every case builds a fixture project tree under the temp folder and calls
  the real function on it. A fresh worktree has no game/.godot/imported cache, so every .glb under game/assets stays
  unimported and the models render as capsules; the preflight must refuse that with exit 6 instead.

  Cases:
    I1  one game/assets/x.glb and no .godot folder: Code 6, the single line
        'PREFLIGHT missing-import-cache 1 models unimported; run tools/godot/import.ps1'
    I2  the same model with game/.godot/imported/x.glb-<hash>.scn newer than the .glb: Ok, Code 0, no lines
    I3  the cache file older than its .glb (stale): Code 6, the same line with 1 model
    I4  two models, a cache for only one of them: Code 6 with '... 1 models unimported ...'
    I5  a model in a sub folder (game/assets/sub/z.gltf.glb) with a fresh cache: Ok (the scan is recursive)
    I6  no .glb under game/assets at all (no assets folder): Ok, nothing to import

  Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/import-cache-test.ps1
  Exit 0 = PASS, 1 = FAIL.
#>
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'preflight.ps1')

$work = Join-Path ([IO.Path]::GetTempPath()) ('keyrun-import-cache-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

$failures = New-Object System.Collections.Generic.List[string]

function Check([string]$case, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Output "ok   $case" } else { Write-Output "FAIL $case :: $detail"; $failures.Add("$case :: $detail") }
}

function Write-Text([string]$path, [string[]]$lines) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [IO.File]::WriteAllLines($path, $lines, (New-Object System.Text.UTF8Encoding($false)))
}

function Set-Mtime([string]$path, [DateTime]$mtime) {
    (Get-Item -LiteralPath $path).LastWriteTimeUtc = $mtime
}

# A fixture repo root with game/ as the Godot project (project.godot only; the .NET check is not under test here).
function New-Fixture([string]$tag) {
    $root = Join-Path $work $tag
    $proj = Join-Path $root 'game'
    New-Item -ItemType Directory -Path $proj -Force | Out-Null
    Write-Text (Join-Path $proj 'project.godot') @('config_version=5')
    return $root
}

function Add-Model([string]$root, [string]$rel, [DateTime]$mtime) {
    $f = Join-Path $root ('game\' + $rel)
    Write-Text $f @('glTF')
    Set-Mtime $f $mtime
}

function Add-Cache([string]$root, [string]$name, [DateTime]$mtime) {
    $f = Join-Path $root ('game\.godot\imported\' + $name)
    Write-Text $f @('SCN')
    Set-Mtime $f $mtime
}

$past = [DateTime]::Parse('2020-06-01T00:00:00Z').ToUniversalTime()
$newer = [DateTime]::Parse('2030-01-01T00:00:00Z').ToUniversalTime()
$line1 = 'PREFLIGHT missing-import-cache 1 models unimported; run tools/godot/import.ps1'

try {
    # I1: a model and no cache at all.
    $root = New-Fixture 'i1'
    Add-Model $root 'assets\x.glb' $past
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I1.missing-cache-code-6' ((-not $r.Ok) -and $r.Code -eq 6) "Ok=$($r.Ok) Code=$($r.Code)"
    Check 'I1.missing-cache-exact-line' ($r.Lines.Count -eq 1 -and $r.Lines[0] -eq $line1) "lines=[$($r.Lines -join ' | ')]"

    # I2: the same model with a fresh cache file.
    $root = New-Fixture 'i2'
    Add-Model $root 'assets\x.glb' $past
    Add-Cache $root 'x.glb-0123456789abcdef0123456789abcdef.scn' $newer
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I2.fresh-cache-ok' ($r.Ok -and $r.Code -eq 0 -and $r.Lines.Count -eq 0) "Ok=$($r.Ok) Code=$($r.Code) lines=[$($r.Lines -join ' | ')]"

    # I3: a cache file older than its model is stale and counts as unimported.
    $root = New-Fixture 'i3'
    Add-Model $root 'assets\x.glb' $newer
    Add-Cache $root 'x.glb-0123456789abcdef0123456789abcdef.scn' $past
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I3.stale-cache-code-6' ((-not $r.Ok) -and $r.Code -eq 6) "Ok=$($r.Ok) Code=$($r.Code)"
    Check 'I3.stale-cache-exact-line' ($r.Lines.Count -eq 1 -and $r.Lines[0] -eq $line1) "lines=[$($r.Lines -join ' | ')]"

    # I4: two models, a cache for only one of them.
    $root = New-Fixture 'i4'
    Add-Model $root 'assets\a.glb' $past
    Add-Model $root 'assets\b.glb' $past
    Add-Cache $root 'a.glb-0123456789abcdef0123456789abcdef.scn' $newer
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I4.partial-cache-counts-missing' ((-not $r.Ok) -and $r.Code -eq 6 -and $r.Lines.Count -eq 1 -and $r.Lines[0] -eq $line1) "Ok=$($r.Ok) Code=$($r.Code) lines=[$($r.Lines -join ' | ')]"

    # I5: a sub-folder model with a double extension and a fresh cache is found (recursive scan).
    $root = New-Fixture 'i5'
    Add-Model $root 'assets\sub\z.gltf.glb' $past
    Add-Cache $root 'z.gltf.glb-0123456789abcdef0123456789abcdef.scn' $newer
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I5.subfolder-double-extension-ok' ($r.Ok -and $r.Code -eq 0) "Ok=$($r.Ok) Code=$($r.Code) lines=[$($r.Lines -join ' | ')]"

    # I6: no models at all.
    $root = New-Fixture 'i6'
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I6.no-models-ok' ($r.Ok -and $r.Code -eq 0) "Ok=$($r.Ok) Code=$($r.Code) lines=[$($r.Lines -join ' | ')]"
} catch {
    Check 'import-cache.exception' $false $_.Exception.Message
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Output ("import-cache-test: FAIL ({0})" -f $failures.Count)
    exit 1
}
Write-Output 'import-cache-test: PASS'
exit 0
