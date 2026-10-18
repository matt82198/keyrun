<#
.SYNOPSIS
  Headless scene test for the 3C HUD (ENGINE-PLAN phase 3, lane 3C; BIBLE s7 choice of three, s8 Slack, s10 pings):
  party frames, objective line, Slack bar, SPREAD/STACK labels and the run-end panel on the real run root.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 twice and reads the HUD's own
  "HUD ..." lines from the engine output (the HUD prints what it draws when --autorun is on):

    Run A (pull 1, the Choir, solo Warden):  -- --autorun --ticks 400
      1. exit 0 and "AUTORUN done ticks=400";
      2. "HUD party seat=1 name=Warden hp=H/400 res=R/M" (the party frame reads the human's health and resource);
      3. "HUD party seat=1 ... DOWNED" after the sim's death event for actor 1 (the DOWNED chip);
      4. "HUD objective pull=1/4 id=choir name="The Choir"" (the current pull's content name);
      5. "HUD slack ticks=620 of=620" first (Timer.InitialSlackTicks(2) = 600 + 20), then a lower value after the
         death (the sim spends 300 on a death and 500 on the wipe);
      6. "HUD runend result=depleted choices=3 cleared=0 ... title="Party wiped -- key depleted"" (the sim reports a
         lasting wipe as "depleted"; the panel must not call it cleared), three "HUD runend choice n=<1..3>
         slot=<slot> tier=<t> facet=..." lines (Rewards.RollRewardChoices), and "HUD runend cause seat=1" lines
         naming only seat 1's own causes.
    Run B (from pull 2, the Pair):  -- --autorun --ticks 400 --pull 2
      7. "HUD objective pull=2/4 id=pair name="The Pair"";
      8. at least one "HUD ping label=SPREAD" or "HUD ping label=STACK" line (from the Pair's mark telegraphs and
         mark effects in the sim state). The solo Warden is down before the Pair's first mark (about tick 294), so
         this line comes from enemy scripts the sim keeps stepping after run_end; a live-run call needs a party.
  Before 3C the arena has no HUD and none of 2-8 appear (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/HudSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 400,
    [int]$PingTicks = 400
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

function Invoke-Arena([int]$n, [string[]]$extra) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    $guard = (3 * $n) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $n @extra 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rc -eq 2 -and $text -match 'Godot binary not found') {
        Write-Output $text
        Write-Output 'HudSceneTest: CANNOT RUN (Godot binary not found)'
        exit 2
    }
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}

function First($lines, [string]$pattern) {
    return $lines | Where-Object { $_ -match $pattern } | Select-Object -First 1
}

# ---- Run A: pull 1 ---------------------------------------------------------------------------------------------
$a = Invoke-Arena $Ticks @()
if ($a.Code -ne 0) { $failures.Add("run A: godot exited $($a.Code) (expected 0)") }
$summary = First $a.Lines "^AUTORUN done ticks=$Ticks "
if (-not $summary) { $failures.Add("run A: no 'AUTORUN done ticks=$Ticks' summary") }

$party = First $a.Lines '^HUD party seat=1 name=Warden hp=\d+/400 res=\d+/\d+'
if (-not $party) { $failures.Add('run A: no "HUD party seat=1 name=Warden hp=H/400 res=R/M" line') }

$death = First $a.Lines '^EVENT \{"actorId":1,.*"type":"death"'
$downed = First $a.Lines '^HUD party seat=1 .*\bDOWNED\b'
if (-not $death) {
    $failures.Add('run A: the Warden did not die (no death EVENT for actor 1); DOWNED cannot be checked')
} elseif (-not $downed) {
    $failures.Add('run A: actor 1 died but no "HUD party seat=1 ... DOWNED" line')
} else {
    $iDeath = [array]::IndexOf($a.Lines, $death)
    $iDowned = [array]::IndexOf($a.Lines, $downed)
    if ($iDowned -lt $iDeath) { $failures.Add('run A: DOWNED was drawn before the sim death event') }
}
$aliveAfter = $null
if ($downed) {
    $iDowned = [array]::IndexOf($a.Lines, $downed)
    $aliveAfter = $a.Lines[($iDowned + 1)..($a.Lines.Count - 1)] | Where-Object { $_ -match '^HUD party seat=1 ' -and $_ -notmatch '\bDOWNED\b' } | Select-Object -First 1
    if ($aliveAfter) { $failures.Add("run A: a party frame after DOWNED is not DOWNED: '$aliveAfter'") }
}

$objective = First $a.Lines '^HUD objective pull=1/4 id=choir name="The Choir"'
if (-not $objective) { $failures.Add('run A: no "HUD objective pull=1/4 id=choir name=""The Choir""" line') }

$slackLines = @($a.Lines | Where-Object { $_ -match '^HUD slack ticks=\d+ of=\d+' })
$slackFirst = $null
$slackLower = $null
if ($slackLines.Count -eq 0) {
    $failures.Add('run A: no "HUD slack" line')
} else {
    $slackFirst = $slackLines[0]
    if ($slackFirst -notmatch '^HUD slack ticks=620 of=620\b') {
        $failures.Add("run A: first Slack line is not the key-2 bank 620 of 620: '$slackFirst'")
    }
    $slackLower = $slackLines | Where-Object { $_ -match '^HUD slack ticks=(\d+) ' -and [int]$Matches[1] -lt 620 } | Select-Object -First 1
    if (-not $slackLower) { $failures.Add('run A: Slack never dropped below 620 after the death') }
}

$runEnd = First $a.Lines '^HUD runend result=depleted choices=3 cleared=0 tick=\d+ title="Party wiped -- key depleted"'
if (-not $runEnd) { $failures.Add('run A: no "HUD runend result=depleted choices=3 cleared=0 ... title=""Party wiped -- key depleted""" line') }
$choices = @($a.Lines | Where-Object { $_ -match '^HUD runend choice n=[123] slot=(weapon|armor|charm|ring|keystone) tier=\d+ facet="[^"]+"' })
if ($choices.Count -ne 3) { $failures.Add("run A: expected 3 'HUD runend choice' lines, got $($choices.Count)") }
$causes = @($a.Lines | Where-Object { $_ -match '^HUD runend cause ' })
if ($causes.Count -eq 0) {
    $failures.Add('run A: no "HUD runend cause" line (the private run log names what got the Warden)')
}
$foreign = @($causes | Where-Object { $_ -notmatch '^HUD runend cause seat=1 ' })
if ($foreign.Count -gt 0) { $failures.Add("run A: the run-end panel shows another seat's cause: '$($foreign[0])'") }

# ---- Run B: from pull 2 -----------------------------------------------------------------------------------------
$b = Invoke-Arena $PingTicks @('--pull', '2')
if ($b.Code -ne 0) { $failures.Add("run B: godot exited $($b.Code) (expected 0)") }
$objB = First $b.Lines '^HUD objective pull=2/4 id=pair name="The Pair"'
if (-not $objB) { $failures.Add('run B: no "HUD objective pull=2/4 id=pair name=""The Pair""" line') }
$ping = First $b.Lines '^HUD ping label=(SPREAD|STACK) '
if (-not $ping) { $failures.Add('run B: no "HUD ping label=SPREAD|STACK" line') }

Write-Output '--- asserted HUD lines ---'
foreach ($l in @($summary, $party, $death, $downed, $objective, $slackFirst, $slackLower, $runEnd) + $choices + $causes + @($objB, $ping)) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "HudSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "HudSceneTest: PASS ticks=$Ticks pingTicks=$PingTicks"
exit 0
