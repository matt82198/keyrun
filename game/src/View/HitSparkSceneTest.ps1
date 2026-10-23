<#
.SYNOPSIS
  Headless scene test for ruling R-ANIM-1 (keyrun hit spark at the target): a health drop on an enemy or a party seat
  spawns a hit spark on that actor, so the hit reads at the target, as WoW does.

.DESCRIPTION
  Ruling R-ANIM-1: WoW reads a hit at the target. The view prints one line when it spawns the hit spark on a health
  drop; ActorAnimator reads the same drop for the hurt clip, so the spark and the hurt clip come from one event.
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 with a fresh temp directory as
  --progression-dir (the test never reads or writes the real user:// save) and runs a party run (--seed 1, key 2,
  first pulls engaged; no --solo):

      godot --headless --path game --fixed-fps 60 -- --autorun --ticks 2400 --seed 1 --progression-dir <tmp>

  The view prints, through GD.Print (stdout) and the run log:
      VIEW hit actor=<id> tick=<tick>
  one line per health drop on an enemy (actor >= 100) or a party seat (actor 1-4).
  This test asserts:
    1. the process exits 0 and prints "AUTORUN done ticks=2400";
    2. at least one "VIEW hit" line names an enemy (actor >= 100);
    3. at least one "VIEW hit" line names a party seat (actor 1-4);
    4. the run has at least one fallback-capsule actor ("ACTOR fallback capsule id=N"), else FAIL: the fallback path is untested;
    5. at least one fallback-capsule actor has a "VIEW hit actor=N" line (its hit spark prints, finding f-8a001150).
  Red by construction on main d1715072: no code prints the VIEW hit line yet, so the counts are zero and the test FAILS.
  The orchestrator runs the scene; lanes never launch Godot.

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/View/HitSparkSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 2400
)
$ErrorActionPreference = 'Stop'

$PartyMaxSeat = 4
$EnemyMinActor = 100

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

# One launch lock for the launch (tools/godot/launch-lock.ps1). Exit 2 when another Godot instance holds it.
. (Join-Path $repo 'tools/godot/launch-lock.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner 'HitSparkSceneTest.ps1' -ExitCodeOnBusy 2
try {
$dir = Join-Path ([System.IO.Path]::GetTempPath()) ('keyrun-hitspark-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null

# --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
$guard = (3 * $Ticks) + 240
$ErrorActionPreference = 'Continue'
$text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks --seed 1 --progression-dir $dir --capsule-class warden 2>&1 | Out-String
$rc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue

if ($rc -eq 2 -and $text -match 'Godot binary not found') {
    Write-Output $text
    Write-Output 'HitSparkSceneTest: CANNOT RUN (Godot binary not found)'
    exit 2
}
if ($rc -ne 0) { $failures.Add("godot exited $rc (expected 0)") }

$lines = @($text -split "`r?`n")
if (-not ($lines | Where-Object { $_ -match "^AUTORUN done ticks=$Ticks " } | Select-Object -First 1)) {
    $failures.Add("no 'AUTORUN done ticks=$Ticks' summary")
}

$hits = @($lines | Where-Object { $_ -match '^VIEW hit actor=(\d+) tick=(\d+)$' })
$enemyHits = @($hits | Where-Object { $_ -match '^VIEW hit actor=(\d+) tick=\d+$' -and [int]$Matches[1] -ge $EnemyMinActor })
$partyHits = @($hits | Where-Object { $_ -match '^VIEW hit actor=(\d+) tick=\d+$' -and [int]$Matches[1] -ge 1 -and [int]$Matches[1] -le $PartyMaxSeat })

if ($enemyHits.Count -eq 0) {
    $failures.Add("no 'VIEW hit actor=<id>' line for an enemy (actor >= $EnemyMinActor): no hit spark at an enemy target")
}
if ($partyHits.Count -eq 0) {
    $failures.Add("no 'VIEW hit actor=<id>' line for a party seat (actor 1-$PartyMaxSeat): no hit spark at a party target")
}

# Fallback-capsule actors (ActorView.TryBind, game/src/View/ActorView.cs:176): a bound model that is missing or fails to
# load draws the capsule and prints "ACTOR fallback capsule id=N class=C" once. Its hit spark must still print (R-ANIM-1,
# finding f-8a001150): at least one fallback actor must have a "VIEW hit actor=N" line.
$fallbackIds = @()
foreach ($line in $lines) {
    if ($line -match '^ACTOR fallback capsule id=(\d+) class=\S+$') { $fallbackIds += [int]$Matches[1] }
}
$fallbackIds = @($fallbackIds | Sort-Object -Unique)
$sparkedIds = @{}
foreach ($hit in $hits) {
    if ($hit -match '^VIEW hit actor=(\d+) tick=\d+$') { $sparkedIds[[int]$Matches[1]] = $true }
}
$fallbackSparked = @($fallbackIds | Where-Object { $sparkedIds.ContainsKey($_) })
$fallbackVerdict = $null
if ($fallbackIds.Count -eq 0) {
    $fallbackVerdict = 'HitSparkSceneTest: FAIL no fallback-capsule actor in run; the fallback path is untested'
} elseif ($fallbackSparked.Count -eq 0) {
    $fallbackVerdict = "HitSparkSceneTest: FAIL fallback actor $($fallbackIds[0]) never sparked"
}
if ($fallbackVerdict) {
    $failures | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" }
    Write-Output $fallbackVerdict
    exit 1
}

if ($failures.Count -gt 0) {
    Write-Output 'HitSparkSceneTest: FAIL'
    $failures | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" }
    exit 1
}
Write-Output "HitSparkSceneTest: PASS enemyHits=$($enemyHits.Count) partyHits=$($partyHits.Count) ticks=$Ticks fallbackSparked=$($fallbackSparked.Count)"
exit 0
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
