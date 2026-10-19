<#
.SYNOPSIS
  Headless scene test for GATE-VIEW (STATE.md D27): the room's entry gate is a node in the scene tree exactly while the
  sim reports the room gated (gate_closed {room} .. gate_open {room}).

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 and replays the deterministic run C (the
  scripted Warden plus the two bot seats from the Entrance, the default seed):

      godot --headless --path game --fixed-fps 60 -- --autorun --ticks N --log <file>

  The sim's gate events come from the run log ("EVENT {...gate_closed...}" / "EVENT {...gate_open...}"); the view's
  side comes from the engine output, where src/View/RoomGateView.cs prints what it put in or took out of the scene
  tree ("GATEVIEW shut|open|probe ..."). The test asserts:
    1. the process exits 0 and prints "AUTORUN done ticks=N";
    2. the run gates at least two rooms and opens at least one (run C: choir-room 50..1749, pair-hall from 1844, ...);
    3. one "GATEVIEW shut room=R tick=C ... inTree=True" per gate_closed {room=R} at tick C, and one
       "GATEVIEW open room=R tick=O inTree=False" per gate_open {room=R} at tick O;
    4. every "GATEVIEW probe tick=T shut=..." (a scene-tree read every 20 ticks) names exactly the rooms R whose gate
       interval holds T (C <= T <= O; the gate_open step leaves state tick O + 1): the gate node is present between
       gate_closed and gate_open, absent before gate_closed and absent after gate_open;
    5. at least one probe falls inside a gate interval, one before the first gate_closed and one after a gate_open.
  Before GATE-VIEW the view drew no entry gate: no GATEVIEW line at all (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/GateViewSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 3600
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$logFile = Join-Path ([IO.Path]::GetTempPath()) ("keyrun-gateview-test-" + [Guid]::NewGuid().ToString('N') + '.txt')

$failures = New-Object System.Collections.Generic.List[string]

$ErrorActionPreference = 'Continue'
# --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
$guardFrames = (3 * $Ticks) + 240
$out = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guardFrames '--' --autorun --ticks $Ticks --log $logFile 2>&1 | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

if ($code -eq 2 -and $out -match 'Godot binary not found') {
    Write-Output $out
    Write-Output 'GateViewSceneTest: CANNOT RUN (Godot binary not found)'
    exit 2
}
if ($code -ne 0) {
    $failures.Add("godot exited $code (expected 0)")
}

$outLines = @($out -split "`r?`n")
$summary = $outLines | Where-Object { $_ -match '^AUTORUN done ' } | Select-Object -Last 1
if (-not $summary -or $summary -notmatch "^AUTORUN done ticks=$Ticks ") {
    $failures.Add("no 'AUTORUN done ticks=$Ticks' summary (got '$summary')")
}

$lines = @()
if (Test-Path -LiteralPath $logFile) {
    $lines = @(Get-Content -LiteralPath $logFile)
} else {
    $failures.Add("run log not written: $logFile")
}

# The sim's side: gate intervals per room, in event order.
$closed = New-Object System.Collections.Generic.List[object]
$opened = New-Object System.Collections.Generic.List[object]
foreach ($l in $lines) {
    if ($l -notmatch '^EVENT (\{.*"type":"gate_(closed|open)".*\})$') { continue }
    $ev = $Matches[1] | ConvertFrom-Json
    $row = [pscustomobject]@{ Room = [string]$ev.room; Tick = [int]$ev.tick }
    if ($ev.type -eq 'gate_closed') { $closed.Add($row) } else { $opened.Add($row) }
}
if ($closed.Count -lt 2) { $failures.Add("the run gated $($closed.Count) room(s) (expected at least 2 gate_closed EVENT lines)") }
if ($opened.Count -lt 1) { $failures.Add('the run opened no gate (expected at least 1 gate_open EVENT line)') }

$intervals = New-Object System.Collections.Generic.List[object]
foreach ($c in $closed) {
    $o = $opened | Where-Object { $_.Room -eq $c.Room -and $_.Tick -ge $c.Tick } | Select-Object -First 1
    $end = if ($o) { $o.Tick } else { [int]::MaxValue }
    $intervals.Add([pscustomobject]@{ Room = $c.Room; From = $c.Tick; To = $end })
}

# The view's side.
$shut = @($outLines | Where-Object { $_ -match '^GATEVIEW shut ' })
$open = @($outLines | Where-Object { $_ -match '^GATEVIEW open ' })
$probes = @($outLines | Where-Object { $_ -match '^GATEVIEW probe ' })

if ($shut.Count -ne $closed.Count) {
    $failures.Add("view shut $($shut.Count) gate(s) for $($closed.Count) gate_closed event(s)")
}
foreach ($c in $closed) {
    $want = "^GATEVIEW shut room=$([regex]::Escape($c.Room)) tick=$($c.Tick) .*inTree=True"
    if (-not ($shut | Where-Object { $_ -match $want })) {
        $failures.Add("no 'GATEVIEW shut room=$($c.Room) tick=$($c.Tick) ... inTree=True' for gate_closed {room=$($c.Room)} at $($c.Tick)")
    }
}
if ($open.Count -ne $opened.Count) {
    $failures.Add("view opened $($open.Count) gate(s) for $($opened.Count) gate_open event(s)")
}
foreach ($o in $opened) {
    $want = "^GATEVIEW open room=$([regex]::Escape($o.Room)) tick=$($o.Tick) .*inTree=False"
    if (-not ($open | Where-Object { $_ -match $want })) {
        $failures.Add("no 'GATEVIEW open room=$($o.Room) tick=$($o.Tick) ... inTree=False' for gate_open {room=$($o.Room)} at $($o.Tick)")
    }
}

$inside = 0
$before = 0
$after = 0
$firstClosed = if ($closed.Count -gt 0) { $closed[0].Tick } else { [int]::MaxValue }
foreach ($p in $probes) {
    if ($p -notmatch '^GATEVIEW probe tick=(\d+) shut=(\S+)') {
        $failures.Add("malformed probe line '$p'")
        continue
    }
    $t = [int]$Matches[1]
    $got = if ($Matches[2] -eq 'none') { '' } else { (($Matches[2] -split ',') | Sort-Object) -join ',' }
    $expected = (@($intervals | Where-Object { $_.From -le $t -and $t -le $_.To } | ForEach-Object { $_.Room }) | Sort-Object) -join ','
    if ($got -ne $expected) {
        $failures.Add("probe tick=$t shows shut='$got' but the sim gates '$expected'")
    }
    if ($expected -ne '') { $inside++ }
    if ($t -lt $firstClosed) { $before++ }
    if ($intervals | Where-Object { $_.To -ne [int]::MaxValue -and $t -gt $_.To -and $expected -notmatch [regex]::Escape($_.Room) }) { $after++ }
}
if ($inside -lt 1) { $failures.Add('no GATEVIEW probe inside a gate interval') }
if ($before -lt 1) { $failures.Add('no GATEVIEW probe before the first gate_closed') }
if ($after -lt 1) { $failures.Add('no GATEVIEW probe after a gate_open') }

Write-Output '--- engine output (tail) ---'
$outLines | Select-Object -Last 6 | ForEach-Object { Write-Output $_ }
Write-Output '--- sim gate events ---'
foreach ($i in $intervals) { Write-Output "gate $($i.Room) closed=$($i.From) open=$(if ($i.To -eq [int]::MaxValue) { 'never' } else { $i.To })" }
Write-Output '--- view gate lines ---'
foreach ($l in @($shut + $open)) { Write-Output $l }
Write-Output "probes=$($probes.Count) inside=$inside before=$before after=$after"
if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }

if ($failures.Count -gt 0) {
    Write-Output "GateViewSceneTest: FAIL ($($failures.Count))"
    $failures | Select-Object -First 12 | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "GateViewSceneTest: PASS ticks=$Ticks gates=$($closed.Count) opens=$($opened.Count) probes=$($probes.Count)"
exit 0
