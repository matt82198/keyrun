<#
.SYNOPSIS
  HUD scene run L (checks 33-35, lane 3E-c): the camera yaw a human sets is recorded beside the keys and replayed;
  interactive, three-seat party.
  L1  -- --replay-input <walk> --record-input <log> --press-yaw <YawAt>:<YawTo> --log <l1>   (--quit-after <YawFrames>)
  L2  -- --replay-input <log> --log <l2>                                                     (--quit-after <YawFrames>)

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text and the RED
  notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunL-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$YawAt = 60,
    [string]$YawTo = '1.5',
    [int]$YawFrames = 900
)
$SceneName = 'HudRunL-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

# ---- Run L: the camera yaw is recorded beside the keys and replayed through the rig movement reads (lane 3E-c) -------
$kTemp = [IO.Path]::GetTempPath()
$lTag = [Guid]::NewGuid().ToString('N')
$walkPath = Join-Path $kTemp "keyrun-hud-walk-$lTag.input-log.json"
$lRecPath = Join-Path $kTemp "keyrun-hud-yaw-$lTag.input-log.json"
$l1LogPath = Join-Path $kTemp "keyrun-hud-l1-$lTag.txt"
$l2LogPath = Join-Path $kTemp "keyrun-hud-l2-$lTag.txt"
$yawExamplePath = Join-Path $repo 'game/fixtures/input/example-yaw-walk.input-log.json'
# L1's held key comes through the existing replay seam: Enter at tick 0 (the screen's Queue starts the run), W from tick 1.
$walkJson = "{`n  `"schema`": `"keyrun.input-log/1`",`n  `"seed`": 1,`n  `"key`": 2,`n  `"events`": [`n" +
    "    { `"tick`": 0, `"key`": `"Enter`", `"pressed`": true },`n    { `"tick`": 1, `"key`": `"W`", `"pressed`": true }`n  ]`n}`n"
[IO.File]::WriteAllText($walkPath, $walkJson, (New-Object System.Text.UTF8Encoding($false)))

# "TICK n warden=(x,z)" lines of a --log file by tick (the first run's line for each tick).
function Get-WardenTicks([string]$path) {
    $out = New-Object 'System.Collections.Generic.Dictionary[int,string]'
    if (-not (Test-Path -LiteralPath $path)) { return ,$out }
    foreach ($l in [IO.File]::ReadAllLines($path)) {
        if ($l -match '^(TICK (\d+) warden=\((-?\d+),(-?\d+)\))' -and -not $out.ContainsKey([int]$Matches[2])) { $out[[int]$Matches[2]] = $Matches[1] }
    }
    return ,$out
}

$ErrorActionPreference = 'Continue'
$l1Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $YawFrames '--' --replay-input $walkPath --record-input $lRecPath --press-yaw "${YawAt}:$YawTo" --log $l1LogPath 2>&1 | Out-String
$l1Code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $l1Code $l1Text
$l1Lines = @($l1Text -split "`r?`n")
Write-SceneLog 'L1' $l1Lines
if ($l1Code -ne 0) { $failures.Add("run L1: godot exited $l1Code (expected 0)") }
$yawL1 = First $l1Lines '^INPUT yaw tick=\d+ yaw=\S+$'
if (-not $yawL1) { $failures.Add("run L1: no 'INPUT yaw tick=T yaw=Y' line (the --press-yaw seam never turned the camera)") }
$recDoneL = First $l1Lines '^INPUT record done events=\d+ cam=\d+ path=.+$'
$lEvents = @()
$camEvents = @()
$yawTick = -1
if (-not $recDoneL) {
    $failures.Add("run L1: no 'INPUT record done events=N cam=M path=P' line (the camera turn was not recorded)")
} else {
    $camN = [int]($recDoneL -replace '^INPUT record done events=\d+ cam=(\d+) .*$', '$1')
    if ($camN -lt 1) { $failures.Add("run L1: the input log holds $camN camera events (want >= 1: the scripted yaw)") }
    if (-not (Test-Path -LiteralPath $lRecPath)) {
        $failures.Add("run L1: '$recDoneL' but no file at $lRecPath")
    } else {
        $recL = Get-Content -Raw -LiteralPath $lRecPath | ConvertFrom-Json
        if ($recL.schema -ne 'keyrun.input-log/2') { $failures.Add("run L1: input log schema is '$($recL.schema)' (want keyrun.input-log/2)") }
        if ($recL.seed -ne 1 -or $recL.key -ne 2) { $failures.Add("run L1: input log header seed=$($recL.seed) key=$($recL.key) (want seed=1 key=2)") }
        $lEvents = @($recL.events)
        $camEvents = @($recL.camera)
        if ($camEvents.Count -ne $camN) { $failures.Add("run L1: the log line says $camN camera events, the file holds $($camEvents.Count)") }
        if ($camEvents.Count -gt 0) {
            $yawTick = [int]$camEvents[0].tick
            if ($yawTick -lt $YawAt) { $failures.Add("run L1: the first camera event is at tick $yawTick, before the scripted yaw at $YawAt") }
        }
        if (@($lEvents | Where-Object { $_.key -eq 'W' -and $_.pressed }).Count -ne 1) { $failures.Add('run L1: the input log does not hold the replayed W press') }
        if (-not (Test-Path -LiteralPath $yawExamplePath)) {
            $failures.Add("run L1: no committed example at $yawExamplePath")
        } elseif ((Get-RotationIndexText $lRecPath) -ne '0') {
            # L1 replays <walk>, which has no rotationIndex, so the run is built at 0 and records 0, never this week's.
            $failures.Add("run L1: the input log's rotationIndex is '$(Get-RotationIndexText $lRecPath)' (want 0: the replayed log's, not today's week)")
        } elseif ((Remove-RotationIndexLine ([IO.File]::ReadAllText($lRecPath))) -cne [IO.File]::ReadAllText($yawExamplePath)) {
            $failures.Add("run L1: the recorded log differs from the committed example $yawExamplePath (regenerate it from run L1 with a stated reason)")
        }
    }
}
$w1 = Get-WardenTicks $l1LogPath
$turnL1 = $null
if ($yawTick -ge 0) {
    if (-not $w1.ContainsKey($yawTick) -or -not $w1.ContainsKey($yawTick + 20)) {
        $failures.Add("run L1: the run log has no TICK $yawTick and TICK $($yawTick + 20) warden lines around the yaw event")
    } else {
        $x0 = [int]($w1[$yawTick] -replace '^TICK \d+ warden=\((-?\d+),.*$', '$1')
        $x20 = [int]($w1[$yawTick + 20] -replace '^TICK \d+ warden=\((-?\d+),.*$', '$1')
        $turnL1 = "$($w1[$yawTick]) -> $($w1[$yawTick + 20])"
        if ($x20 -ge ($x0 - 100)) { $failures.Add("run L1: the Warden's x went $x0 -> $x20 in the 20 ticks after the yaw (W did not walk along the turned camera)") }
    }
}

$ErrorActionPreference = 'Continue'
$l2Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $YawFrames '--' --replay-input $lRecPath --log $l2LogPath 2>&1 | Out-String
$l2Code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $l2Code $l2Text
$l2Lines = @($l2Text -split "`r?`n")
Write-SceneLog 'L2' $l2Lines
if ($l2Code -ne 0) { $failures.Add("run L2: godot exited $l2Code (expected 0)") }
$scriptedL2 = First $l2Lines '^INPUT (enter|ping|yaw) '
if ($scriptedL2) { $failures.Add("run L2: a scripted press ran during the replay: '$scriptedL2'") }
$camReplayed = @($l2Lines | Where-Object { $_ -match '^INPUT replay cam tick=\d+ yaw=\S+$' })
$replayDoneL = First $l2Lines '^INPUT replay done events=\d+ last=-?\d+ cam=\d+$'
if ($camEvents.Count -gt 0) {
    if ($camReplayed.Count -ne $camEvents.Count) {
        $failures.Add("run L2: $($camReplayed.Count) 'INPUT replay cam' lines for $($camEvents.Count) recorded camera events")
    } else {
        for ($i = 0; $i -lt $camEvents.Count; $i++) {
            $null = $camReplayed[$i] -match '^INPUT replay cam tick=(\d+) yaw=(\S+)$'
            if ([int]$Matches[1] -ne [int]$camEvents[$i].tick -or [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture) -ne [double]$camEvents[$i].yaw) {
                $failures.Add("run L2: camera replay line $i is '$($camReplayed[$i])' (want tick=$($camEvents[$i].tick) yaw=$($camEvents[$i].yaw))"); break
            }
        }
    }
    $wantDoneL = "INPUT replay done events=$($lEvents.Count) last=$($lEvents[$lEvents.Count - 1].tick) cam=$($camEvents.Count)"
    if ($replayDoneL -ne $wantDoneL) { $failures.Add("run L2: replay summary '$replayDoneL' (want '$wantDoneL')") }
}
$w2 = Get-WardenTicks $l2LogPath
$comparedL = 0
if ($yawTick -ge 0) {
    foreach ($t in @($w1.Keys | Where-Object { $_ -ge $yawTick } | Sort-Object)) {
        if (-not $w2.ContainsKey($t)) { $failures.Add("run L2: the replayed run log has no TICK $t warden line (L1: '$($w1[$t])')"); break }
        if ($w2[$t] -ne $w1[$t]) { $failures.Add("run L2: '$($w2[$t])' but L1 had '$($w1[$t])' (the replayed yaw is not the recorded one)"); break }
        $comparedL++
    }
    if ($comparedL -lt 20) { $failures.Add("run L2: only $comparedL TICK lines at or after the yaw event's tick $yawTick were compared (want >= 20)") }
}
foreach ($p in @($walkPath, $lRecPath, $l1LogPath, $l2LogPath)) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p } }

Write-Output '--- run L (camera yaw record -> replay), lane 3E-c ---'
foreach ($l in @($yawL1, $recDoneL, $turnL1) + $camReplayed + @($replayDoneL, "compared=$comparedL")) {
    if ($l) { Write-Output $l }
}

Complete-Scene "yawAt=$YawAt yawTo=$YawTo yawFrames=$YawFrames yawCompared=$comparedL"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
