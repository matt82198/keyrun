<#
.SYNOPSIS
  Headless scene test for BOSS-AUTORUN (MVP-MAP G8, unit U11): a plain --autorun reaches the Hollow Warden.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 and autoruns the scripted Warden with
  NO --ticks, so the autorun runs until the first run_end (the Warden's kill of pull 4, or the run clock's timer cap):

      godot --headless --path game --fixed-fps 60 -- --autorun --key 2 --seed 1 --log <demo.log>

  Before BOSS-AUTORUN the autorun stopped at 400 ticks (SimDriver.cs, the Finish check on ArenaOptions.DefaultTicks),
  which is before pull 2 is cleared, so the boss was never proven. The stall ceiling is the fixture's ceilingTicks
  (game/fixtures/input/boss-autorun.json); a run still going at that tick ends with "run=running" and fails below.

  The test asserts, from the run log (demo.log) and the engine's own summary line:
    1. the process exits 0;
    2. "RUN start autorun=1" (the plain autorun started a run);
    3. "PULL spawn index=3 " (pull 4, the boss pull, started);
    4. exactly one "RUN end cause=<c> result=<r> tick=<t>" line, with c = boss_dead or timer;
    5. an EVENT line with "type":"run_end", logged after the pull 4 start;
    6. "AUTORUN done ticks=N ... run=<r>" with N past 400 (the old default cut) and r not "running".

  Run from the repo root:  pwsh game/src/Host/BossAutorunSceneTest.ps1
  (or: powershell -ExecutionPolicy Bypass -File game/src/Host/BossAutorunSceneTest.ps1)
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$spec = Get-Content -LiteralPath (Join-Path $repo 'game/fixtures/input/boss-autorun.json') -Raw | ConvertFrom-Json
$ceiling = [int]$spec.run.ceilingTicks
$dir = Join-Path ([IO.Path]::GetTempPath()) ('keyrun-boss-autorun-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$logFile = Join-Path $dir 'demo.log'

$failures = New-Object System.Collections.Generic.List[string]

# 20 Hz sim at 60 fps is 3 frames per tick; the guard only stops a hang, a correct run quits by itself at run_end.
$guardFrames = (3 * $ceiling) + 240
$ErrorActionPreference = 'Continue'
# In-process call, as game.yml's smoke step does, so the quoted '--' reaches Godot intact.
$out = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guardFrames '--' --autorun --key ([string]$spec.run.key) --seed ([string]$spec.run.seed) --log $logFile 2>&1 | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

if ($code -eq 2 -and $out -match 'Godot binary not found') {
    Write-Output $out
    Write-Output 'BossAutorunSceneTest: CANNOT RUN (Godot binary not found)'
    exit 2
}

if ($code -ne 0) {
    $failures.Add("godot exited $code (expected 0)")
}

$lines = @()
if (Test-Path -LiteralPath $logFile) {
    $lines = @(Get-Content -LiteralPath $logFile)
} else {
    $failures.Add("run log not written: $logFile")
}

if (-not ($lines | Where-Object { $_ -match '^RUN start autorun=1 ' })) {
    $failures.Add('no "RUN start autorun=1" line (the plain autorun did not start a run)')
}

$bossIdx = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].StartsWith('PULL spawn index=3 ')) { $bossIdx = $i; break }
}
if ($bossIdx -lt 0) {
    $failures.Add('no "PULL spawn index=3 " line (pull 4, the Warden, never started)')
}

$ends = @($lines | Where-Object { $_ -match '^RUN end cause=' })
$cause = ''
if ($ends.Count -ne 1) {
    $failures.Add("expected exactly one RUN end line, got $($ends.Count)")
} elseif ($ends[0] -match '^RUN end cause=(\S+) ') {
    $cause = $Matches[1]
    if ($spec.expect.runEndCauses -notcontains $cause) {
        $failures.Add("RUN end cause '$cause' is not one of $($spec.expect.runEndCauses -join ', ')")
    }
} else {
    $failures.Add("RUN end line does not name a cause: '$($ends[0])'")
}

$evIdx = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^EVENT \{.*"type":"run_end"') { $evIdx = $i; break }
}
if ($evIdx -lt 0) {
    $failures.Add('no EVENT line with "type":"run_end" in the run log')
} elseif ($bossIdx -ge 0 -and $evIdx -lt $bossIdx) {
    $failures.Add('run_end was logged before pull 4 started')
}

$summary = @(@($lines) + @($out -split "`r?`n") | Where-Object { $_ -match '^AUTORUN done ' }) | Select-Object -Last 1
$ticks = -1
if (-not $summary) {
    $failures.Add('no "AUTORUN done" summary line')
} else {
    if ($summary -match ' ticks=(\d+) ') {
        $ticks = [int]$Matches[1]
        if ($ticks -le [int]$spec.expect.minTicksExclusive) {
            $failures.Add("AUTORUN done ticks=$ticks is not past the old 400-tick cut ($($spec.expect.minTicksExclusive))")
        }
    } else {
        $failures.Add("AUTORUN done line has no ticks= field: '$summary'")
    }
    if ($summary -match ' run=running') {
        $failures.Add("run is still running at the end of the autorun (the stall ceiling of $ceiling ticks was hit)")
    }
}

Write-Output '--- evidence ---'
$lines | Where-Object { $_ -match '^(RUN start|PULL spawn index=3 |RUN end )' } | ForEach-Object { Write-Output $_ }
$lines | Where-Object { $_ -match '^EVENT \{.*"type":"run_end"' } | Select-Object -First 1 | ForEach-Object { Write-Output $_ }
if ($summary) { Write-Output $summary }
if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }

if ($failures.Count -gt 0) {
    Write-Output "BossAutorunSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "BossAutorunSceneTest: PASS cause=$cause ticks=$ticks"
exit 0
