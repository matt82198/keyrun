<#
.SYNOPSIS
    Lane PLATE-RECTS (finding f-9d4a31a5). Reads the HUD's own proof lines of one three-seat party autorun at the
    project's 1280x720 viewport and asserts a floating damage number never sits on a name plate where LabelView
    actually draws it (LabelView.PlateRects, after the same-name crowd fold and the separation):

      a. at least one "HUD numbers real target=T number=X,Y,W,H plates=P stacked=S real_hit=0|1" line with S >= 2 (the
         number's target is folded into a stacked crowd plate, "Paired Warrior x2", so the stacked case is exercised);
      b. on every such line real_hit=0 (the placed number shares no pixel with any drawn plate).

    On the DMG-NUMBERS branch (PR 252, 0f3b2568) with only this lane's LabelView.PlateRects and proof line added,
    CombatText still placed numbers against its own copy of the plate layout (which never folds a crowd): 27 of 155
    lines had real_hit=1, every one stacked=2, so b fails (RED).

.DESCRIPTION
    Three ways in:
      -Lines <string[]>   the output lines of one autorun; the failures come back as strings, nothing is printed.
      -LogPath <file>     saved godot stdout of an autorun; prints each failure, then PASS or FAIL, exit 0 or 1.
      (neither)           runs the autorun itself (headless, --fixed-fps 60, --autorun --ticks <Ticks> --view-size
                          1280x720) and checks it.
    Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [string[]]$Lines,
    [string]$LogPath,
    [int]$Ticks = 7000
)
$ErrorActionPreference = 'Stop'

function Get-PlateRectFailures([string[]]$all) {
    $out = New-Object System.Collections.Generic.List[string]
    $real = @($all | Where-Object { $_ -match '^HUD numbers real ' })
    if ($real.Count -eq 0) { $out.Add('plate-rects: no "HUD numbers real" line (no number was checked against the drawn plates)') }
    $stacked = 0
    foreach ($l in $real) {
        if ($l -notmatch '^HUD numbers real target=\d+ number=-?\d+,-?\d+,\d+,\d+ plates=\d+ stacked=(\d+) real_hit=([01])$') {
            $out.Add("plate-rects: malformed line: '$l'")
            continue
        }
        if ([int]$Matches[1] -ge 2) { $stacked++ }
        if ($Matches[2] -ne '0') { $out.Add("plate-rects b: a damage number sits on a drawn name plate: '$l'") }
    }
    if ($real.Count -gt 0 -and $stacked -eq 0) {
        $out.Add('plate-rects a: no number on a stacked crowd plate (stacked>=2), so the stacked case is not exercised')
    }
    return , $out
}

if (-not $LogPath -and -not $PSBoundParameters.ContainsKey('Lines')) {
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
    $wrapper = Join-Path $repo 'tools/godot/godot.ps1'
    $guard = (3 * $Ticks) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks --view-size 1280x720 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rc -eq 2 -and $text -match 'Godot binary not found') { Write-Output 'CombatTextPlateRectsTest: CANNOT RUN (Godot binary not found)'; exit 2 }
    $f = Get-PlateRectFailures @($text -split "`r?`n")
    if ($rc -ne 0) { $f.Add("plate-rects: godot exited $rc (expected 0)") }
} elseif ($LogPath) {
    $f = Get-PlateRectFailures @(Get-Content -LiteralPath $LogPath)
} else {
    return (Get-PlateRectFailures @($Lines))
}
$count = @($f).Count
foreach ($x in $f) { Write-Output $x }
if ($count -gt 0) { Write-Output 'CombatTextPlateRectsTest: FAIL'; exit 1 }
Write-Output 'CombatTextPlateRectsTest: PASS'
exit 0
