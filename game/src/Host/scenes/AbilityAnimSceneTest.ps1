<#
.SYNOPSIS
  ANIMS-READABLE-2 scene test (Matt 2026-10-21 "theres still no ability animations", f-a5ffb07b; KF1 f-fa28e36e):
  every player ability the default party uses and every boss cast logs a clip start that is not idle or run.

.DESCRIPTION
  Two headless launches of the real game, each writing its run log with --log:
    A  the default three-seat party run, pull 1 (the Choir):  -- --autorun --ticks <PartyTicks> --log <file>
    B  the same party started at the boss pull:               -- --autorun --pull 4 --ticks <BossTicks> --log <file>
  The view writes one line per clip start through the host's log sink (src/View/AbilityClipPlayer.cs):
      ANIM actor=<id> clip=<clip> ability=<ability id or -> at=<sim tick>
  Checks, per launch:
    1. godot exits 0 and the run log exists;
    2. every "ANIM actor=" line carries at=<tick>;
    3. for every (actor, ability) a player seat (actors 1-3) resolved (an EVENT cast_complete), at least one ANIM line
       on that actor names that ability with a clip that is not an idle, run or walk loop;
    4. (B) the same for every ability the boss started (CAST start) or resolved;
    5. vacuity guards: A resolves at least 5 distinct player abilities, B at least 2 distinct boss abilities.
  Before ANIMS-READABLE-2 the ANIM line had no at= (check 2 RED) and a second ability resolved on the same step as a
  first drew no clip (check 3 RED for cinder.coldSnap / cinder.detonate on the capture kf-main-0e748ca1).

  Run from the repo root (orchestrator or CI only; lanes never launch the game):
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/AbilityAnimSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing, or another launch holds the lock).
#>
param(
    [int]$PartyTicks = 900,
    [int]$BossTicks = 1800
)
$SceneName = 'AbilityAnimSceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$proof = New-Object System.Collections.Generic.List[string]
$loopClip = '^(Idle|Idle_B|Idle_Combat|Unarmed_Idle|2H_Melee_Idle|Running_[A-Z]|Running_Strafe_\w+|Walking_\w+)$'

# The run log's lines, or an empty list (and a failure) when the launch wrote none.
function Read-RunLog([string]$run, [string]$path) {
    if (Test-Path -LiteralPath $path) {
        $l = @(Get-Content -LiteralPath $path)
        Remove-Item -LiteralPath $path -Force
        return $l
    }
    $failures.Add("run ${run}: run log not written: $path")
    return @()
}

# Checks 2 to 4 on one run log; $actors are the actor ids whose abilities must each show a non-loop clip. Returns the
# number of distinct (actor, ability) pairs checked.
function Assert-AbilityClips([string]$run, $lines, [int[]]$actors, [switch]$CastStarts) {
    $anim = @{}
    foreach ($l in $lines) {
        if ($l -notmatch '^ANIM actor=') { continue }
        $m = [regex]::Match($l, '^ANIM actor=(\d+) clip=(\S+) ability=(\S+) at=(\d+)$')
        if (-not $m.Success) {
            $failures.Add("run ${run}: ANIM line without the 'ANIM actor=N clip=X ability=Y at=T' shape: '$l'")
            continue
        }
        if ($m.Groups[2].Value -match $loopClip) { continue }
        $anim["$($m.Groups[1].Value) $($m.Groups[3].Value)"] = $l
    }
    $used = [ordered]@{}
    foreach ($l in $lines) {
        $c = [regex]::Match($l, '^EVENT \{.*"abilityId":"([^"]+)","actorId":(\d+),.*"type":"cast_complete"')
        if ($c.Success -and [int]$c.Groups[2].Value -in $actors) { $used["$($c.Groups[2].Value) $($c.Groups[1].Value)"] = $l }
        if ($CastStarts) {
            $s = [regex]::Match($l, '^CAST start actor=(\d+) ability=(\S+) ')
            if ($s.Success -and [int]$s.Groups[1].Value -in $actors) { $used["$($s.Groups[1].Value) $($s.Groups[2].Value)"] = $l }
        }
    }
    foreach ($key in $used.Keys) {
        if ($anim.ContainsKey($key)) {
            $proof.Add("run ${run}: $key -> $($anim[$key])")
        } else {
            $failures.Add("run ${run}: actor/ability '$key' was used ('$($used[$key])') but no ANIM line shows a clip other than idle/run for it")
        }
    }
    return $used.Count
}

# ---- A: the default party run, pull 1 -----------------------------------------------------------------------------
$logA = Join-Path ([IO.Path]::GetTempPath()) ("keyrun-abilityanim-a-" + [Guid]::NewGuid().ToString('N') + '.txt')
$ErrorActionPreference = 'Continue'
$aText = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after ((3 * $PartyTicks) + 240) '--' --autorun --ticks $PartyTicks --log $logA 2>&1 | Out-String
$aCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $aCode $aText
Write-SceneLog 'A' @($aText -split "`r?`n")
if ($aCode -ne 0) { $failures.Add("run A: godot exited $aCode (expected 0)") }
$aLines = Read-RunLog 'A' $logA
$aCount = Assert-AbilityClips 'A' $aLines @(1, 2, 3)
if ($aCount -lt 5) { $failures.Add("run A: vacuity guard: only $aCount distinct player abilities resolved (want >= 5)") }

# ---- B: the boss pull ---------------------------------------------------------------------------------------------
$logB = Join-Path ([IO.Path]::GetTempPath()) ("keyrun-abilityanim-b-" + [Guid]::NewGuid().ToString('N') + '.txt')
$ErrorActionPreference = 'Continue'
$bText = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after ((3 * $BossTicks) + 240) '--' --autorun --pull 4 --ticks $BossTicks --log $logB 2>&1 | Out-String
$bCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $bCode $bText
Write-SceneLog 'B' @($bText -split "`r?`n")
if ($bCode -ne 0) { $failures.Add("run B: godot exited $bCode (expected 0)") }
$bLines = Read-RunLog 'B' $logB
$bossLine = First $bLines '^PULL spawn .*\b(\d+):hollowWarden\.boss\b'
$bCount = 0
if (-not $bossLine) {
    $failures.Add("run B: no 'PULL spawn ... <id>:hollowWarden.boss' line (the --pull 4 run did not reach the boss)")
} else {
    $bossId = [int]([regex]::Match($bossLine, '(\d+):hollowWarden\.boss').Groups[1].Value)
    $bCount = Assert-AbilityClips 'B' $bLines @($bossId) -CastStarts
    if ($bCount -lt 2) { $failures.Add("run B: vacuity guard: only $bCount distinct boss abilities cast (want >= 2)") }
}

Write-Output '--- actor ability -> its latest non-loop ANIM line ---'
$proof | ForEach-Object { Write-Output $_ }
Complete-Scene "partyTicks=$PartyTicks bossTicks=$BossTicks playerAbilities=$aCount bossAbilities=$bCount"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
