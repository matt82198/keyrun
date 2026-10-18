<#
.SYNOPSIS
  game-e2e assertion for the D10 bar (ENGINE-PLAN lane 3E): the EXPORTED game runs the Warden plus Chanter and
  Cinder bot party through pull 1 with a live HUD ping, and the bot command stream is deterministic.

.DESCRIPTION
  Runs the exported console exe (not the editor, not dotnet test) headless twice with the same arguments:

    <exe> --headless --fixed-fps 60 --quit-after <3N+240> -- --autorun --ticks N [ExtraArgs]

  N and the --autorun arguments come from game/fixtures/input/walk-kick.json ("run"); the fixture is checked
  fail-closed (N must pass run.mustPassTick, which must be at least 295). From the engine output of run 1 it asserts:

    1. exit 0 and "AUTORUN done ticks=N";
    2. three "HUD party seat=" lines: seat=1 name=Warden, seat=2 name=Chanter, seat=3 name=Cinder;
    3. "PULL spawn index=1" (the party cleared pull 1 and the next pull spawned);
    4. inside pull 1 (before "PULL spawn index=1"): at least one heal EVENT with sourceId 2 (Chanter) on a party seat
       and at least one damage EVENT with sourceId 3 (Cinder) on an enemy;
    5. "TICK <mustPassTick>" before the party wipe (every seated actor dead) and before run_end;
    6. a "HUD ping label=STACK|SPREAD" line before both run_end and the party wipe (a call in a live run);
    7. "BOTS done commands=N hash=H" with N > 0, and the same line in run 2 (determinism).
  Any missing line exits 1. A --solo run (ExtraArgs '--solo') has one seat and no bots, so it must FAIL.

  Replay pass (lane 3E-a2, ENGINE-PLAN row 3E "a recorded file drives the Warden"): the same exe is then run twice in
  INTERACTIVE mode (no --autorun) replaying the recorded input log -ReplayFixture through the real PlayerInput route:

    <exe> --headless --fixed-fps 60 --quit-after 900 -- --replay-input <ReplayFixture> --log <OutDir>/e2e-replayN.log

  (SimDriver.MaybeReplay feeds each event through Input.ParseInputEvent and each camera heading through
  CameraRig.RestoreYaw. Interactive TICK lines go to the --log file only; RunLog.Tick echoes them to the console under
  --autorun alone. Both paths are made absolute: the exported exe resolves a relative path against its own folder.)
  From those two runs it asserts:

    8. exit 0, one "INPUT replay tick=T key=K pressed=B" line per fixture event, "INPUT replay done events=N ..." with
       N > 0 and N = the fixture's event count, and no scripted "INPUT enter|ping|yaw" line;
    9. the replay drove the Warden: "TICK 0".."TICK <ReplayTicks-1>" warden lines are all in the --log file (a launch
       without --replay-input waits on the between-runs screen and logs no TICK line at all), the Warden moved at
       least 1000 mm across them, and, when the fixture holds camera headings, the Warden's facing at the first
       heading's tick + 20 differs from its facing the tick before it;
   10. determinism: the SHA-256 (first 16 hex) of those TICK lines, each ending in LF, is equal in both runs and equals
       $ReplayTickDigest committed below (computed once from the default fixture; a deliberate change to the fixture
       or the sim regenerates it with a stated reason, never to fit a red run).

  Run from the repo root after an export:
    powershell -ExecutionPolicy Bypass -File game/src/Host/E2eAssert.ps1 [-Exe build/keyrun.console.exe] [-ExtraArgs '--solo']
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (exe or fixture missing).
#>
param(
    [string]$Exe = 'build/keyrun.console.exe',
    [string]$Fixture = 'game/fixtures/input/walk-kick.json',
    [string]$OutDir = 'build',
    [string[]]$ExtraArgs = @(),
    [string]$ReplayFixture = 'game/fixtures/input/example-yaw-walk.input-log.json'
)
$ErrorActionPreference = 'Stop'

