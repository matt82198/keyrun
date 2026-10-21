<#
.SYNOPSIS
  Headless scene test for the 3D between-runs screen (ENGINE-PLAN phase 3, lane 3D; BIBLE s7 Ash and crafting
  rerolls, s8 Clause pick, s10 screen): run end -> take a reward -> the screen -> reroll x3 -> key up -> Clause -> Queue.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 once and reads the screen's own
  "SCREEN ..." lines (printed under --autorun). After the run ends, Screen.cs pushes keys through the real input
  path one per frame: 1 (the run-end panel takes reward 1 and opens the screen), R, R, R, Right, 2, Enter.

    -- --autorun --ticks 9800 --solo (pull 1, the Choir, solo Warden; 480 -> 600 by ROOM-LOCK-IN D27, when the
       Warden's wipe ended the run depleted at 491; 600 -> 9800 by R-446-CI, f-ea16265e: under R-BACKFILL-2, BIBLE
       s9.1, a wipe is a release, not a run end, so the lone Warden dies, respawns and the run goes on until the timer
       cap, Timer.CapTicks, ends it depleted and uncleared at 9600, titled "Out of time -- key stays at +2"; 9800 keeps
       the 109 ticks the scene kept past the end before, pinned headless by
       tools/Keyrun.Playtest.Tests/ScreenSceneWipeReleaseRunTests.cs; the same run as HudSceneTest run A):
      1. exit 0 and "AUTORUN done ticks=9800";
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
     10. "SCREEN help visible=0" between the open and Queue, "SCREEN help visible=1" after Queue (lane SCREEN-OVERFLOW:
         the in-run help line no longer shows through the screen);
     11. "SCREEN fit clause n=<1..3> lines=<n> widest=<px> box=<px>" with widest <= box (each Clause description is
         wrapped inside its card), and "SCREEN fit caption lines=<n> right=<px> viewport=<px>" with right <= viewport.
     12. "SCREEN queue facets applied=1 skipped=0", "SCREEN queue facet actor=1 slot=<slot> facet=<the item's facet>
         ability=<id> <stats> base <stats>" with the queued stats differing from the base (lane FACET-APPLIED,
         BIBLE s7: before it no Facet patch reached any run and none of these lines exist: RED), and no
         "SCREEN run facet ..." line (that line reports a run the Queue started, which only --requeue does).
  Before 3D there is no screen and none of 2-8 appear (RED). Before HUD-FU there is no caption line: 9 fails (RED).
  Before SCREEN-OVERFLOW 10-11 fail (RED); with the wrap removed the fit lines report widest > box (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Hud/ScreenSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 9800
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

# Lane SCREEN-OVERFLOW (1280x720 visual audit): the in-run help line is hidden while the screen is up and shown again
# after Queue; every Clause description fits inside its card (widest wrapped line <= the card's text width); the Queue
# caption's right edge is at or inside the viewport.
$helpHidden = $null
$helpShown = $null
if ($open -and $queue) {
    $iOpen = IndexOf $open
    $iQueue = IndexOf $queue
    $helpHidden = $lines[$iOpen..$iQueue] | Where-Object { $_ -match '^SCREEN help visible=0$' } | Select-Object -First 1
    $helpShown = $lines[$iQueue..($lines.Count - 1)] | Where-Object { $_ -match '^SCREEN help visible=1$' } | Select-Object -First 1
}
if (-not $helpHidden) { $failures.Add('no "SCREEN help visible=0" line between the screen opening and Queue (the in-run help line shows through the screen)') }
if (-not $helpShown) { $failures.Add('no "SCREEN help visible=1" line after Queue (the help line does not come back for the run)') }

$fits = @($lines | Where-Object { $_ -match '^SCREEN fit clause n=[123] ' })
if ($fits.Count -lt 3) { $failures.Add("expected 'SCREEN fit clause n=<1..3> lines=<n> widest=<px> box=<px>' lines, got $($fits.Count)") }
foreach ($f in $fits) {
    if ($f -notmatch '^SCREEN fit clause n=[123] lines=(\d+) widest=(\d+) box=(\d+)$') {
        $failures.Add("malformed clause fit line: '$f'")
    } elseif ([int]$Matches[2] -gt [int]$Matches[3]) {
        $failures.Add("a Clause description runs past its card: '$f'")
    }
}

$capFit = First '^SCREEN fit caption '
if (-not $capFit) {
    $failures.Add("no 'SCREEN fit caption lines=<n> right=<px> viewport=<px>' line")
} elseif ($capFit -notmatch '^SCREEN fit caption lines=(\d+) right=(\d+) viewport=(\d+)$') {
    $failures.Add("malformed caption fit line: '$capFit'")
} elseif ([int]$Matches[2] -gt [int]$Matches[3]) {
    $failures.Add("the Queue caption is cut off at the window edge: '$capFit'")
}

# Lane BIBLE-TALENTS-UI (BIBLE s6 "one fork of two or three mutually exclusive options; 12-15 choices per class";
# "Respec is free on the screen, always"): the autorun script presses T, 1, Down, 2, Backspace, 1, T before Enter.
# Expected, in order between the screen opening and Queue: the panel opens on the Warden's 5 forks with 0 picks, 12-15
# options in all; fork 1 (warden.strike) takes Rend; fork 2 takes its second side (picks=2); the respec clears both and
# leaves Ash at the banked 30; fork 2 takes its first side (picks=1); the panel closes holding picks=1. Every option
# card's wrapped description fits its card (widest <= box, not clipped, text_bottom <= card_bottom) and the panel stays
# inside the 1280x720 viewport. Before this lane there is no talent panel and every line below is missing (RED).
$talentLines = @()
if ($open -and $queue) {
    $talentLines = @($lines[(IndexOf $open)..(IndexOf $queue)] | Where-Object { $_ -match '^SCREEN (talents?|fit talents?) ' })
}
$tOpen = $talentLines | Where-Object { $_ -match '^SCREEN talents open=1 class=warden forks=5 picks=0 applied=0$' } | Select-Object -First 1
if (-not $tOpen) { $failures.Add("no 'SCREEN talents open=1 class=warden forks=5 picks=0 applied=0' line before Queue (got: $($talentLines | Select-Object -First 2))") }
$forkRows = @($talentLines | Where-Object { $_ -match '^SCREEN talent fork=[1-5] ability=warden\.\w+ options=[\w,-]+ picked=-$' })
$optionCount = 0
foreach ($r in $forkRows) { if ($r -match ' options=([\w,-]+) ') { $optionCount += @($Matches[1] -split ',').Count } }
if ($forkRows.Count -ne 5) { $failures.Add("expected 5 unpicked 'SCREEN talent fork=<n>' rows, got $($forkRows.Count)") }
if ($optionCount -lt 12 -or $optionCount -gt 15) { $failures.Add("the Warden's forks offer $optionCount options in all (bible: 12-15)") }
$fork2 = $forkRows | Where-Object { $_ -match '^SCREEN talent fork=2 ' } | Select-Object -First 1
$f2opts = @()
if ($fork2 -and $fork2 -match ' ability=(\S+) options=([\w,-]+) ') { $f2ability = $Matches[1]; $f2opts = @($Matches[2] -split ',') }
$picks = @($talentLines | Where-Object { $_ -match '^SCREEN talent (pick|respec) ' })
$expectPicks = @(
    '^SCREEN talent pick fork=1 ability=warden\.strike option=rend picks=1$',
    $(if ($f2opts.Count -ge 2) { "^SCREEN talent pick fork=2 ability=$([regex]::Escape($f2ability)) option=$($f2opts[1]) picks=2$" } else { '^NEVER$' }),
    '^SCREEN talent respec picks=0 ash=30 ash_after=30$',
    $(if ($f2opts.Count -ge 1) { "^SCREEN talent pick fork=2 ability=$([regex]::Escape($f2ability)) option=$($f2opts[0]) picks=1$" } else { '^NEVER$' })
)
if ($picks.Count -ne $expectPicks.Count) {
    $failures.Add("expected $($expectPicks.Count) talent pick/respec lines, got $($picks.Count): $($picks -join ' | ')")
} else {
    for ($i = 0; $i -lt $expectPicks.Count; $i++) {
        if ($picks[$i] -notmatch $expectPicks[$i]) { $failures.Add("talent step $($i + 1) is '$($picks[$i])', expected /$($expectPicks[$i])/") }
    }
}
# Lane TALENT-PICKS: applied= is the number of talented abilities the next Queue hands the run. It was pinned at 0
# while the picks never reached the run (the bug); with the one kept pick it is 1.
$tClose = $talentLines | Where-Object { $_ -match '^SCREEN talents open=0 class=warden forks=5 picks=1 applied=1$' } | Select-Object -First 1
if (-not $tClose) { $failures.Add("no 'SCREEN talents open=0 class=warden forks=5 picks=1 applied=1' line (the build is not kept when the panel closes, or its pick is not applied)") }

# Lane TALENT-PICKS (BIBLE s6): Queue hands the kept pick to the run. Right after the queue line the request's own
# RunOptions (what SimDriver passes to RunRoot.CreateRun) carries one talented ability for the human (actor 1): the
# fork-2 ability, its queued stats differing from the base the run would otherwise play. Before the lane the request
# had no loadout and none of these lines exist (RED).
$qTalents = $null
$qTalent = $null
if ($queue) {
    $qi = IndexOf $queue
    if ($qi + 1 -lt $lines.Count) { $qTalents = $lines[$qi + 1] }
    $qTalent = $lines | Where-Object { $_ -match '^SCREEN queue talent actor=' } | Select-Object -First 1
}
if ($qTalents -notmatch '^SCREEN queue talents applied=1$') { $failures.Add("the line after Queue is '$qTalents', expected 'SCREEN queue talents applied=1'") }
$talentStats = '(shape=\S+ cooldown=\S+ cost=\S+ damage=\S+)'
if (-not $qTalent) {
    $failures.Add("no 'SCREEN queue talent actor=1 ability=...' line (the picked talent is not in the queued run options)")
} elseif ($qTalent -notmatch "^SCREEN queue talent actor=1 ability=$([regex]::Escape([string]$f2ability)) $talentStats base $talentStats$") {
    $failures.Add("malformed or wrong queued talent line: '$qTalent' (expected actor=1 ability=$f2ability)")
} elseif ($Matches[1] -eq $Matches[2] -or $Matches[2] -match '=-') {
    $failures.Add("the queued talent does not change the ability from its base: '$qTalent'")
}
$cardFits = @($talentLines | Where-Object { $_ -match '^SCREEN fit talent fork=' })
if ($cardFits.Count -ne $optionCount -or $cardFits.Count -eq 0) { $failures.Add("expected one 'SCREEN fit talent' line per option ($optionCount), got $($cardFits.Count)") }
foreach ($f in $cardFits) {
    if ($f -notmatch '^SCREEN fit talent fork=\d option=\d lines=(\d+) widest=(\d+) box=(\d+) clipped=(\d) text_bottom=(\d+) card_bottom=(\d+)$') {
        $failures.Add("malformed talent fit line: '$f'")
    } elseif ([int]$Matches[2] -gt [int]$Matches[3] -or $Matches[4] -ne '0' -or [int]$Matches[5] -gt [int]$Matches[6] -or [int]$Matches[1] -lt 1) {
        $failures.Add("a talent description does not fit its card: '$f'")
    }
}
$panelFit = $talentLines | Where-Object { $_ -match '^SCREEN fit talents ' } | Select-Object -First 1
if (-not $panelFit) {
    $failures.Add("no 'SCREEN fit talents right=<px> bottom=<px> viewport=<w>x<h>' line")
} elseif ($panelFit -notmatch '^SCREEN fit talents right=(\d+) bottom=(\d+) viewport=(\d+)x(\d+)$') {
    $failures.Add("malformed talent panel fit line: '$panelFit'")
} elseif ([int]$Matches[1] -gt [int]$Matches[3] -or [int]$Matches[2] -gt [int]$Matches[4]) {
    $failures.Add("the talent panel runs past the viewport: '$panelFit'")
}

# Lane FACET-APPLIED (BIBLE s7 "Facets retag or reshape an ability"): the equipped item's Facet reaches the run. The
# request's own RunOptions carries the human's kit with the Facet patch applied (content/apply.ts applyLoadout order:
# talents, then facets in slot order), the object SimDriver.StartRun passes to RunRoot.CreateRun. The Facet is the one
# the screen last showed on the taken item (after both rerolls). Before the lane the facet patch was read nowhere and
# none of these lines exist (RED).
$facetStats = '(shape=\S+ range=\S+ cast=\S+ cooldown=\S+ charges=\S+ cost=\S+ effects=\S+ tags=\S+)'
$equippedFacet = $null
if ($items.Count -gt 0 -and $items[-1] -match ' facet=(\S+) ') { $equippedFacet = $Matches[1] }
$qFacets = $lines | Where-Object { $_ -match '^SCREEN queue facets ' } | Select-Object -First 1
$qFacet = $lines | Where-Object { $_ -match '^SCREEN queue facet actor=' } | Select-Object -First 1
$rFacet = $lines | Where-Object { $_ -match '^SCREEN run facet actor=' } | Select-Object -First 1
if (-not $qFacets -or $qFacets -notmatch '^SCREEN queue facets applied=1 skipped=0$') { $failures.Add("expected 'SCREEN queue facets applied=1 skipped=0' (the equipped Facet is not in the queued run options), got '$qFacets'") }
$facetAbility = $null
$queuedStats = $null
if (-not $qFacet) {
    $failures.Add("no 'SCREEN queue facet actor=1 slot=...' line (the equipped Facet does not patch the queued kit)")
} elseif ($qFacet -notmatch "^SCREEN queue facet actor=1 slot=$slot facet=$([regex]::Escape([string]$equippedFacet)) ability=(warden\.\w+) $facetStats base $facetStats$") {
    $failures.Add("malformed or wrong queued facet line: '$qFacet' (expected actor=1 slot=$slot facet=$equippedFacet)")
} else {
    $facetAbility = $Matches[1]
    $queuedStats = $Matches[2]
    if ($Matches[2] -eq $Matches[3]) { $failures.Add("the queued Facet does not change its ability from the base: '$qFacet'") }
}
# "SCREEN run facet ..." reports the run a Queue STARTED. Plain --autorun (no --requeue) never starts the queued run
# (SimDriver.StartQueuedRun), so the line must be absent here: a line would be a report about the old, ended run.
if ($rFacet) {
    $failures.Add("a 'SCREEN run facet' line without a started run (it reads a run the Queue did not start): '$rFacet'")
}

Write-Output '--- asserted SCREEN lines ---'
foreach ($l in @($summary, $bank, $take, $open, $class) + $abilities + $items + $rerolls + @($key2, $key3, $picked, $queue, $caption, $helpHidden, $helpShown) + $fits + @($capFit, $tOpen) + $forkRows + $picks + @($tClose, $qTalents, $qTalent) + $cardFits + @($panelFit, $qFacets, $qFacet, $rFacet)) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "ScreenSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "ScreenSceneTest: PASS ticks=$Ticks slot=$slot"
exit 0
