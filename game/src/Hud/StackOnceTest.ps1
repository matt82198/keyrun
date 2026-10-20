<#
.SYNOPSIS
    Lane HUD-STACK-ONCE (Matt 2026-10-19, "reduce screen clutter"; orchestrator frames visual-audit/bytick-42d5). After
    NAMEPLATE-DECLUTTER (#251) the SPREAD call draws its banner only, but the STACK call still drew its sentence twice
    at once: the callout banner "STACK: group up  N.Ns" AND a world label "STACK: group up" over the spot. This test
    reads the HUD's own proof lines of one three-seat party autorun (the run NameplateDeclutterTest reads):

      s. one STACK sentence: at least one "HUD pingtext label=STACK banner=B world=W" line, and on every one B is 1 and
         W is 0 (the sentence "STACK: group up" is drawn by the banner alone);
      m. the spot is still marked: at least one "HUD pingmarker label=STACK world=M" line with M >= 1 (the world keeps
         a marker on the spot to group up on, without the sentence).

    On origin/main 3e072d6 s and m fail (RED): every STACK line reads banner=1 world=1, and no marker line exists.

    Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Hud/StackOnceTest.ps1
    Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 7000
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

$guard = (3 * $Ticks) + 240
$ErrorActionPreference = 'Continue'
$text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks 2>&1 | Out-String
$rc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($rc -eq 2 -and $text -match 'Godot binary not found') { Write-Output 'StackOnceTest: CANNOT RUN (Godot binary not found)'; exit 2 }
if ($rc -ne 0) { $failures.Add("godot exited $rc (expected 0)") }
$all = @($text -split "`r?`n")

# s. one STACK sentence.
$stack = @($all | Where-Object { $_ -match '^HUD pingtext label=STACK banner=\d+ world=\d+$' })
if ($stack.Count -eq 0) { $failures.Add('s: no "HUD pingtext label=STACK" line (the STACK call never showed)') }
foreach ($l in $stack) {
    if ($l -notmatch ' banner=1 world=0$') { $failures.Add("s: the STACK sentence is not drawn by the banner alone: '$l'") }
}

# m. the spot keeps a marker.
$markers = @($all | Where-Object { $_ -match '^HUD pingmarker label=STACK world=\d+$' })
$marked = $markers | Where-Object { $_ -match ' world=([1-9]|\d\d+)$' } | Select-Object -First 1
if (-not $marked) { $failures.Add('m: no "HUD pingmarker label=STACK world>=1" line (the stack spot lost its world marker)') }

Write-Output '--- asserted lines ---'
foreach ($l in @($stack | Select-Object -First 3) + @($markers | Select-Object -First 3)) {
    if ($l) { Write-Output $l }
}
if ($failures.Count -gt 0) {
    Write-Output "StackOnceTest: FAIL ($($failures.Count))"
    $failures | Select-Object -First 8 | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "StackOnceTest: PASS ticks=$Ticks stackLines=$($stack.Count) markerLines=$($markers.Count)"
exit 0
