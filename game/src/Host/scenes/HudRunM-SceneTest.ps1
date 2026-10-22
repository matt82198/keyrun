<#
.SYNOPSIS
  HUD scene run M (checks 36-39, lane HOST-HEADING): a recorded human run starts facing the route and the input log
  carries that heading.
  M1  -- --record-input <log> --press-enter 1 --log <m1>       (--quit-after <HeadingFrames>)
  M2  -- --replay-input <log> --log <m2>                       (--quit-after <HeadingFrames>)
  M3  -- --replay-input <walk> --log <m3>                      (<walk>: M1's startHeading, Enter at 0, W from 1)
  M4  -- --replay-input <bad> --log <m4>                       (<bad>: "startHeading": "east")

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3); its -HeadingOnly switch is
  this file plus HudSourceChecks-SceneTest.ps1. The check text and the RED notes are in README.md beside this file.
  Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunM-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$HeadingFrames = 420
)
$SceneName = 'HudRunM-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

# One interactive launch for run M: exit code and output lines.
function Invoke-Interactive([int]$frames, [string[]]$userArgs) {
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $frames '--' @userArgs 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Assert-GodotFound $rc $text
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}

# ---- Run M: a recorded human run starts facing the route; the input log carries the heading (lane HOST-HEADING) ---
# "TICK n warden=(x,z)" lines of a --log file by tick (the first run's line for each tick).
function Get-WardenTickLines([string]$path) {
    $out = New-Object 'System.Collections.Generic.Dictionary[int,string]'
    if (-not (Test-Path -LiteralPath $path)) { return ,$out }
    foreach ($l in [IO.File]::ReadAllLines($path)) {
        if ($l -match '^(TICK (\d+) warden=\((-?\d+),(-?\d+)\))' -and -not $out.ContainsKey([int]$Matches[2])) { $out[[int]$Matches[2]] = $Matches[1] }
    }
    return ,$out
}