# Replay pass constants (lane 3E-a2). The digest is of TICK 0..ReplayTicks-1 of the default ReplayFixture's replay.
$ReplayFrames = 900
$ReplayTicks = 240
$ReplayTickDigest = '94eb4f3d216477f9'

$failures = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
    Write-Output "E2eAssert: CANNOT RUN (exported exe not found: $Exe)"
    exit 2
}
if (-not (Test-Path -LiteralPath $Fixture -PathType Leaf)) {
    Write-Output "E2eAssert: CANNOT RUN (input fixture not found: $Fixture)"
    exit 2
}
if (-not (Test-Path -LiteralPath $ReplayFixture -PathType Leaf)) {
    Write-Output "E2eAssert: CANNOT RUN (replay input log not found: $ReplayFixture)"
    exit 2
}

# ---- fixture (fail closed) --------------------------------------------------------------------------------------
$spec = Get-Content -LiteralPath $Fixture -Raw | ConvertFrom-Json
if ($spec.schema -ne 'keyrun.e2e.walk-kick/1') { Write-Output "E2eAssert: CANNOT RUN (fixture schema '$($spec.schema)' is not keyrun.e2e.walk-kick/1)"; exit 2 }
$ticks = [int]$spec.run.ticks
$mustPass = [int]$spec.run.mustPassTick
$fps = [int]$spec.run.fixedFps
$runArgs = @($spec.run.args)
if ($mustPass -lt 295) { Write-Output "E2eAssert: CANNOT RUN (fixture run.mustPassTick=$mustPass is below 295)"; exit 2 }
if ($ticks -le $mustPass) { Write-Output "E2eAssert: CANNOT RUN (fixture run.ticks=$ticks does not pass tick $mustPass)"; exit 2 }
if ($fps -ne 60) { Write-Output "E2eAssert: CANNOT RUN (fixture run.fixedFps=$fps; the 20 Hz sim needs 60)"; exit 2 }
if ($runArgs -notcontains '--autorun') { Write-Output 'E2eAssert: CANNOT RUN (fixture run.args has no --autorun)'; exit 2 }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Invoke-Exported([int]$n, [string]$tag) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    $guard = (3 * $n) + 240
    $userArgs = @($runArgs) + @('--ticks', "$n") + @($ExtraArgs)
    $ErrorActionPreference = 'Continue'
    $text = & $Exe --headless --fixed-fps $fps --quit-after $guard '--' @userArgs 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $log = Join-Path $OutDir "e2e-$tag.txt"
    Set-Content -LiteralPath $log -Value $text -Encoding utf8
    Write-Output "run ${tag}: exit=$rc lines=$(@($text -split "`r?`n").Count) args='$($userArgs -join ' ')' log=$log" | Out-Host
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}

function First($lines, [string]$pattern) {
    return $lines | Where-Object { $_ -match $pattern } | Select-Object -First 1
}

$r1 = Invoke-Exported $ticks 'run1'
$r2 = Invoke-Exported $ticks 'run2'
$lines = $r1.Lines

# 1. exit and summary
if ($r1.Code -ne 0) { $failures.Add("run 1: exported exe exited $($r1.Code) (expected 0)") }
if ($r2.Code -ne 0) { $failures.Add("run 2: exported exe exited $($r2.Code) (expected 0)") }
$summary = First $lines "^AUTORUN done ticks=$ticks "
if (-not $summary) { $failures.Add("no 'AUTORUN done ticks=$ticks' summary") }

# 2. the three seats
$seats = @()
foreach ($s in @(@{ Seat = 1; Name = 'Warden' }, @{ Seat = 2; Name = 'Chanter' }, @{ Seat = 3; Name = 'Cinder' })) {
    $line = First $lines "^HUD party seat=$($s.Seat) name=$($s.Name) hp=\d+/\d+ "
    if (-not $line) { $failures.Add("no 'HUD party seat=$($s.Seat) name=$($s.Name)' line") } else { $seats += $line }
}

