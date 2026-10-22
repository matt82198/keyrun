<#
.SYNOPSIS
  Scene test SCREEN-TABS (f-08279d82, D10 MVP line; BIBLE s1 "Screen -> Queue -> Dungeon -> End-of-run choice of
  three -> Screen"): the one pre-run screen opens on its Character tab, walks its tabs, and Queue starts the run.
  -- --autorun --ticks <Ticks> --solo --requeue   (run E's launch: the solo Warden's run ends at the timer cap)

.DESCRIPTION
  After the run ends the run-end panel takes reward 1 (the end-of-run choice of three) and opens the screen; the
  screen's autorun script (Screen.cs AutorunKeys) then rerolls, steps the key to 3, opens and closes Build (T ... T),
  presses F2, F4 and F5 (Gear, Key, Queue) and Enter. Checks, on the engine's own "SCREEN ..." lines:
    S1. exit 0;
    S2. the first "SCREEN tab ..." line after "SCREEN open" is "SCREEN tab character class=warden" (the screen opens on
        Character, Lobby.Open; before SCREEN-TABS it opened on Play: RED);
    S3. the Character tab's sheet (Host/CharacterSheet.cs): "SCREEN sheet class=warden key=2 marks=0 ascended=0", the
        taken reward's slot "state=equipped" with the item line's tier (1-10), a gear-table name and a Warden Facet
        (active=1), and the other four slots "state=empty" (before SCREEN-TABS no sheet line exists: RED);
    S4. the tab walk, in order, before Queue: character, build, character, gear, key, queue (before SCREEN-TABS there
        is no gear, key or queue tab: RED);
    S5. "SCREEN seats total=1 human=warden bots=none" (the Queue tab's seats; --solo fills no bot seat);
    S6. Queue leads to the run: "SCREEN queue dungeon=hollow-vault key=3 clause=" after "SCREEN tab queue", then a
        "RUN start ... dungeon=hollow-vault key=3 " line (SimDriver starts the queued run, --requeue).
  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/ScreenTabs-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing, or another launch holds the lock; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 10000
)
$SceneName = 'ScreenTabs-SceneTest'
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

$t = Invoke-Arena $Ticks @('--solo', '--requeue')
Write-SceneLog 'TABS' $t.Lines
$lines = $t.Lines
if ($t.Code -ne 0) { $failures.Add("tabs: godot exited $($t.Code) (expected 0)") }
$err = First $lines '^SCREEN error '
if ($err) { $failures.Add("tabs: screen script error: '$err'") }

$open = First $lines '^SCREEN open '
$queue = First $lines '^SCREEN queue dungeon='
$window = @()
if (-not $open) {
    $failures.Add('tabs: no "SCREEN open" line (the run-end take did not open the screen)')
} else {
    $iOpen = [array]::IndexOf($lines, $open)
    $iEnd = if ($queue) { [array]::IndexOf($lines, $queue) } else { $lines.Count - 1 }
    if ($iEnd -gt $iOpen) { $window = @($lines[$iOpen..$iEnd]) }
}

# S2 + S4: the tab walk between the open and the Queue press.
$tabs = @($window | Where-Object { $_ -match '^SCREEN tab \w+ class=' } | ForEach-Object { ($_ -split ' ')[2] })
$want = @('character', 'build', 'character', 'gear', 'key', 'queue')
if ($tabs.Count -eq 0 -or $tabs[0] -ne 'character') {
    $failures.Add("tabs: the screen did not open on Character (first tab line '$($tabs | Select-Object -First 1)')")
}
if (($tabs -join ',') -ne ($want -join ',')) {
    $failures.Add("tabs: the tab walk before Queue is '$($tabs -join ',')', expected '$($want -join ',')'")
}

# S3: the Character tab's sheet shows the taken reward.
$take = First $lines '^HUD runend take n=1 id=reward-(weapon|armor|charm|ring|keystone)-'
$sheetHead = First $window '^SCREEN sheet class=warden key=2 marks=0 ascended=0$'
if (-not $sheetHead) { $failures.Add('tabs: no "SCREEN sheet class=warden key=2 marks=0 ascended=0" line after the open') }
if (-not $take) {
    $failures.Add('tabs: no "HUD runend take n=1 id=reward-<slot>-..." line (no choice-of-three reward was taken)')
} else {
    $null = $take -match '^HUD runend take n=1 id=reward-(\w+)-'
    $slot = $Matches[1]
    $item = First $window "^SCREEN item slot=$slot tier=(\d+) "
    $tier = if ($item -and $item -match " tier=(\d+) ") { [int]$Matches[1] } else { -1 }
    $equipped = First $window "^SCREEN sheet slot=$slot state=equipped tier=$tier name=`"[^`"]+`" facet=warden\.$slot\.\S+ active=1$"
    if ($tier -lt 1 -or $tier -gt 10) { $failures.Add("tabs: the taken '$slot' item's tier is not 1-10: '$item'") }
    if (-not $equipped) { $failures.Add("tabs: the sheet does not show the taken '$slot' item at tier $tier with a name and a Warden Facet") }
    $empty = @($window | Where-Object { $_ -match '^SCREEN sheet slot=(\w+) state=empty$' -and $Matches[1] -ne $slot })
    if ($empty.Count -ne 4) { $failures.Add("tabs: expected the other 4 slots 'state=empty' on the sheet, got $($empty.Count)") }
}

# S5: the Queue tab's seats.
$seats = First $window '^SCREEN seats total=1 human=warden bots=none$'
if (-not $seats) { $failures.Add("tabs: no 'SCREEN seats total=1 human=warden bots=none' line (got '$(First $window '^SCREEN seats ')')") }

# S6: Queue, pressed on the Queue tab, starts the run at the stepped key.
$run = $null
if (-not $queue -or $queue -notmatch '^SCREEN queue dungeon=hollow-vault key=3 clause=$') {
    $failures.Add("tabs: no 'SCREEN queue dungeon=hollow-vault key=3 clause=' line (got '$queue')")
} else {
    $iQueue = [array]::IndexOf($lines, $queue)
    $run = First @($lines[$iQueue..($lines.Count - 1)]) '^RUN start '
    if (-not $run -or $run -notmatch ' dungeon=hollow-vault key=3 ') { $failures.Add("tabs: Queue did not start the run at key 3 (got '$run')") }
}

Write-Output '--- SCREEN-TABS (f-08279d82): open on Character, tab walk, sheet, seats, Queue -> run ---'
foreach ($l in @($open, $sheetHead, $equipped, $seats, $queue, $run)) {
    if ($l) { Write-Output $l }
}
Write-Output ("tab walk: " + ($tabs -join ','))

Complete-Scene "ticks=$Ticks"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
