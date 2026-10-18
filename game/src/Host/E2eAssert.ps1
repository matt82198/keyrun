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

  Run from the repo root after an export:
    powershell -ExecutionPolicy Bypass -File game/src/Host/E2eAssert.ps1 [-Exe build/keyrun.console.exe] [-ExtraArgs '--solo']
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (exe or fixture missing).
#>
param(
    [string]$Exe = 'build/keyrun.console.exe',
    [string]$Fixture = 'game/fixtures/input/walk-kick.json',
    [string]$OutDir = 'build',
    [string[]]$ExtraArgs = @()
)
$ErrorActionPreference = 'Stop'

$failures = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
    Write-Output "E2eAssert: CANNOT RUN (exported exe not found: $Exe)"
    exit 2
}
if (-not (Test-Path -LiteralPath $Fixture -PathType Leaf)) {
    Write-Output "E2eAssert: CANNOT RUN (input fixture not found: $Fixture)"
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

Write-Output '--- asserted lines (run 1) ---'
foreach ($l in @($summary) + $seats + $botCasts + @($pull2, $tickLine, $livePing, $runEndEv, $bots1, $bots2)) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "E2eAssert: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "E2eAssert: PASS ticks=$ticks"
exit 0