# 3. pull 1 cleared
$pull2 = First $lines '^PULL spawn index=1 '
$iPull2 = if ($pull2) { [array]::IndexOf($lines, $pull2) } else { -1 }
if (-not $pull2) { $failures.Add('no "PULL spawn index=1" line (the party did not clear pull 1)') }

# 4. bot casts inside pull 1. Canonical EVENT JSON sorts keys: amount, sourceId, targetId, tick, type.
#    Party seats are 1-3, enemies 100+ (boss 900).
$botCasts = @()
if ($iPull2 -ge 0) {
    $pull1Lines = $lines[0..$iPull2]
    foreach ($want in @(
            @{ Who = 'heal EVENT with sourceId 2 (Chanter) on a party seat'; Re = '^EVENT \{"amount":[1-9]\d*,"sourceId":2,"targetId":[123],.*"type":"heal"' },
            @{ Who = 'damage EVENT with sourceId 3 (Cinder) on an enemy'; Re = '^EVENT \{.*"healthDamage":[1-9]\d*,.*"sourceId":3,"targetId":(1\d\d|900),.*"type":"damage"' })) {
        $ev = First $pull1Lines $want.Re
        if (-not $ev) { $failures.Add("no $($want.Who) inside pull 1") } else { $botCasts += $ev }
    }
}

# 5./6. live-run bound: run_end, or the party's third death if earlier (the sim keeps stepping enemies after a wipe).
$runEndEv = First $lines '^EVENT \{.*"type":"run_end"'
$iLive = if ($runEndEv) { [array]::IndexOf($lines, $runEndEv) } else { $lines.Count }
#    The party is wiped once every seated actor (seats the HUD drew, at least one) has a death EVENT.
$seated = @($lines | Where-Object { $_ -match '^HUD party seat=([123]) ' } | ForEach-Object { [regex]::Match($_, '^HUD party seat=(\d)').Groups[1].Value } | Sort-Object -Unique)
$seatCount = [Math]::Max(1, $seated.Count)
$dead = New-Object System.Collections.Generic.HashSet[string]
for ($i = 0; $i -lt $iLive; $i++) {
    $m = [regex]::Match($lines[$i], '^EVENT \{"actorId":([123]),.*"type":"death"')
    if ($m.Success) {
        [void]$dead.Add($m.Groups[1].Value)
        if ($dead.Count -ge $seatCount) { $iLive = $i; break }
    }
}
$tickLine = First $lines "^TICK $mustPass "
if (-not $tickLine -or [array]::IndexOf($lines, $tickLine) -gt $iLive) { $failures.Add("the party was not alive at tick $mustPass") }
$livePing = $null
for ($i = 0; $i -lt $iLive; $i++) {
    if ($lines[$i] -match '^HUD ping label=(STACK|SPREAD) ') { $livePing = $lines[$i]; break }
}
if (-not $livePing) { $failures.Add('no "HUD ping label=STACK|SPREAD" line while the run was live') }

# 7. bot determinism
$bots1 = First $r1.Lines '^BOTS done commands=\d+ hash=[0-9a-f]{16}$'
$bots2 = First $r2.Lines '^BOTS done commands=\d+ hash=[0-9a-f]{16}$'
if (-not $bots1) { $failures.Add('run 1: no "BOTS done commands=N hash=H" line') }
elseif ($bots1 -match 'commands=0 ') { $failures.Add("run 1: the bots issued no commands: '$bots1'") }
if (-not $bots2) { $failures.Add('run 2: no "BOTS done commands=N hash=H" line') }
if ($bots1 -and $bots2 -and $bots1 -ne $bots2) { $failures.Add("bot command stream differs across two same-seed runs: '$bots1' vs '$bots2'") }

