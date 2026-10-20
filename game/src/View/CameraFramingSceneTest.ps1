<#
.SYNOPSIS
  Headless scene test for CAMERA-FRAMING (Matt 2026-10-20 "it should feel almost exact to wow with less button
  clutter"; demo verdict KF1: the camera drew the fight about 60 px tall in a 720 px frame).

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 and replays the deterministic run C from
  the Entrance into the Choir pull at the shipped 1280x720:

      godot --headless --path game --fixed-fps 60 -- --autorun --ticks N --view-size 1280x720

  src/View/CameraRig.cs prints, every 60 frames, on stdout:
    CAMERA framing frame=F player=P zoom=Z boom=B pitch=R fov=V
  where P is the Warden's drawn height measured on the real Camera3D (UnprojectPosition of its feet and of
  CameraFraming.PlayerHeightMm above them), permille of the viewport height, or -1 when a point is behind the camera.
  This test asserts:
    1. the process exits 0 and prints "AUTORUN done ticks=N";
    2. at least 20 framing lines, every one at the default zoom (CameraFraming.DefaultBoomMm 5000) and fov 59;
    3. every measured P is between 200 and 350 (the player a fifth to a third of the frame, like WoW's default), and
       at least 90 percent of the lines are measured (P is not -1).
  Before CAMERA-FRAMING the rig printed no framing line, and the camera (7.5 m boom grown up to 12 m to fit the party,
  pitch -0.42, fov 70) drew the Warden 92..144 permille tall (Keyrun.Content.Tests/CameraFramingTests.cs) (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/View/CameraFramingSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 600
)
$ErrorActionPreference = 'Stop'

$MinPermille = 200
$MaxPermille = 350
$DefaultZoomMm = 5000
$Fov = '59.0'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

$failures = New-Object System.Collections.Generic.List[string]

$ErrorActionPreference = 'Continue'
# --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
$guardFrames = (3 * $Ticks) + 240
$out = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guardFrames '--' --autorun --ticks $Ticks --view-size 1280x720 2>&1 | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

if ($code -eq 2 -and $out -match 'Godot binary not found') {
    Write-Output $out
    Write-Output 'CameraFramingSceneTest: CANNOT RUN (Godot binary not found)'
    exit 2
}
if ($code -ne 0) {
    $failures.Add("godot exited $code (expected 0)")
}

$outLines = @($out -split "`r?`n")
$summary = $outLines | Where-Object { $_ -match '^AUTORUN done ' } | Select-Object -Last 1
if (-not $summary -or $summary -notmatch "^AUTORUN done ticks=$Ticks ") {
    $failures.Add("no 'AUTORUN done ticks=$Ticks' summary (got '$summary')")
}

$framing = @($outLines | Where-Object { $_ -match '^CAMERA framing ' })
if ($framing.Count -lt 20) {
    $failures.Add("only $($framing.Count) 'CAMERA framing' lines (want >= 20)")
}
$measured = 0
foreach ($line in $framing) {
    if ($line -notmatch '^CAMERA framing frame=(\d+) player=(-?\d+) zoom=(\d+) boom=\S+ pitch=\S+ fov=(\S+)$') {
        $failures.Add("malformed framing line: $line")
        continue
    }
    $player = [int]$Matches[2]
    if ([int]$Matches[3] -ne $DefaultZoomMm) { $failures.Add("zoom is not the default ${DefaultZoomMm}: $line") }
    if ($Matches[4] -ne $Fov) { $failures.Add("fov is not ${Fov}: $line") }
    if ($player -lt 0) { continue }
    $measured++
    if ($player -lt $MinPermille -or $player -gt $MaxPermille) {
        $failures.Add("the Warden is drawn $player permille tall (want $MinPermille..$MaxPermille): $line")
    }
}
if ($framing.Count -gt 0 -and ($measured * 10) -lt ($framing.Count * 9)) {
    $failures.Add("only $measured of $($framing.Count) framing lines measured the Warden (want >= 90 percent)")
}

if ($failures.Count -gt 0) {
    Write-Output 'CameraFramingSceneTest: FAIL'
    $failures | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" }
    exit 1
}
$first = $framing | Select-Object -First 1
Write-Output "CameraFramingSceneTest: PASS ($($framing.Count) framing lines, $measured measured; first: $first)"
exit 0
