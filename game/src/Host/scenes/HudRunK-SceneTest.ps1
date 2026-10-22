<#
.SYNOPSIS
  HUD scene run K (checks 30-32, lane 3E-b): a recorded input log replays through the real input path; interactive,
  three-seat party, from pull 4.
  K1  -- --record-input <log> --press-enter 3 --press-ping 2 --pull 4 --log <k1>   (--quit-after <RecordFrames>)
  K2  -- --replay-input <log> --pull 4 --log <k2>                                  (--quit-after <RecordFrames>)

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text and the RED
  notes are in README.md beside this file. -KeepK1Record PATH keeps a copy of K1's recording. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunK-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$RecordFrames = 6600,
    [string]$KeepK1Record = ''
)
$SceneName = 'HudRunK-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

# ---- Run K: record an interactive run's keys, replay them through the real input path (lane 3E-b) -----------------
$kTag = [Guid]::NewGuid().ToString('N')
$kTemp = [IO.Path]::GetTempPath()
$recPath = Join-Path $kTemp "keyrun-hud-record-$kTag.input-log.json"
$k1LogPath = Join-Path $kTemp "keyrun-hud-k1-$kTag.txt"
$k2LogPath = Join-Path $kTemp "keyrun-hud-k2-$kTag.txt"
$examplePath = Join-Path $repo 'game/fixtures/input/example-enter-ping.input-log.json'