# ---- replay pass (lane 3E-a2): a recorded input log drives the Warden through PlayerInput --------------------------
$replayFull = (Resolve-Path -LiteralPath $ReplayFixture).Path
$outFull = (Resolve-Path -LiteralPath $OutDir).Path
$replaySpec = Get-Content -LiteralPath $replayFull -Raw | ConvertFrom-Json
$replayEvents = @($replaySpec.events | Where-Object { $null -ne $_ })
$replayCamera = @($replaySpec.camera | Where-Object { $null -ne $_ })
if ($replaySpec.schema -notmatch '^keyrun\.input-log/[12]$') { $failures.Add("replay fixture schema '$($replaySpec.schema)' is not keyrun.input-log/1 or /2") }

function Invoke-Replay([string]$tag) {
    $logPath = Join-Path $outFull "e2e-$tag.log"
    if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath }
    $userArgs = @('--replay-input', $replayFull, '--log', $logPath)
    $ErrorActionPreference = 'Continue'
    $text = & $Exe --headless --fixed-fps $fps --quit-after $ReplayFrames '--' @userArgs 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Set-Content -LiteralPath (Join-Path $OutDir "e2e-$tag.txt") -Value $text -Encoding utf8
    $logLines = if (Test-Path -LiteralPath $logPath) { @([IO.File]::ReadAllLines($logPath)) } else { @() }
    Write-Output "run ${tag}: exit=$rc lines=$(@($text -split "`r?`n").Count) log-lines=$($logLines.Count) args='$($userArgs -join ' ')'" | Out-Host
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n"); Log = $logLines }
}

# The first "TICK n warden=(x,z) yaw=Y ..." line of each tick in a --log file, keyed by tick.
function Get-WardenTicks($logLines) {
    $byTick = @{}
    foreach ($l in $logLines) {
        if ($l -match '^TICK (\d+) warden=\(-?\d+,-?\d+\) ' -and -not $byTick.ContainsKey([int]$Matches[1])) { $byTick[[int]$Matches[1]] = $l }
    }
    return $byTick
}

# SHA-256 (first 16 hex) of TICK 0..ReplayTicks-1, each line ending in LF; null when any tick of the window is missing.
function Get-TickDigest($byTick) {
    $window = New-Object System.Text.StringBuilder
    for ($t = 0; $t -lt $ReplayTicks; $t++) {
        if (-not $byTick.ContainsKey($t)) { return $null }
        [void]$window.Append($byTick[$t]).Append("`n")
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($window.ToString()))
    return -join ($hash[0..7] | ForEach-Object { $_.ToString('x2') })
}

function Get-WardenPos([string]$tickLine) {
    $m = [regex]::Match($tickLine, '^TICK \d+ warden=\((-?\d+),(-?\d+)\) yaw=(-?\d+) ')
    return [pscustomobject]@{ X = [int]$m.Groups[1].Value; Z = [int]$m.Groups[2].Value; Yaw = $m.Groups[3].Value }
}

$p1 = Invoke-Replay 'replay1'
$p2 = Invoke-Replay 'replay2'

# 8. the replayer fed the fixture's events through the input route
if ($p1.Code -ne 0) { $failures.Add("replay run 1: exported exe exited $($p1.Code) (expected 0)") }
if ($p2.Code -ne 0) { $failures.Add("replay run 2: exported exe exited $($p2.Code) (expected 0)") }
$replayLines = @($p1.Lines | Where-Object { $_ -match '^INPUT replay tick=\d+ key=\S+ pressed=(true|false)$' })
$replayDone = First $p1.Lines '^INPUT replay done events=\d+ '
$replayN = if ($replayDone) { [int]($replayDone -replace '^INPUT replay done events=(\d+) .*$', '$1') } else { 0 }
if (-not $replayDone) { $failures.Add("replay run 1: no 'INPUT replay done events=N' line (--replay-input fed nothing)") }
elseif ($replayN -le 0) { $failures.Add("replay run 1: the replayer fed no events: '$replayDone'") }
elseif ($replayN -ne $replayEvents.Count) { $failures.Add("replay run 1: '$replayDone' but the fixture holds $($replayEvents.Count) events") }
if ($replayLines.Count -ne $replayEvents.Count -or $replayLines.Count -eq 0) { $failures.Add("replay run 1: $($replayLines.Count) 'INPUT replay tick=' lines for $($replayEvents.Count) fixture events") }
$scripted = First $p1.Lines '^INPUT (enter|ping|yaw) '
if ($scripted) { $failures.Add("replay run 1: a scripted press ran during the replay: '$scripted'") }

