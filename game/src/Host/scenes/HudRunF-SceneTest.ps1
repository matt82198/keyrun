<#
.SYNOPSIS
  HUD scene run F (checks 21-23, lane 3J): backfill in the real loop; three-seat party, key 2, no --requeue.
  -- --autorun --ticks <BackfillTicks> --abandon-seat <AbandonSeat> --joiner-key 2

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text, the abandon
  default's history and the RED notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunF-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    # R-K4-4/R-K4-13: the kit moves the hp10 anchor to tick 1266 and pull-0 clear to 1450; +1 stalls pull 0 so the party
    # never reaches the pair pull. RunFJoinerActsTests.cs:43 pins +50 (pull 1 at 1864).
    [int]$BackfillTicks = 3500,
    [string]$AbandonSeat = '2@pull0.hp10+50'
)
$SceneName = 'HudRunF-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

function Invoke-Arena([int]$n, [string[]]$extra) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    $guard = (3 * $n) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $n --rotation 0 @extra 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Assert-GodotFound $rc $text
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}
. (Join-Path $PSScriptRoot 'HudBackfillCommon.ps1')

$f = Invoke-Backfill 'F' 2
if ($f.Pull) {
    $refillF = $null
    for ($i = $f.IPull + 1; $i -lt $f.Lines.Count; $i++) {
        if ($f.Lines[$i] -match '^TICK ') { break }
        if ($f.Lines[$i] -match '^SEAT refill ') { $refillF = $f.Lines[$i]; break }
    }
    $stepTick = $null
    for ($i = $f.IPull - 1; $i -ge 0; $i--) {
        if ($f.Lines[$i] -match '^TICK (\d+) ') { $stepTick = [int]$Matches[1]; break }
    }
    $wantFill = if ($null -ne $stepTick) { $stepTick + 1 } else { -1 }
    if (-not $refillF) {
        $failures.Add("run F: no 'SEAT refill' line in the step that spawned pull 2 (first refill line: '$($f.Refill)')")
    } elseif ($refillF -notmatch "^SEAT refill actor=$seatActor tick=$wantFill key=2$") {
        $failures.Add("run F: wrong refill line: '$refillF' (want 'SEAT refill actor=$seatActor tick=$wantFill key=2')")
    } else {
        $iRefillF = [array]::IndexOf($f.Lines, $refillF)
        $consumeF = First @($f.Lines[$iRefillF..($f.Lines.Count - 1)]) '^QUEUE consume n=1 size=0$'
        if (-not $consumeF) { $failures.Add('run F: the joiner was not consumed from the Queue node (no "QUEUE consume n=1 size=0" after the refill)') }
        if ($f.Abandon) {
            $iAbandonF = [array]::IndexOf($f.Lines, $f.Abandon)
            $idle = @($f.Lines[$iAbandonF..$iRefillF] | Where-Object { $_ -match "^EVENT \{`"abilityId`":`"[^`"]+`",`"actorId`":$seatActor,.*`"type`":`"cast_complete`"" })
            if ($idle.Count -gt 0) { $failures.Add("run F: the open seat acted before the refill: '$($idle[0])'") }
        }
        $joinerActs = First @($f.Lines[$iRefillF..($f.Lines.Count - 1)]) "^EVENT \{`"abilityId`":`"[^`"]+`",`"actorId`":$seatActor,.*`"type`":`"cast_complete`""
        if (-not $joinerActs) { $failures.Add('run F: the refilled seat never acted after the refill (the joiner''s commands are dropped)') }
    }
    $deadEarly = @($f.Lines[0..$f.IPull] | Where-Object { $_ -match '^EVENT \{"actorId":[123],.*"type":"death"' })
    if ($deadEarly.Count -gt 0) { $failures.Add("run F: a party seat died before pull 2 (pick an abandon the party survives): '$($deadEarly[0])'") }
}

# Run-end titles (lane 3C-d) on run F's lines.
$titlesF = Assert-RunEndTitles $f.Lines

Write-Output '--- run F (backfill), lane 3J ---'
foreach ($l in @($f.Abandon, $f.Add, $f.Pull, $refillF, $consumeF, $joinerActs)) {
    if ($l) { Write-Output $l }
}
Write-RunEndTitles $titlesF

Complete-Scene "backfillTicks=$BackfillTicks abandonSeat=$AbandonSeat"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
