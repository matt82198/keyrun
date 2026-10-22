<#
.SYNOPSIS
  HUD scene run D (checks 14-16, lane 3I): the armed boss, from pull 4 (the boss pull), three-seat party, key 10 seed 7.
  -- --autorun --ticks <BossTicks> --pull 4 --key 10 --seed 7

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text, the tick
  budget's history and the RED notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunD-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$BossTicks = 600
)
$SceneName = 'HudRunD-SceneTest'
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

# ---- Run D: the armed boss (lane 3I) -----------------------------------------------------------------------------
# From pull 4 (the boss pull): no key reaches the boss from pull 1 under autorun (see the header for the sweep).
$d = Invoke-Arena $BossTicks @('--pull', '4', '--key', '10', '--seed', '7')
Write-SceneLog 'D' $d.Lines
if ($d.Code -ne 0) { $failures.Add("run D: godot exited $($d.Code) (expected 0)") }
$summaryD = First $d.Lines "^AUTORUN done ticks=$BossTicks "
if (-not $summaryD) { $failures.Add("run D: no 'AUTORUN done ticks=$BossTicks' summary") }
$armedD = Assert-BossArmed 'D' @(FirstRun $d.Lines)
$bossPull = First $d.Lines '^PULL spawn index=0 id=boss actors=900:hollowWarden\.boss$'
if (-not $bossPull) { $failures.Add('run D: no "PULL spawn index=0 id=boss actors=900:hollowWarden.boss" line') }
# Canonical EVENT JSON sorts keys: actorId, bossActorId, classId, tick, type.
$twins = @()
foreach ($t in @(@{ Id = 901; Class = 'hollowWarden.twinA' }, @{ Id = 902; Class = 'hollowWarden.twinB' })) {
    $ev = First $d.Lines "^EVENT \{`"actorId`":$($t.Id),`"bossActorId`":900,`"classId`":`"$([regex]::Escape($t.Class))`",.*`"type`":`"boss_add_spawn`""
    if (-not $ev) { $failures.Add("run D: no boss_add_spawn EVENT for $($t.Id) $($t.Class)") } else { $twins += $ev }
}
$bossRule = First $d.Lines '^EVENT \{.*"type":"(lock_open|lock_broken|lock_failed|boss_enrage)"'

# Run-end titles (lane 3C-d) on run D's lines.
$titlesD = Assert-RunEndTitles $d.Lines

Write-Output '--- run D (armed boss), lane 3I ---'
foreach ($l in @($summaryD, $armedD, $bossPull) + $twins + @($bossRule)) {
    if ($l) { Write-Output $l }
}
Write-RunEndTitles $titlesD

Complete-Scene "bossTicks=$BossTicks"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