# Marker lines of a --log file, each as "<tick>|<line>" (tick = the last TICK line before it; none yet = -1).
function Get-Markers([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    $tick = -1
    $out = @()
    foreach ($l in [IO.File]::ReadAllLines($path)) {
        if ($l -match '^TICK (\d+) ') { $tick = [int]$Matches[1]; continue }
        if ($l -match '^(RUN start |SCREEN reopen |PING routed )' -or $l -match '^EVENT \{.*"type":"run_end"') { $out += "$tick|$l" }
    }
    return $out
}

$ErrorActionPreference = 'Continue'
$k1Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $RecordFrames '--' --record-input $recPath --press-enter 3 --press-ping 2 --pull 4 --log $k1LogPath 2>&1 | Out-String
$k1Code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $k1Code $k1Text
$k1Lines = @($k1Text -split "`r?`n")
Write-SceneLog 'K1' $k1Lines
if ($k1Code -ne 0) { $failures.Add("run K1: godot exited $k1Code (expected 0)") }
$recDone = First $k1Lines '^INPUT record done events=\d+ path=.+$'
$recEvents = @()
$recN = -1
if (-not $recDone) {
    $failures.Add("run K1: no 'INPUT record done events=N path=P' line (--record-input wrote no input log)")
} else {
    $recN = [int]($recDone -replace '^INPUT record done events=(\d+) .*$', '$1')
    if ($recN -lt 2) { $failures.Add("run K1: the input log holds $recN events (want >= 2: the Enters and the G)") }
    if (-not (Test-Path -LiteralPath $recPath)) {
        $failures.Add("run K1: '$recDone' but no file at $recPath")
    } else {
        $rec = Get-Content -Raw -LiteralPath $recPath | ConvertFrom-Json
        if ($rec.schema -ne 'keyrun.input-log/1') { $failures.Add("run K1: input log schema is '$($rec.schema)' (want keyrun.input-log/1)") }
        if ($rec.seed -ne 1 -or $rec.key -ne 2) { $failures.Add("run K1: input log header seed=$($rec.seed) key=$($rec.key) (want seed=1 key=2)") }
        $recEvents = @($rec.events)
        if ($recEvents.Count -ne $recN) { $failures.Add("run K1: the log line says $recN events, the file holds $($recEvents.Count)") }
        for ($i = 1; $i -lt $recEvents.Count; $i++) {
            if ([int]$recEvents[$i].tick -lt [int]$recEvents[$i - 1].tick) { $failures.Add("run K1: event $i tick $($recEvents[$i].tick) is before event $($i - 1)'s $($recEvents[$i - 1].tick)"); break }
        }
        if (@($recEvents | Where-Object { $_.key -eq 'Enter' -and $_.pressed }).Count -ne 3) { $failures.Add('run K1: the input log does not hold the three scripted Enter presses') }
        if (@($recEvents | Where-Object { $_.key -eq 'G' -and $_.pressed }).Count -ne 1) { $failures.Add('run K1: the input log does not hold the scripted G press') }
        # HOST-HEADING: a plain recording carries one "startHeading" header line the committed example predates; it
        # is asserted here and left out of the byte comparison (the fixture is not regenerated).
        $kHeading = Get-StartHeadingText $recPath
        if ($null -eq $kHeading) { $failures.Add("run K1: the input log has no `"startHeading`" header line") }
        if ($null -eq (Get-RotationIndexText $recPath)) { $failures.Add("run K1: the input log has no `"rotationIndex`" header line (the weekly affix rotation is not recorded)") }
        $recText = Remove-RotationIndexLine (([IO.File]::ReadAllText($recPath)) -replace '(?m)^  "startHeading": [^,\r\n]+,\n', '')
        if (-not (Test-Path -LiteralPath $examplePath)) {
            $failures.Add("run K1: no committed example at $examplePath")
        } elseif ($recText -cne [IO.File]::ReadAllText($examplePath)) {
            $failures.Add("run K1: the recorded log differs from the committed example $examplePath (regenerate it from run K1 with a stated reason)")
            # Name the difference, line by line, so a CI log shows the field (R-CLOCK-2: the comparison itself is unchanged).
            Write-Host 'K1 diff:'
            $k1Want = @([IO.File]::ReadAllText($examplePath) -split "`n")
            $k1Got = @($recText -split "`n")
            $k1Max = [Math]::Max($k1Want.Count, $k1Got.Count)
            for ($k1i = 0; $k1i -lt $k1Max; $k1i++) {
                $w = if ($k1i -lt $k1Want.Count) { $k1Want[$k1i] } else { '<missing>' }
                $g = if ($k1i -lt $k1Got.Count) { $k1Got[$k1i] } else { '<missing>' }
                if ($w -cne $g) {
                    Write-Host ("K1 diff line {0} expected: {1}" -f ($k1i + 1), ($w -replace "`r", '<CR>'))
                    Write-Host ("K1 diff line {0} actual:   {1}" -f ($k1i + 1), ($g -replace "`r", '<CR>'))
                }
            }
        }
    }
}

$ErrorActionPreference = 'Continue'
$k2Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $RecordFrames '--' --replay-input $recPath --pull 4 --log $k2LogPath 2>&1 | Out-String
$k2Code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $k2Code $k2Text
$k2Lines = @($k2Text -split "`r?`n")
Write-SceneLog 'K2' $k2Lines
if ($k2Code -ne 0) { $failures.Add("run K2: godot exited $k2Code (expected 0)") }
$replayed = @($k2Lines | Where-Object { $_ -match '^INPUT replay tick=\d+ key=\S+ pressed=(true|false)$' })
$replayDone = First $k2Lines '^INPUT replay done events=\d+ last=\d+$'
$scriptedK2 = First $k2Lines '^INPUT (enter|ping) '
if ($scriptedK2) { $failures.Add("run K2: a scripted press ran during the replay: '$scriptedK2'") }
if ($recEvents.Count -gt 0) {
    $want = @($recEvents | ForEach-Object { "INPUT replay tick=$($_.tick) key=$($_.key) pressed=$(([string]$_.pressed).ToLowerInvariant())" })
    if ($replayed.Count -ne $want.Count) {
        $failures.Add("run K2: $($replayed.Count) 'INPUT replay tick=' lines for $($want.Count) recorded events")
    } else {
        for ($i = 0; $i -lt $want.Count; $i++) {
            if ($replayed[$i] -ne $want[$i]) { $failures.Add("run K2: replay line $i is '$($replayed[$i])' (want '$($want[$i])')"); break }
        }
    }
    $wantDone = "INPUT replay done events=$($recEvents.Count) last=$($recEvents[$recEvents.Count - 1].tick)"
    if ($replayDone -ne $wantDone) { $failures.Add("run K2: replay summary '$replayDone' (want '$wantDone')") }
}
$m1 = @(Get-Markers $k1LogPath)
$m2 = @(Get-Markers $k2LogPath)
if (@($m1 | Where-Object { $_ -match '\|SCREEN reopen ' }).Count -lt 1 -or @($m1 | Where-Object { $_ -match '\|PING routed ' }).Count -lt 1) {
    $failures.Add("run K1: the run log has no 'SCREEN reopen' and 'PING routed' markers to compare ($($m1.Count) markers)")
} elseif ($m2.Count -lt $m1.Count) {
    $failures.Add("run K2: the replayed run log has $($m2.Count) markers, K1's has $($m1.Count) (first K1 marker: '$($m1[0])')")
} else {
    for ($i = 0; $i -lt $m1.Count; $i++) {
        if ($m2[$i] -ne $m1[$i]) { $failures.Add("run K2: marker $i is '$($m2[$i])', K1 had '$($m1[$i])' (same line at the same tick expected)"); break }
    }
}
# -KeepK1Record PATH: keep a copy of K1's recording so the committed example can be regenerated from a real run.
if ($KeepK1Record -and (Test-Path -LiteralPath $recPath)) { Copy-Item -LiteralPath $recPath -Destination $KeepK1Record }
foreach ($p in @($recPath, $k1LogPath, $k2LogPath)) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p } }

Write-Output '--- run K (record -> replay), lane 3E-b ---'
foreach ($l in @($recDone) + $replayed + @($replayDone) + $m1) {
    if ($l) { Write-Output $l }
}

Complete-Scene "recordFrames=$RecordFrames recordEvents=$recN"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