# 9. the replay drove the Warden (a launch without --replay-input logs no TICK line: it waits on the screen)
$w1 = Get-WardenTicks $p1.Log
$w2 = Get-WardenTicks $p2.Log
$moved = $null
$turned = $null
if (-not $w1.ContainsKey(0) -or -not $w1.ContainsKey($ReplayTicks - 1)) {
    $failures.Add("replay run 1: the --log file has no TICK 0 and TICK $($ReplayTicks - 1) warden lines (the replay never started a run)")
} else {
    $a = Get-WardenPos $w1[0]
    $b = Get-WardenPos $w1[$ReplayTicks - 1]
    $moved = "$($w1[0]) -> $($w1[$ReplayTicks - 1])"
    if (([Math]::Abs($b.X - $a.X) + [Math]::Abs($b.Z - $a.Z)) -lt 1000) { $failures.Add("replay run 1: the Warden moved less than 1000 mm from TICK 0 to TICK $($ReplayTicks - 1): $moved") }
    if ($replayCamera.Count -gt 0) {
        $c = [int]$replayCamera[0].tick
        if (-not $w1.ContainsKey($c - 1) -or -not $w1.ContainsKey($c + 20)) {
            $failures.Add("replay run 1: no TICK $($c - 1) and TICK $($c + 20) warden lines around the first camera heading")
        } else {
            $turned = "$($w1[$c - 1]) -> $($w1[$c + 20])"
            if ((Get-WardenPos $w1[$c - 1]).Yaw -eq (Get-WardenPos $w1[$c + 20]).Yaw) { $failures.Add("replay run 1: the Warden's facing did not change after the replayed camera heading at tick ${c}: $turned") }
        }
    }
}

# 10. determinism against the committed digest
$digest1 = Get-TickDigest $w1
$digest2 = Get-TickDigest $w2
if (-not $digest1) { $failures.Add("replay run 1: TICK 0..$($ReplayTicks - 1) are not all in the --log file (no digest)") }
if (-not $digest2) { $failures.Add("replay run 2: TICK 0..$($ReplayTicks - 1) are not all in the --log file (no digest)") }
if ($digest1 -and $digest2 -and $digest1 -ne $digest2) { $failures.Add("replay TICK digest differs across two runs of the same log: $digest1 vs $digest2") }
if ($digest1 -and $digest1 -ne $ReplayTickDigest) { $failures.Add("replay TICK digest $digest1 is not the committed $ReplayTickDigest") }

Write-Output '--- asserted lines (run 1) ---'
foreach ($l in @($summary) + $seats + $botCasts + @($pull2, $tickLine, $livePing, $runEndEv, $bots1, $bots2)) {
    if ($l) { Write-Output $l }
}
Write-Output "--- asserted lines (replay pass, $ReplayFixture) ---"
foreach ($l in $replayLines + @($replayDone, $moved, $turned)) {
    if ($l) { Write-Output $l }
}
if ($digest1) { Write-Output "REPLAY ticks=0..$($ReplayTicks - 1) digest=$digest1 run2=$digest2 committed=$ReplayTickDigest" }

if ($failures.Count -gt 0) {
    Write-Output "E2eAssert: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "E2eAssert: PASS ticks=$ticks"
exit 0
