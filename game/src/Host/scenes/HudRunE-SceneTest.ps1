<#
.SYNOPSIS
  HUD scene run E (checks 17-20, lane 3I): the queued run, solo Warden, as ScreenSceneTest.
  -- --autorun --ticks <QueueTicks> --solo --requeue

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text, the tick
  budget's history and the RED notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunE-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$QueueTicks = 10000
)
$SceneName = 'HudRunE-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

function Invoke-Arena([int]$n, [string[]]$extra) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    $guard = (3 * $n) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $n @extra 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Assert-GodotFound $rc $text
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}

# ---- Run E: the queued run (lane 3I) -----------------------------------------------------------------------------
# The solo Warden wipes in pull 1 and releases, over and over (R-BACKFILL-2), until the timer cap ends the run (run A's
# run, 9600 headless); the screen's autorun script then takes reward 1, steps the key to 3 and queues.
# SimDriver must start the next run at that key with that Clause.
# R1 (BIBLE s8, the key sets the Clause): key 3 is below run.json clauseStartKey 5, so Queue carries clause= and the run clause=none.
$e = Invoke-Arena $QueueTicks @('--solo', '--requeue')
Write-SceneLog 'E' $e.Lines
if ($e.Code -ne 0) { $failures.Add("run E: godot exited $($e.Code) (expected 0)") }
$startsE = @($e.Lines | Where-Object { $_ -match '^RUN start ' })
$queueE = First $e.Lines '^SCREEN queue dungeon=hollow-vault key=3 clause=$'
$requeued = $null
$armedE2 = $null
$pullE2 = $null
if ($startsE.Count -lt 1 -or $startsE[0] -notmatch ' dungeon=hollow-vault key=2 clause=none ') {
    $failures.Add("run E: the first run does not start at key 2 with no Clause: '$($startsE | Select-Object -First 1)'")
}
if (-not $queueE) {
    $failures.Add('run E: no "SCREEN queue dungeon=hollow-vault key=3 clause=" line (the screen did not queue)')
} else {
    # R1: no Clause below run.json clauseStartKey 5; the queued run prints it as clause=none.
    $clauseE = 'none'
    $iQueueE = [array]::IndexOf($e.Lines, $queueE)
    $after = @($e.Lines[($iQueueE + 1)..($e.Lines.Count - 1)])
    $requeued = First $after '^RUN start '
    if (-not $requeued) {
        $failures.Add('run E: no "RUN start" after the queue (the queued run never started)')
    } elseif ($requeued -notmatch " dungeon=hollow-vault key=3 clause=$clauseE ") {
        $failures.Add("run E: the queued run did not start at key 3 with Clause ${clauseE}: '$requeued'")
    } else {
        $iRe = [array]::IndexOf($after, $requeued)
        $rest = @($after[$iRe..($after.Count - 1)])
        $armedE2 = First $rest '^BOSS armed twins=2 lockAt=50\b'
        $pullE2 = First $rest '^PULL spawn index=0 id=choir '
        if (-not $armedE2) { $failures.Add('run E: the queued run is not armed (no second "BOSS armed" line)') }
        if (-not $pullE2) { $failures.Add('run E: the queued run did not spawn pull 1 (no second "PULL spawn index=0 id=choir")') }
        $consumedE = First $rest '^QUEUE consume n=1 size=0$'
        if (-not $consumedE) { $failures.Add('run E: the request that started the queued run is still in the Queue node (no "QUEUE consume n=1 size=0" after it)') }
    }
}

# Run-end titles (lane 3C-d) on run E's lines.
$titlesE = Assert-RunEndTitles $e.Lines

Write-Output '--- run E (queued run), lane 3I ---'
foreach ($l in $startsE + @($queueE, $requeued, $armedE2, $pullE2, $consumedE)) {
    if ($l) { Write-Output $l }
}
Write-RunEndTitles $titlesE

Complete-Scene "queueTicks=$QueueTicks"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
