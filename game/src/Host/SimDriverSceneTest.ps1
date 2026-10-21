<#
.SYNOPSIS
  Headless scene test for SimDriver's fixed tick accumulator (ENGINE-PLAN phase 1, lane 1E).

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless with Godot's --fixed-fps 60, so every
  _Process frame has delta = 1/60 s, and asks the arena to autorun for N sim ticks:

      godot --headless --path game --fixed-fps 60 -- --autorun --ticks 40 --log <file>

  SimDriver steps Keyrun.Sim at 20 Hz (D9). 40 ticks at 20 Hz is 2.0 s of game time, which at
  60 fps is exactly 120 frames. The test asserts, from the engine's own output:
    1. the process exits 0;
    2. the RunLog file holds exactly N "TICK " lines, numbered 0..N-1 in order;
    3. the summary line reports ticks=N frames=3N frame_ms=N*50 (2.0 s of frames for 40 ticks);
    4. the run log holds at least one "ACTOR model=<path|capsule> fallback=<0|1>" line and none with fallback=1
       (finding f-30dcbb08: every actor shows its KayKit model, never the capsule; counted by
       tools/godot/preflight.ps1 Measure-KeyrunActorModels, the same count screens.ps1 gates with exit 9).
  A broken accumulator (two ticks per due frame, a tick every frame, a dropped tick) changes the
  frame count or the tick sequence and turns this red.

  Run from the repo root:  pwsh game/src/Host/SimDriverSceneTest.ps1
  (or: powershell -ExecutionPolicy Bypass -File game/src/Host/SimDriverSceneTest.ps1)
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 40
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$logFile = Join-Path ([IO.Path]::GetTempPath()) ("keyrun-simdriver-test-" + [Guid]::NewGuid().ToString('N') + '.txt')

$failures = New-Object System.Collections.Generic.List[string]

$ErrorActionPreference = 'Continue'
# In-process call, as game.yml's smoke step does, so the quoted '--' reaches Godot intact.
# --quit-after is a hang guard only: a correct run quits by itself at frame 3N, well before it.
$guardFrames = (3 * $Ticks) + 240
$out = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guardFrames '--' --autorun --ticks $Ticks --log $logFile 2>&1 | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

if ($code -eq 2 -and $out -match 'Godot binary not found') {
    Write-Output $out
    Write-Output 'SimDriverSceneTest: CANNOT RUN (Godot binary not found)'
    exit 2
}

if ($code -ne 0) {
    $failures.Add("godot exited $code (expected 0)")
}

$tickLines = @()
if (Test-Path -LiteralPath $logFile) {
    $tickLines = @(Get-Content -LiteralPath $logFile | Where-Object { $_ -match '^TICK ' })
} else {
    $failures.Add("run log not written: $logFile")
}

if ($tickLines.Count -ne $Ticks) {
    $failures.Add("run log has $($tickLines.Count) TICK lines (expected $Ticks)")
}
for ($i = 0; $i -lt [Math]::Min($tickLines.Count, $Ticks); $i++) {
    if ($tickLines[$i] -notmatch "^TICK $i ") {
        $failures.Add("TICK line $i is out of sequence: '$($tickLines[$i])'")
        break
    }
}

# Model gate (finding f-30dcbb08): every bound actor logs 'ACTOR model=<path|capsule> fallback=<0|1>'; CI pins fallback=0.
. (Join-Path $repo 'tools/godot/preflight.ps1')
$models = Measure-KeyrunActorModels -LogPath $logFile
if ($models.Actors -lt 1) {
    $failures.Add("run log has no 'ACTOR model=' line (no actor bound a body)")
}
if ($models.Fallback -gt 0) {
    $failures.Add("$($models.Fallback) of $($models.Actors) actors rendered the capsule fallback (want fallback=0)")
}

$expectFrames = 3 * $Ticks
$expectMs = 50 * $Ticks
$summary = ($out -split "`r?`n") | Where-Object { $_ -match '^AUTORUN done ' } | Select-Object -Last 1
if (-not $summary) {
    $failures.Add('no "AUTORUN done" summary line in engine output')
} elseif ($summary -notmatch "^AUTORUN done ticks=$Ticks frames=$expectFrames frame_ms=$expectMs(\s|$)") {
    $failures.Add("summary mismatch: '$summary' (expected ticks=$Ticks frames=$expectFrames frame_ms=$expectMs)")
}

Write-Output '--- engine output (tail) ---'
($out -split "`r?`n") | Select-Object -Last 12 | ForEach-Object { Write-Output $_ }
Write-Output '--- run log (tail) ---'
$tickLines | Select-Object -Last 3 | ForEach-Object { Write-Output $_ }
Write-Output $models.Lines[0]
if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }

if ($failures.Count -gt 0) {
    Write-Output "SimDriverSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "SimDriverSceneTest: PASS ticks=$Ticks frames=$expectFrames frame_ms=$expectMs"
exit 0