function Invoke-RunM {
    $shown = New-Object System.Collections.Generic.List[string]
    $tag = [Guid]::NewGuid().ToString('N')
    $tmp = [IO.Path]::GetTempPath()
    $mRec = Join-Path $tmp "keyrun-hud-heading-$tag.input-log.json"
    $mWalk = Join-Path $tmp "keyrun-hud-heading-walk-$tag.input-log.json"
    $mBad = Join-Path $tmp "keyrun-hud-heading-bad-$tag.input-log.json"
    $m1Log = Join-Path $tmp "keyrun-hud-m1-$tag.txt"
    $m2Log = Join-Path $tmp "keyrun-hud-m2-$tag.txt"
    $m3Log = Join-Path $tmp "keyrun-hud-m3-$tag.txt"
    $m4Log = Join-Path $tmp "keyrun-hud-m4-$tag.txt"
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $inv = [Globalization.CultureInfo]::InvariantCulture

    # 36. M1: a plain recording heads down the route and writes that heading.
    $m1 = Invoke-Interactive $HeadingFrames @('--record-input', $mRec, '--press-enter', '1', '--log', $m1Log)
    Write-SceneLog 'M1' $m1.Lines
    if ($m1.Code -ne 0) { $failures.Add("run M1: godot exited $($m1.Code) (expected 0)") }
    $head1 = First $m1.Lines '^CAMERA start heading'
    $headYaw = $null
    if (-not $head1) {
        $failures.Add("run M1: no 'CAMERA start heading' line")
    } elseif ($head1 -match 'reason=input-log') {
        $failures.Add("run M1: '$head1' (a --record-input launch kept the legacy heading instead of facing the route)")
    } elseif ($head1 -match ' yaw=(\S+)$') {
        $headYaw = $Matches[1]
    }
    [void]$shown.Add($head1)
    $sh1 = Get-StartHeadingText $mRec
    if (-not (Test-Path -LiteralPath $mRec)) {
        $failures.Add("run M1: --record-input wrote no file at $mRec")
    } else {
        $recM = Get-Content -Raw -LiteralPath $mRec | ConvertFrom-Json
        if ($recM.schema -ne 'keyrun.input-log/1') { $failures.Add("run M1: input log schema is '$($recM.schema)' (want keyrun.input-log/1: the start heading is a header field, not a camera event)") }
        if ($null -eq $sh1) {
            $failures.Add("run M1: the input log has no `"startHeading`" header line")
        } else {
            [void]$shown.Add("M1 startHeading=$sh1")
            $shF = [single]::Parse($sh1, $inv)
            if ($headYaw -and $shF -ne [single]::Parse($headYaw, $inv)) { $failures.Add("run M1: startHeading $sh1 is not the camera's start yaw $headYaw") }
            if ([math]::Abs($shF + [math]::PI / 2) -gt 0.001) { $failures.Add("run M1: startHeading $sh1 does not face +x (want -Pi/2)") }
        }
    }

    # 37. M2: replaying M1's file restores the heading and gives the same TICK lines.
    $m2 = Invoke-Interactive $HeadingFrames @('--replay-input', $mRec, '--log', $m2Log)
    Write-SceneLog 'M2' $m2.Lines
    if ($m2.Code -ne 0) { $failures.Add("run M2: godot exited $($m2.Code) (expected 0)") }
    $rh2 = First $m2.Lines '^INPUT replay heading yaw=\S+$'
    [void]$shown.Add($rh2)
    if (-not $rh2) {
        $failures.Add("run M2: no 'INPUT replay heading yaw=Y' line (the replay did not apply the file's startHeading)")
    } elseif ($sh1 -and $rh2 -ne "INPUT replay heading yaw=$sh1") {
        $failures.Add("run M2: '$rh2' (want 'INPUT replay heading yaw=$sh1')")
    }
    $t1 = Get-WardenTickLines $m1Log
    $t2 = Get-WardenTickLines $m2Log
    $comparedM = 0
    foreach ($t in @($t1.Keys | Sort-Object)) {
        if (-not $t2.ContainsKey($t)) { continue }
        if ($t2[$t] -ne $t1[$t]) { $failures.Add("run M2: '$($t2[$t])' but M1 had '$($t1[$t])'"); break }
        $comparedM++
    }
    if ($comparedM -lt 20) { $failures.Add("run M2: only $comparedM TICK warden lines compared between M1 and M2 (want >= 20)") }
    [void]$shown.Add("M2 compared=$comparedM")

    # 38. M3: the recorded heading is the one W walks along (+x down corridor 1).
    $walkHeading = if ($sh1) { $sh1 } else { '-1.5707964' }
    $walk = "{`n  `"schema`": `"keyrun.input-log/1`",`n  `"seed`": 1,`n  `"key`": 2,`n  `"startHeading`": $walkHeading,`n  `"events`": [`n" +
        "    { `"tick`": 0, `"key`": `"Enter`", `"pressed`": true },`n    { `"tick`": 1, `"key`": `"W`", `"pressed`": true }`n  ]`n}`n"
    [IO.File]::WriteAllText($mWalk, $walk, $utf8)
    $m3 = Invoke-Interactive $HeadingFrames @('--replay-input', $mWalk, '--log', $m3Log)
    Write-SceneLog 'M3' $m3.Lines
    if ($m3.Code -ne 0) { $failures.Add("run M3: godot exited $($m3.Code) (expected 0)") }
    $rh3 = First $m3.Lines '^INPUT replay heading yaw=\S+$'
    if ($rh3 -ne "INPUT replay heading yaw=$walkHeading") { $failures.Add("run M3: heading line '$rh3' (want 'INPUT replay heading yaw=$walkHeading')") }
    $t3 = Get-WardenTickLines $m3Log
    $first3 = @($t3.Keys | Where-Object { $_ -ge 1 } | Sort-Object | Select-Object -First 1)
    if ($first3.Count -lt 1 -or -not $t3.ContainsKey($first3[0] + 20)) {
        $failures.Add("run M3: the run log has no TICK warden lines 20 ticks apart after the W press")
    } else {
        $a3 = $t3[$first3[0]]
        $b3 = $t3[$first3[0] + 20]
        [void]$shown.Add("M3 $a3 -> $b3")
        $null = $a3 -match 'warden=\((-?\d+),(-?\d+)\)'; $ax = [int]$Matches[1]; $az = [int]$Matches[2]
        $null = $b3 -match 'warden=\((-?\d+),(-?\d+)\)'; $bx = [int]$Matches[1]; $bz = [int]$Matches[2]
        if ($bx -le ($ax + 100) -or [math]::Abs($bz - $az) -gt 100) { $failures.Add("run M3: W moved the Warden $a3 -> $b3 (want x up by more than 100 mm, z within 100 mm: down +x)") }
    }

    # 39. M4: a mistyped startHeading is refused.
    [IO.File]::WriteAllText($mBad, ($walk -replace '"startHeading": [^,]+,', '"startHeading": "east",'), $utf8)
    $m4 = Invoke-Interactive 120 @('--replay-input', $mBad, '--log', $m4Log)
    Write-SceneLog 'M4' $m4.Lines
    # The error comes on stderr, which PowerShell may wrap across lines: match it with the whitespace taken out.
    $err4 = $null
    if ((($m4.Lines -join '') -replace '\s', '') -match 'ARENAerror:[^\r\n]*?startHeadingmustbeafinitenumber') { $err4 = 'ARENA error ... startHeading must be a finite number' }
    [void]$shown.Add("M4 exit=$($m4.Code) $err4")
    if ($m4.Code -ne 1 -or -not $err4) { $failures.Add("run M4: exit $($m4.Code), error '$err4' (want exit 1 and an 'ARENA error' naming startHeading)") }

    foreach ($p in @($mRec, $mWalk, $mBad, $m1Log, $m2Log, $m3Log, $m4Log)) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p } }
    return ,$shown.ToArray()
}

$mShown = @(Invoke-RunM)

Write-Output '--- run M (recorded start heading), lane HOST-HEADING ---'
foreach ($l in $mShown) {
    if ($l) { Write-Output $l }
}

Complete-Scene "headingFrames=$HeadingFrames"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
