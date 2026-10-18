<#
.SYNOPSIS
  Headless scene test for the 2E combat view (ENGINE-PLAN phase 2, lane 2E): Hollow Vault pull 1 through the real
  C# run root (RunRoot.CreateRun / StepRun) at 20 Hz.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 and autoruns the scripted Warden:

      godot --headless --path game --fixed-fps 60 -- --autorun --ticks N --log <file>

  The test reads the engine's own run log and asserts the ENGINE-PLAN 2E "Matt sees" beats, in order of the fight:
    1. the process exits 0 and prints "AUTORUN done ticks=N";
    2. "PULL spawn index=0 id=choir" names the three Choir actors (two choir.melee, one choir.lieutenant);
    3. at least one "CHASE actor=<id>" line: a Choir enemy closed distance on the Warden by itself (the sim's
       chase step moved it; the scripted Warden never pushes enemies toward himself);
    4. "CAST start actor=<lieutenant> ability=choir.healCast name="Choral Mending"": the lieutenant's 4 s heal began
       (the cast bar's source);
    5. an "EVENT" interrupt line from the Warden (sourceId 1) on choir.healCast: Q kicked it;
    6. at least one "CAST done actor=1 ability=warden.<id> name="<Name>"" line: a player cast completed and the log
       names it with the content display name the ability bar shows.
  Before 2E the arena ran DebugCleaveEmitter against a static box and none of 2-6 appear (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/CombatViewSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 400
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$logFile = Join-Path ([IO.Path]::GetTempPath()) ("keyrun-combatview-test-" + [Guid]::NewGuid().ToString('N') + '.txt')

$failures = New-Object System.Collections.Generic.List[string]

$ErrorActionPreference = 'Continue'
# --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
$guardFrames = (3 * $Ticks) + 240
$out = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guardFrames '--' --autorun --ticks $Ticks --log $logFile 2>&1 | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

if ($code -eq 2 -and $out -match 'Godot binary not found') {
    Write-Output $out
    Write-Output 'CombatViewSceneTest: CANNOT RUN (Godot binary not found)'
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

function First([string]$pattern) {
    return $lines | Where-Object { $_ -match $pattern } | Select-Object -First 1
}

$summary = ($out -split "`r?`n") | Where-Object { $_ -match '^AUTORUN done ' } | Select-Object -Last 1
if (-not $summary -or $summary -notmatch "^AUTORUN done ticks=$Ticks ") {
    $failures.Add("no 'AUTORUN done ticks=$Ticks' summary (got '$summary')")
}

$spawn = First '^PULL spawn index=0 id=choir '
$lieutenant = $null
if (-not $spawn) {
    $failures.Add('no "PULL spawn index=0 id=choir" line')
} else {
    $melee = ([regex]::Matches($spawn, '\d+:choir\.melee')).Count
    $lt = [regex]::Match($spawn, '(\d+):choir\.lieutenant')
    if ($melee -ne 2 -or -not $lt.Success) {
        $failures.Add("pull 1 spawn is not 2 choir.melee + 1 choir.lieutenant: '$spawn'")
    } else {
        $lieutenant = $lt.Groups[1].Value
    }
}

$chase = First '^CHASE actor=\d+ '
if (-not $chase) {
    $failures.Add('no "CHASE actor=" line (no Choir enemy closed on the Warden)')
}

$castStart = $null
if ($lieutenant) {
    $castStart = First "^CAST start actor=$lieutenant ability=choir\.healCast name=`"Choral Mending`" "
    if (-not $castStart) {
        $failures.Add("no 'CAST start actor=$lieutenant ability=choir.healCast name=""Choral Mending""' line")
    }
}

$kick = First '^EVENT \{.*"abilityId":"choir\.healCast".*"sourceId":1,.*"type":"interrupt"'
if (-not $kick) {
    $failures.Add('no interrupt EVENT from the Warden (sourceId 1) on choir.healCast')
}

$playerCast = First '^CAST done actor=1 ability=warden\.[A-Za-z]+ name="[^"]+"'
if (-not $playerCast) {
    $failures.Add('no "CAST done actor=1 ability=warden.* name=..." line')
}

Write-Output '--- engine output (tail) ---'
($out -split "`r?`n") | Select-Object -Last 6 | ForEach-Object { Write-Output $_ }
Write-Output '--- asserted log lines ---'
foreach ($l in @($spawn, $chase, $castStart, $kick, $playerCast)) {
    if ($l) { Write-Output $l }
}
if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }

if ($failures.Count -gt 0) {
    Write-Output "CombatViewSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "CombatViewSceneTest: PASS ticks=$Ticks"
exit 0
