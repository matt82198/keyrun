<#
.SYNOPSIS
  HUD scene run J (check 29, lane 3H-c): a human G-ping reaches the bot party; interactive, three-seat party, pull 1.
  -- --press-enter 1 --press-ping <PingAt>   (--quit-after <PingFrames>)

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text and the RED
  notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunJ-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$PingAt = 100,
    [int]$PingFrames = 900
)
$SceneName = 'HudRunJ-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

# ---- Run J: a human G-ping reaches the bot party (lane 3H-c) ------------------------------------------------------
$ErrorActionPreference = 'Continue'
$jText = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $PingFrames '--' --press-enter 1 --press-ping $PingAt 2>&1 | Out-String
$jCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $jCode $jText
$jLines = @($jText -split "`r?`n")
Write-SceneLog 'J' $jLines
if ($jCode -ne 0) { $failures.Add("run J: godot exited $jCode (expected 0)") }
$inputJ = First $jLines '^INPUT ping tick=\d+$'
$routedJ = $null
$reactJ = $null
if (-not $inputJ) {
    $failures.Add("run J: no 'INPUT ping tick=T' line (the scripted G was never pushed by frame $PingFrames)")
} else {
    $pressTick = [int]($inputJ -replace '^INPUT ping tick=', '')
    $iIn = [array]::IndexOf($jLines, $inputJ)
    $afterIn = @($jLines[($iIn + 1)..($jLines.Count - 1)])
    $routedJ = First $afterIn '^PING routed '
    if (-not $routedJ) {
        $failures.Add('run J: the G press reached no bot (no "PING routed" line: nothing routes PlayerInput.Pinged)')
    } elseif ($routedJ -notmatch '^PING routed kind=spot target=none tick=(\d+)') {
        $failures.Add("run J: wrong routed ping: '$routedJ' (want 'PING routed kind=spot target=none tick=N')")
    } else {
        $pingTick = [int]$Matches[1]
        if ($pingTick -lt $pressTick) { $failures.Add("run J: the ping was routed at tick $pingTick, before the press at $pressTick") }
        $iRt = [array]::IndexOf($afterIn, $routedJ)
        $afterRt = @($afterIn[($iRt + 1)..($afterIn.Count - 1)])
        $reactJ = First $afterRt '^PING react actor=[23] kind=move tick=\d+$'
        if (-not $reactJ) {
            $failures.Add('run J: no bot seat reacted to the routed ping (no "PING react actor=2|3 kind=move" line)')
        } else {
            $reactTick = [int]($reactJ -replace '^.* tick=', '')
            if ($reactTick -lt $pingTick -or $reactTick -gt ($pingTick + 40)) {
                $failures.Add("run J: the bot reaction at tick $reactTick is outside the ping's 40-tick move window from $pingTick")
            }
        }
    }
}

Write-Output '--- run J (human G-ping -> bots), lane 3H-c ---'
foreach ($l in @($inputJ, $routedJ, $reactJ)) {
    if ($l) { Write-Output $l }
}

Complete-Scene "pingAt=$PingAt pingFrames=$PingFrames"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
