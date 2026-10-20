<#
.SYNOPSIS
  Headless scene test for lane PROGRESSION-SAVE (BIBLE s7 "Every run banks Ash, win or lose"; the chase is gear you
  keep between runs): the between-runs screen loads the banked progression at launch and saves it after the bank, the
  reward take and each reroll, so the next launch starts with the same Ash and the same equipped gear.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 twice with the same fresh temp directory
  as --progression-dir (the test never reads or writes the real user:// save; a headless run without the flag
  persists nothing, see ProgressionSavePlace), and reads Screen's own "SCREEN progression ..." lines:

    Launch 1 (no save file yet):  -- --autorun --ticks 600 --solo --progression-dir <tmp>
      1. exit 0, "SCREEN progression load store=1 ash=0 runs=0 items=none path=<tmp>/progression.json": with no save
         the screen starts from Progression.Start (no Ash, no runs, nothing equipped), and the store is in <tmp>;
      2. "SCREEN progression save reason=bank ok=1 ash=30 runs=1" after "SCREEN bank result=depleted ash=30 total=30";
      3. "SCREEN progression save reason=take ok=1 ..." whose items name the taken reward's slot;
      4. two "SCREEN progression save reason=reroll ok=1 ..." lines (the script rerolls three times; the third is
         refused and changes nothing, so it saves nothing);
      5. <tmp>/progression.json exists.
    Launch 2 (the same <tmp>):
      6. "SCREEN progression load store=1 ash=30 runs=1 items=<I>" where <I> is exactly the items of launch 1's last
         save line (the equipped item, its tier, Facet and charges left come back), then "SCREEN open ash=60 runs=2"
         (the second run's Ash is added to the banked 30, not to a fresh 0).
  Before PROGRESSION-SAVE Screen calls Progression.Start every launch and never saves: no "SCREEN progression" line
  exists and launch 2 opens at ash=30 runs=1 (RED).

  Talent picks (lane TALENT-SAVE, BIBLE s6: the talents are the player's build, so a restart must not reset them):
      7. launch 1 opens with "SCREEN talents restored class=warden picks=0 dropped=0 applied=0 choices=none" (nothing
         saved yet), and each talent pick and the respec is followed by "SCREEN progression save reason=talent ok=1";
      8. launch 2 opens with "SCREEN talents restored class=warden picks=1 dropped=0 applied=1 choices=<A>:<O>" where
         <A>/<O> are the ability and option of launch 1's last "SCREEN talent pick" line (the respec cleared the rest).
  Before TALENT-SAVE the picks lived only in the Screen node: no "SCREEN talents restored" line exists and launch 2
  starts every fork unpicked (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Hud/ProgressionSaveSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 600
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

# One launch lock for both launches (tools/godot/launch-lock.ps1); they run one after another. Exit 2 when another
# Godot instance holds the lock.
. (Join-Path $repo 'tools/godot/launch-lock.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner 'ProgressionSaveSceneTest.ps1' -ExitCodeOnBusy 2
try {
$dir = Join-Path ([System.IO.Path]::GetTempPath()) ('keyrun-progression-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
$saveFile = Join-Path $dir 'progression.json'

function Invoke-Launch([string]$name) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    $guard = (3 * $Ticks) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks --solo --progression-dir $dir 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rc -eq 2 -and $text -match 'Godot binary not found') {
        Write-Output $text
        Write-Output 'ProgressionSaveSceneTest: CANNOT RUN (Godot binary not found)'
        Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
        exit 2
    }
    if ($rc -ne 0) { $failures.Add("launch $name`: godot exited $rc (expected 0)") }
    return ,@($text -split "`r?`n")
}

function First($lines, [string]$pattern) {
    return $lines | Where-Object { $_ -match $pattern } | Select-Object -First 1
}

$dirPattern = [regex]::Escape($saveFile) -replace '\\\\', '[\\/]'

# Launch 1: no save file yet.
$a = Invoke-Launch '1'
$loadA = First $a '^SCREEN progression load '
if (-not $loadA) {
    $failures.Add('launch 1: no "SCREEN progression load" line (Screen did not load through ProgressionStore)')
} else {
    if ($loadA -notmatch '^SCREEN progression load store=1 ash=0 runs=0 items=none path="(.+)"$') {
        $failures.Add("launch 1 with no save did not start from Progression.Start: '$loadA'")
    } elseif ($Matches[1] -notmatch "^$dirPattern$") {
        $failures.Add("launch 1 store is not in the test's temp directory: '$loadA'")
    }
}
$bankA = First $a '^SCREEN bank result=depleted ash=30 total=30$'
if (-not $bankA) { $failures.Add('launch 1: no "SCREEN bank result=depleted ash=30 total=30" line') }
$saveBank = First $a '^SCREEN progression save reason=bank ok=1 ash=30 runs=1 items=\S+$'
if (-not $saveBank) { $failures.Add('launch 1: no "SCREEN progression save reason=bank ok=1 ash=30 runs=1" line (not saved after the bank)') }
$take = First $a '^HUD runend take n=1 id=reward-(weapon|armor|charm|ring|keystone)-'
$slot = $null
if ($take) { $null = $take -match '^HUD runend take n=1 id=reward-(\w+)-'; $slot = $Matches[1] }
$saveTake = First $a '^SCREEN progression save reason=take ok=1 ash=30 runs=1 items=\S+$'
if (-not $saveTake) {
    $failures.Add('launch 1: no "SCREEN progression save reason=take ok=1" line (not saved after the reward take)')
} elseif ($slot -and $saveTake -notmatch "items=(\S+,)?$slot`:") {
    $failures.Add("launch 1: the take save does not hold the taken '$slot': '$saveTake'")
}
$saveRerolls = @($a | Where-Object { $_ -match '^SCREEN progression save reason=reroll ok=1 ash=30 runs=1 items=\S+$' })
if ($saveRerolls.Count -ne 2) { $failures.Add("launch 1: expected 2 'SCREEN progression save reason=reroll ok=1' lines, got $($saveRerolls.Count)") }
$lastSave = $a | Where-Object { $_ -match '^SCREEN progression save ' } | Select-Object -Last 1
$savedItems = $null
if ($lastSave -and $lastSave -match ' items=(\S+)$') { $savedItems = $Matches[1] }
if (-not (Test-Path $saveFile)) { $failures.Add("launch 1 wrote no save file at '$saveFile'") }

# Launch 2: the same directory; the banked progression comes back.
$b = Invoke-Launch '2'
$loadB = First $b '^SCREEN progression load '
if (-not $loadB) {
    $failures.Add('launch 2: no "SCREEN progression load" line')
} elseif (-not $savedItems -or $loadB -notmatch "^SCREEN progression load store=1 ash=30 runs=1 items=$([regex]::Escape($savedItems)) path=") {
    $failures.Add("launch 2 did not start from launch 1's save (saved items '$savedItems'): '$loadB'")
}
$openB = First $b '^SCREEN open ash=60 runs=2 '
if (-not $openB) {
    $failures.Add("launch 2: no 'SCREEN open ash=60 runs=2' line (got: '$(First $b '^SCREEN open ')')")
}

# Talent picks survive the restart (lane TALENT-SAVE).
$restoreA = First $a '^SCREEN talents restored '
if ($restoreA -notmatch '^SCREEN talents restored class=warden picks=0 dropped=0 applied=0 choices=none$') {
    $failures.Add("launch 1 with no saved picks did not restore an empty build: '$restoreA'")
}
$talentChanges = @($a | Where-Object { $_ -match '^SCREEN talent (pick|respec) ' })
$talentSaves = @($a | Where-Object { $_ -match '^SCREEN progression save reason=talent ok=1 ' })
if ($talentChanges.Count -eq 0 -or $talentSaves.Count -ne $talentChanges.Count) {
    $failures.Add("launch 1: expected one 'SCREEN progression save reason=talent ok=1' per talent change ($($talentChanges.Count)), got $($talentSaves.Count)")
}
$lastPick = $a | Where-Object { $_ -match '^SCREEN talent pick ' } | Select-Object -Last 1
$expectChoice = $null
if ($lastPick -and $lastPick -match ' ability=(\S+) option=(\S+) ') { $expectChoice = "$($Matches[1]):$($Matches[2])" }
$restoreB = First $b '^SCREEN talents restored '
if (-not $expectChoice) {
    $failures.Add("launch 1: no 'SCREEN talent pick' line to restore")
} elseif ($restoreB -ne "SCREEN talents restored class=warden picks=1 dropped=0 applied=1 choices=$expectChoice") {
    $failures.Add("launch 2 did not restore launch 1's pick $expectChoice into the panel and the queued loadout: '$restoreB'")
}

Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue

Write-Output '--- asserted SCREEN lines ---'
foreach ($l in @($loadA, $bankA, $saveBank, $take, $saveTake) + $saveRerolls + @($lastSave, $loadB, $openB, $restoreA) + $talentSaves + @($lastPick, $restoreB)) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "ProgressionSaveSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "ProgressionSaveSceneTest: PASS ticks=$Ticks slot=$slot items=$savedItems"
exit 0
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
