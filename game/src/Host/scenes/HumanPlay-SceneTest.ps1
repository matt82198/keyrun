<#
.SYNOPSIS
  Scene test HUMAN-PLAY (D65/D66, Matt 2026-10-22 "tomorrow morning i want to be able to play the tutorial" and "I
  also want to be able to play any class, not just the warden"): a human on seat 1 plays the tutorial as each class,
  through the real input path, with bots in the two other roles.
  -- --replay-input game/fixtures/input/human-<class>.input-log.json --progression-dir <tmp> --camera vertical --log <tmp>
  (--quit-after <Frames>; one launch per class: warden, chanter, cinder)

.DESCRIPTION
  Each log drives the pre-run screen (Down 0, 1 or 2 times on the Character tab, then Enter queues) and the first
  minute of the run (W into the Choir's ring, Tab onto the Choir Cantor, G, the five buttons, Q on the Cantor's heal).
  tools/Keyrun.Playtest.Tests/HumanReplayTests.cs replays the same logs headlessly in game-test. Checks, per class:
    H1. exit 0, and "INPUT replay done events=N" (every event of the log was fed);
    H2. "RUN seats human=<class> bots=<the two other classes>" (SimDriver.StartRun: the Character tab's class is seat
        1's; before HUMAN-PLAY there is no seats line: RED);
    H3. an 'EVENT {... "pullId":"choir" ... "type":"pull_start"}' line (seat 1 walked into the Choir);
    H4. at least 10 'EVENT {"ability":...,"actorId":1,...,"type":"cast_complete"}' lines (seat 1 casts its kit);
    H5. an 'EVENT {"abilityId":"choir.healCast",...,"sourceId":1,...,"type":"interrupt"}' line (seat 1 kicked the
        Cantor's heal);
    H6. a "PING routed kind=target" line (G reached the bots);
    H7. no "ACTOR model id=1 class=" line naming another class (the seat-1 body is the picked class's).
  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HumanPlay-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing, or another launch holds the lock; see tools/godot/godot.ps1).
#>
param(
    [int]$Frames = 3900  # 12 screen frames + 1200 ticks at 20 Hz under --fixed-fps 60 (3600 frames) + slack
)
$SceneName = 'HumanPlay-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$classes = @('warden', 'chanter', 'cinder')
$summary = New-Object System.Collections.Generic.List[string]

foreach ($class in $classes) {
    $log = Join-Path $repo "game/fixtures/input/human-$class.input-log.json"
    $tag = [Guid]::NewGuid().ToString('N')
    $progDir = Join-Path ([IO.Path]::GetTempPath()) "keyrun-human-play-$class-$tag"
    $runLog = Join-Path ([IO.Path]::GetTempPath()) "keyrun-human-play-$class-$tag.txt"
    New-Item -ItemType Directory -Force -Path $progDir | Out-Null
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $Frames '--' --replay-input $log --progression-dir $progDir --camera vertical --log $runLog 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Assert-GodotFound $rc $text
    $lines = @($text -split "`r?`n")
    Write-SceneLog $class.ToUpperInvariant() $lines
    $bots = (@('warden', 'chanter', 'cinder') | Where-Object { $_ -ne $class })

    # H1
    if ($rc -ne 0) { $failures.Add("${class}: godot exited $rc (expected 0)") }
    $err = First $lines '^ARENA error '
    if ($err) { $failures.Add("${class}: '$err'") }
    $done = First $lines '^INPUT replay done events=\d+ '
    if (-not $done) { $failures.Add("${class}: no 'INPUT replay done' line (the log was not fed to the end)") }
    # H2
    $seats = First $lines '^RUN seats '
    $wantSeats = "RUN seats human=$class bots=2:$($bots[0]),3:$($bots[1])"
    if ($seats -ne $wantSeats) { $failures.Add("${class}: seats line '$seats', want '$wantSeats'") }
    # H3
    $choir = First $lines '^EVENT \{.*"pullId":"choir".*"type":"pull_start"\}$'
    if (-not $choir) { $failures.Add("${class}: no Choir pull_start EVENT (seat 1 never walked into the Choir)") }
    # H4
    $casts = @($lines | Where-Object { $_ -match '^EVENT \{.*"actorId":1,.*"type":"cast_complete"' }).Count
    if ($casts -lt 10) { $failures.Add("${class}: seat 1 completed $casts casts (want >= 10)") }
    # H5
    $kick = First $lines '^EVENT \{"abilityId":"choir\.healCast",.*"sourceId":1,.*"type":"interrupt"\}$'
    if (-not $kick) { $failures.Add("${class}: no interrupt of the Cantor's heal by seat 1") }
    # H6
    $ping = First $lines '^PING routed kind=target '
    if (-not $ping) { $failures.Add("${class}: no 'PING routed kind=target' line (G did not reach the bots)") }
    # H7
    $wrongBody = @($lines | Where-Object { $_ -match '^ACTOR model id=1 class=(\S+) ' -and $Matches[1] -ne $class })
    if ($wrongBody.Count -gt 0) { $failures.Add("${class}: seat 1 drew another class's body: '$($wrongBody[0])'") }

    $summary.Add("${class}: $seats | casts=$casts | $kick | $ping")
    Remove-Item -Recurse -Force -LiteralPath $progDir -ErrorAction SilentlyContinue
}

Write-Output '--- HUMAN-PLAY (D65/D66): seat 1 as each class, replayed through the real input path ---'
foreach ($l in $summary) { Write-Output $l }

Complete-Scene "frames=$Frames"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
