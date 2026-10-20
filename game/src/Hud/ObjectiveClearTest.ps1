<#
.SYNOPSIS
    Lane fix/target-frame-after-death (P0 wrong data shown to the player): once a run clears, the objective line
    must not claim "No enemies remain" while an enemy is still alive in the sim. The Hollow Warden pull clears when
    the boss dies (its pull spawns only the boss); a Sundered Twin left standing stays alive, frozen by
    RUN-END-FREEZE (sim Run.cs StepRun), and the target frame truthfully shows it ("Sundered Twin (A) 30 / 1030"),
    while ObjectiveModel.Build reported "remaining=0/0 complete" ("No enemies remain") without counting actors.

    Reads the HUD's own proof lines of one autorun and asserts: after the run_end EVENT, the first "HUD objective
    ... complete" line's remaining count equals the enemies alive at run_end (every actor from a "PULL spawn" line
    or a "boss_add_spawn" EVENT, minus every actor with a "death" EVENT before run_end).

    On origin/main 9cc91ec, seed 1 key 2 (the visual audit's run: Twin B dies at 3150, the boss at 3396, run_end at
    3398, Twin A alive at 30 HP) the line reads "remaining=0/0 complete" against 1 alive: RED.

.DESCRIPTION
    -Lines <string[]>  the output lines of one autorun (HudSceneTest passes its first run's lines); failures come
                       back as strings, nothing is printed.
    -LogPath <file>    saved godot stdout of an autorun; prints each failure, then PASS or FAIL, exit 0 or 1.
    (neither)          runs seed 1 key 2 itself (headless, --fixed-fps 60, --autorun --ticks <Ticks>) and checks it.
#>
param(
    [string[]]$Lines,
    [string]$LogPath,
    [int]$Ticks = 3600
)
$ErrorActionPreference = 'Stop'

function Get-ObjectiveClearFailures([string[]]$all) {
    $f = New-Object System.Collections.Generic.List[string]
    $iEnd = -1
    for ($i = 0; $i -lt $all.Count; $i++) {
        if ($all[$i] -match '^EVENT \{.*"type":"run_end"') { $iEnd = $i; break }
    }
    if ($iEnd -lt 0) { $f.Add('objective-clear: no run_end EVENT (the run never ended)'); return $f }
    $spawned = New-Object System.Collections.Generic.HashSet[int]
    $dead = New-Object System.Collections.Generic.HashSet[int]
    for ($i = 0; $i -lt $iEnd; $i++) {
        $line = $all[$i]
        if ($line -match '^PULL spawn index=\d+ id=\S+ actors=(\S+)$') {
            foreach ($pair in $Matches[1].Split(',')) { [void]$spawned.Add([int]$pair.Split(':')[0]) }
        } elseif ($line -match '^EVENT \{"actorId":(\d+),.*"type":"boss_add_spawn"') {
            [void]$spawned.Add([int]$Matches[1])
        } elseif ($line -match '^EVENT \{"actorId":(\d+),.*"type":"death"') {
            [void]$dead.Add([int]$Matches[1])
        }
    }
    $alive = @($spawned | Where-Object { -not $dead.Contains($_) })
    $complete = $null
    for ($i = $iEnd + 1; $i -lt $all.Count; $i++) {
        if ($all[$i] -match '^RUN start ') { break }
        if ($all[$i] -match '^HUD objective .* complete$') { $complete = $all[$i]; break }
    }
    $result = if ($all[$iEnd] -match '"result":"([^"]*)"') { $Matches[1] } else { '' }
    if ($result -eq 'depleted') { return $f }
    if (-not $complete) { $f.Add("objective-clear: the run cleared ($result) but no 'HUD objective ... complete' line follows run_end"); return $f }
    if ($complete -notmatch ' remaining=(\d+)/\d+ complete$') { $f.Add("objective-clear: unparsable complete line '$complete'"); return $f }
    $shown = [int]$Matches[1]
    if ($shown -ne $alive.Count) {
        $f.Add("objective-clear: the cleared objective shows remaining=$shown but $($alive.Count) enemy actor(s) are alive at run_end ($(($alive | Sort-Object) -join ',')): '$complete'")
    }
    return $f
}

if (-not $LogPath -and -not $PSBoundParameters.ContainsKey('Lines')) {
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
    $wrapper = Join-Path $repo 'tools/godot/godot.ps1'
    $guard = (3 * $Ticks) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks --key 2 --seed 1 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rc -eq 2 -and $text -match 'Godot binary not found') { Write-Output 'ObjectiveClearTest: CANNOT RUN (Godot binary not found)'; exit 2 }
    $f = Get-ObjectiveClearFailures @($text -split "`r?`n")
    if ($rc -ne 0) { $f.Add("objective-clear: godot exited $rc (expected 0)") }
} elseif ($LogPath) {
    $f = Get-ObjectiveClearFailures @(Get-Content -LiteralPath $LogPath)
} else {
    return (Get-ObjectiveClearFailures @($Lines))
}
foreach ($x in $f) { Write-Output $x }
if ($f.Count -gt 0) { Write-Output 'ObjectiveClearTest: FAIL'; exit 1 }
Write-Output 'ObjectiveClearTest: PASS'
exit 0
