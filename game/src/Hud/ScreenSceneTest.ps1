<#
.SYNOPSIS
  Headless scene test for the 3D between-runs screen (ENGINE-PLAN phase 3, lane 3D; BIBLE s7 Ash and crafting
  rerolls, s8 Clause pick, s10 screen): run end -> take a reward -> the screen -> reroll x3 -> key up -> Clause -> Queue.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 once and reads the screen's own
  "SCREEN ..." lines (printed under --autorun). After the run ends, Screen.cs pushes keys through the real input
  path one per frame: 1 (the run-end panel takes reward 1 and opens the screen), R, R, R, Right, 2, Enter.

    -- --autorun --ticks 600 --solo (pull 1, the Choir, solo Warden; the lone Warden wipes, so the run ends
       depleted inside the tick budget; the 3H bot party may still be fighting at tick 600;
       480 -> 600 by ROOM-LOCK-IN D27: the Warden dies at 390, depleted run_end at 491):
      1. exit 0 and "AUTORUN done ticks=600";
      2. "SCREEN bank result=depleted ash=30 total=30" (Ash banked win or lose, before any reward is taken);
      3. "HUD runend take n=1 id=reward-<slot>-..." and "SCREEN open ash=30 runs=1 since="+30 Ash, <slot> tier <t> (new)"";
      4. "SCREEN character class=Warden" and five "SCREEN ability key=<1..5> name=..." lines;
      5. "SCREEN item slot=<slot> tier=<t> ... charges=2 selected=1" for the taken reward;
      6. two "SCREEN reroll slot=<slot> ok=1 facet=warden.<slot>.* charges=1|0" lines (the class's listed pool for
         that slot), then "SCREEN reroll slot=<slot> ok=0 charges=0 refused=..." (the third is refused), each followed
         by the item line showing the charges left: 1, 0, 0;
      7. "SCREEN key level=3 locked=0" (the stepper moved from key 2), with three distinct Clause offers;
      8. "SCREEN clause n=2 ... picked=1" for key 3, then "SCREEN queue dungeon=hollow-vault key=3 clause=<that id>".
      9. "SCREEN queue caption="Enter starts the next run at this key and Clause."" (lane HUD-FU: the one line drawn
         under the Queue box, Screen.QueueCaption, says what the host does since 3I; never "the host does not read
         the queued key and Clause").
  Before 3D there is no screen and none of 2-8 appear (RED). Before HUD-FU there is no caption line: 9 fails (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Hud/ScreenSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 600
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

# --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
$guard = (3 * $Ticks) + 240
$ErrorActionPreference = 'Continue'
$text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks --solo 2>&1 | Out-String
$rc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($rc -eq 2 -and $text -match 'Godot binary not found') {
    Write-Output $text
    Write-Output 'ScreenSceneTest: CANNOT RUN (Godot binary not found)'
    exit 2
}
$lines = @($text -split "`r?`n")

function First([string]$pattern) {
    return $lines | Where-Object { $_ -match $pattern } | Select-Object -First 1
}
function IndexOf($line) {
    if (-not $line) { return -1 }
    return [array]::IndexOf($lines, $line)
}

if ($rc -ne 0) { $failures.Add("godot exited $rc (expected 0)") }
$summary = First "^AUTORUN done ticks=$Ticks "
if (-not $summary) { $failures.Add("no 'AUTORUN done ticks=$Ticks' summary") }
$err = First '^SCREEN error '
if ($err) { $failures.Add("screen script error: '$err'") }

$bank = First '^SCREEN bank result=depleted ash=30 total=30$'
if (-not $bank) { $failures.Add('no "SCREEN bank result=depleted ash=30 total=30" line') }

$take = First '^HUD runend take n=1 id=reward-(weapon|armor|charm|ring|keystone)-'
$slot = $null
if (-not $take) {
    $failures.Add('no "HUD runend take n=1 id=reward-<slot>-..." line (the run-end panel did not take reward 1)')
} else {
    $null = $take -match '^HUD runend take n=1 id=reward-(\w+)-'
    $slot = $Matches[1]
}
$open = First '^SCREEN open ash=30 runs=1 since="\+30 Ash, (\w+) tier (\d+) \(new\)"$'
if (-not $open) {
    $failures.Add('no "SCREEN open ash=30 runs=1 since=""+30 Ash, <slot> tier <t> (new)""" line')
} elseif ($slot -and $open -notmatch "since=`"\+30 Ash, $slot tier ") {
    $failures.Add("the since-last-run line names another slot than the taken '$slot': '$open'")
}
if ($take -and $open -and (IndexOf $open) -lt (IndexOf $bank)) { $failures.Add('the screen opened before the Ash was banked') }

$class = First '^SCREEN character class=Warden$'
if (-not $class) { $failures.Add('no "SCREEN character class=Warden" line') }
$abilities = @($lines | Where-Object { $_ -match '^SCREEN ability key=[1-5] name="[^"]+"$' })
if ($abilities.Count -ne 5) { $failures.Add("expected 5 'SCREEN ability key=<1..5>' lines, got $($abilities.Count)") }

$items = @()
$rerolls = @()
if ($slot) {
    $items = @($lines | Where-Object { $_ -match "^SCREEN item slot=$slot tier=\d+ facet=\S+ charges=\d selected=1$" })
    $rerolls = @($lines | Where-Object { $_ -match "^SCREEN reroll slot=$slot " })
    if ($items.Count -lt 1 -or $items[0] -notmatch ' charges=2 ') {
        $failures.Add("the first item line for '$slot' does not show 2 charges left: '$($items | Select-Object -First 1)'")
    }
    if ($rerolls.Count -ne 3) {
        $failures.Add("expected 3 'SCREEN reroll slot=$slot' lines, got $($rerolls.Count)")
    } else {
        if ($rerolls[0] -notmatch "^SCREEN reroll slot=$slot ok=1 facet=warden\.$slot\.\w+ charges=1$") { $failures.Add("reroll 1 is not a pool draw with 1 charge left: '$($rerolls[0])'") }
        if ($rerolls[1] -notmatch "^SCREEN reroll slot=$slot ok=1 facet=warden\.$slot\.\w+ charges=0$") { $failures.Add("reroll 2 is not a pool draw with 0 charges left: '$($rerolls[1])'") }
        if ($rerolls[2] -notmatch "^SCREEN reroll slot=$slot ok=0 charges=0 refused=`"[^`"]+`"$") { $failures.Add("reroll 3 was not refused: '$($rerolls[2])'") }
    }
    # The screen shows charges left after each reroll: the item line right after each reroll line.
    $expected = @(1, 0, 0)
    for ($i = 0; $i -lt [Math]::Min(3, $rerolls.Count); $i++) {
        $after = $lines[((IndexOf $rerolls[$i]) + 1)]
        if ($after -notmatch "^SCREEN item slot=$slot tier=\d+ facet=\S+ charges=$($expected[$i]) selected=1$") {
            $failures.Add("after reroll $($i + 1) the screen does not show $($expected[$i]) charges left: '$after'")
        }
    }
}

$key2 = First '^SCREEN key level=2 locked=0$'
$key3 = First '^SCREEN key level=3 locked=0$'
if (-not $key2) { $failures.Add('no "SCREEN key level=2 locked=0" line (the screen opens at the run''s key)') }
if (-not $key3) { $failures.Add('no "SCREEN key level=3 locked=0" line (the key stepper did not step up)') }

$queue = First '^SCREEN queue dungeon=hollow-vault key=3 clause=(echo|tide|hush|pact)$'
$picked = $null
if (-not $queue) {
    $failures.Add('no "SCREEN queue dungeon=hollow-vault key=3 clause=<id>" line')
} else {
    # The last Clause band before Queue: three distinct offers, n=2 picked, and Queue carries that id.
    $iQueue = IndexOf $queue
    $band = @($lines[0..$iQueue] | Where-Object { $_ -match '^SCREEN clause n=[123] ' } | Select-Object -Last 3)
    $ids = @($band | ForEach-Object { if ($_ -match ' id=(\w+) ') { $Matches[1] } })
    if (($ids | Sort-Object -Unique).Count -ne 3) { $failures.Add("the Clause band does not offer three distinct Clauses: $($band -join ' | ')") }
    $picked = $band | Where-Object { $_ -match '^SCREEN clause n=2 id=(\w+) name="[^"]+" picked=1$' } | Select-Object -First 1
    if (-not $picked) {
        $failures.Add("Clause offer 2 is not the pick before Queue: $($band -join ' | ')")
    } else {
        $null = $picked -match ' id=(\w+) '
        if ($queue -notmatch "clause=$($Matches[1])$") { $failures.Add("Queue carries another Clause than the pick: '$queue' vs '$picked'") }
    }
}

$captions = @($lines | Where-Object { $_ -match '^SCREEN queue caption=' })
$caption = First '^SCREEN queue caption="Enter starts the next run at this key and Clause\."$'
if (-not $caption) {
    $failures.Add("no 'SCREEN queue caption=""Enter starts the next run at this key and Clause.""' line (got: $($captions -join ' | '))")
}
$stale = $captions | Where-Object { $_ -match 'does not read|no Clause' } | Select-Object -First 1
if ($stale) { $failures.Add("the Queue caption still says the host ignores the queue: '$stale'") }

Write-Output '--- asserted SCREEN lines ---'
foreach ($l in @($summary, $bank, $take, $open, $class) + $abilities + $items + $rerolls + @($key2, $key3, $picked, $queue, $caption)) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "ScreenSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "ScreenSceneTest: PASS ticks=$Ticks slot=$slot"
exit 0
