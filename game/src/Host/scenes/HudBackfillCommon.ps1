<#
.SYNOPSIS
  Shared backfill launch of HUD scene runs F and G (lane 3J): the --abandon-seat parse and Invoke-Backfill, split from the
  former game/src/Host/HudSceneTest.ps1. Not a scene test itself (the CI glob is *SceneTest.ps1).

.DESCRIPTION
  Dot-source it after HudSceneCommon.ps1, from a script whose param block sets $AbandonSeat and $BackfillTicks.
#>

# ---- Runs F and G: backfill in the real loop (lane 3J) -----------------------------------------------------------
# SimDriver steps Backfill.Step: the seat abandoned in pull 1 is refilled at pull 2's start by a matching-key joiner.
$seatParts = $AbandonSeat -split '@'
$seatActor = $seatParts[0]
$seatTick = $seatParts[1]
# R-415-D: A@pullN+O leaves O ticks after pull N's pull_start; its SEAT abandon line names the anchor and T = S + O.
# f-0fd3e6fb: A@pullN.hpP+O leaves O ticks after pull N's standing enemies fall to P percent of their health; its line
# names the share and the anchor ("hp=P at=S") and T = S + O likewise.
$seatRel = $null
if ($seatTick -match '^pull(\d+)\+(\d+)$') {
    $seatRel = [pscustomobject]@{ Pull = [int]$Matches[1]; Offset = [int]$Matches[2]; Anchor = 'start=(\d+)' }
} elseif ($seatTick -match '^pull(\d+)\.hp(\d+)\+(\d+)$') {
    $seatRel = [pscustomobject]@{ Pull = [int]$Matches[1]; Offset = [int]$Matches[3]; Anchor = "hp=$($Matches[2]) at=(\d+)" }
}
$abandonPattern = if ($seatRel) { "^SEAT abandon actor=$seatActor tick=(\d+) pull=$($seatRel.Pull) $($seatRel.Anchor) offset=$($seatRel.Offset)$" } else { "^SEAT abandon actor=$seatActor tick=$seatTick$" }
function Invoke-Backfill([string]$run, [int]$joinerKey) {
    $r = Invoke-Arena $BackfillTicks @('--abandon-seat', $AbandonSeat, '--joiner-key', "$joinerKey")
    Write-SceneLog $run $r.Lines
    if ($r.Code -ne 0) { $failures.Add("run ${run}: godot exited $($r.Code) (expected 0)") }
    $lines = @(FirstRun $r.Lines)
    $res = [pscustomobject]@{ Lines = $lines; Abandon = $null; Add = $null; Pull = $null; IPull = -1; Refill = $null; Consume = $null }
    $res.Abandon = First $lines $abandonPattern
    if ($res.Abandon -and $seatRel -and $res.Abandon -match $abandonPattern -and [int]$Matches[1] -ne [int]$Matches[2] + $seatRel.Offset) {
        $failures.Add("run ${run}: the abandon tick is not the pull start plus the offset: '$($res.Abandon)'")
    }
    $res.Add = First $lines "^QUEUE add dungeon=hollow-vault key=$joinerKey "
    $res.Pull = First $lines '^PULL spawn index=1 id=pair '
    if ($res.Pull) { $res.IPull = [array]::IndexOf($lines, $res.Pull) }
    $res.Refill = First $lines '^SEAT refill '
    $res.Consume = First $lines '^QUEUE consume '
    if (-not $res.Abandon) { $failures.Add("run ${run}: no SEAT abandon line for --abandon-seat $AbandonSeat (want '$abandonPattern')") }
    if (-not $res.Add) { $failures.Add("run ${run}: no 'QUEUE add dungeon=hollow-vault key=$joinerKey' line (the scripted joiner never reached the Queue node)") }
    if (-not $res.Pull) {
        $failures.Add("run ${run}: the party did not reach 'PULL spawn index=1 id=pair' (no next pull start to refill at)")
    } elseif ($res.Abandon -and [array]::IndexOf($lines, $res.Abandon) -gt $res.IPull) {
        $failures.Add("run ${run}: the abandon came after pull 2 spawned (it must be in pull 1)")
    }
    return $res
}
