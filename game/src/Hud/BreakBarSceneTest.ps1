<#
.SYNOPSIS
    Lane BREAK-BAR (design/BIBLE.md section 9, The Sundered Twins, mechanic 2: the Twins "share one Break bar").
    Reads the HUD's own "HUD breakbar" proof lines of one default autorun (the human Warden plus bots, through the
    boss pull) and asserts the boss unit frame draws the Twins' shared Break bar from the sim's value:

      a. the bar shows on the boss frame once the Twins are up: a "HUD breakbar boss=900 bar=B ... fill=0
         phase=filling" line after the boss_add_spawn EVENTs, and every breakbar line names that one bar B (one bar,
         shared, never one per twin);
      b. it fills as the sim value rises: every line's fill equal to (max - current) * 1000 / max, the filling lines of
         each fill cycle rising at later ticks, and at least one cycle showing a fill above 0 before its break
         (R-BOSS-1, #561: 50 per interrupt on a 100 bar, so a cycle has one partial fill, 500; it was "at least two
         filling lines above 0 before the first break" under 34 per interrupt);
      Since R-BOSS-1 the bots kick the Twins at once, so the bar may break in the step it appears: a asks for an empty
      filling line after the spawn, not that the first line is empty.
      c. it flashes full on break: a stagger EVENT for bar B, then a "fill=1000 phase=broken" line at a later tick;
      d. it resets: a "fill=0 phase=filling" line after that broken line (the sim refilled the bar).

    On origin/main 8fd2fca nothing in the HUD reads EncounterState.BreakBars and no "HUD breakbar" line is printed:
    a, b, c and d all fail (RED).

.DESCRIPTION
    Three ways in:
      -Lines <string[]>   the output lines of one autorun; the failures come back as strings, nothing is printed.
      -LogPath <file>     saved godot stdout of an autorun; prints each failure, then PASS or FAIL, exit 0 or 1.
      (neither)           runs the autorun itself (headless, --fixed-fps 60, --autorun --ticks <Ticks>) and checks it.
#>
param(
    [string[]]$Lines,
    [string]$LogPath,
    [int]$Ticks = 7000
)
$ErrorActionPreference = 'Stop'

function Get-BreakBarFailures([string[]]$all) {
    $out = New-Object System.Collections.Generic.List[string]
    $spawnAt = -1
    $staggerAt = -1
    $bars = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $all.Count; $i++) {
        $l = $all[$i]
        if ($spawnAt -lt 0 -and $l -match '^EVENT \{.*"type":"boss_add_spawn"') { $spawnAt = $i }
        if ($l -match '^HUD breakbar boss=(\d+) bar=(\S+) tick=(\d+) current=(-?\d+) max=(\d+) fill=(\d+) phase=(filling|broken)$') {
            $bars.Add([pscustomobject]@{ Index = $i; Boss = [int]$Matches[1]; Bar = $Matches[2]; Tick = [int]$Matches[3];
                Current = [long]$Matches[4]; Max = [long]$Matches[5]; Fill = [int]$Matches[6]; Phase = $Matches[7]; Line = $l })
        }
    }
    if ($bars.Count -eq 0) {
        $out.Add('break-bar a: no "HUD breakbar" line (the boss frame draws no Break bar)')
        return , $out
    }
    # a. on the boss frame once the Twins are up; one shared bar.
    # R-BOSS-1 (#561): the bots kick the Break-bar enemy at once (BreakBarKick.cs) and two interrupts break the bar, so
    # the bar can break in the very step it appears (default run, seed 1: created and staggered at tick 3108, measured
    # headless by BreakBarSceneReplicaTests). The first line may therefore be the broken one; what a states (header) is
    # that the bar shows empty and filling on the boss frame after the Twins spawn, at any point after them.
    $first = $bars[0]
    if ($spawnAt -lt 0) { $out.Add('break-bar a: no boss_add_spawn EVENT (the Twins never came up)') }
    elseif ($first.Index -lt $spawnAt) { $out.Add("break-bar a: a Break bar line before the Twins spawned: '$($first.Line)'") }
    if ($first.Boss -ne 900) { $out.Add("break-bar a: first line is not the boss=900 frame's bar: '$($first.Line)'") }
    $emptyAfterSpawn = $bars | Where-Object { $_.Index -gt $spawnAt -and $_.Boss -eq 900 -and $_.Fill -eq 0 -and $_.Phase -eq 'filling' } | Select-Object -First 1
    if (-not $emptyAfterSpawn) { $out.Add('break-bar a: no empty "fill=0 phase=filling" boss=900 line after the Twins spawned') }
    $ids = @($bars | ForEach-Object { $_.Bar } | Sort-Object -Unique)
    if ($ids.Count -ne 1) { $out.Add("break-bar a: more than one bar on the boss frame: $($ids -join ',')") }
    $barId = $first.Bar
    # b. fills as the sim value rises; the fill is the sim's value.
    foreach ($b in $bars) {
        $want = if ($b.Phase -eq 'broken') { 1000 } elseif ($b.Max -le 0) { 0 } else { [int][math]::Floor(($b.Max - [math]::Min($b.Current, $b.Max)) * 1000 / $b.Max) }
        if ($b.Fill -ne $want) { $out.Add("break-bar b: fill $($b.Fill) is not the sim value (want $want): '$($b.Line)'") }
    }
    # R-BOSS-1 (#561) set twinsBreak to 50 per interrupt on a 100 bar (was 34): a fill cycle is exactly two interrupts,
    # so it shows at most one filling value above 0 (500) before the break. The old "at least two filling lines above
    # 0 before the first break" was the 34-per-interrupt shape (three interrupts, two partial fills) and no R-BOSS-1 bar
    # can print it. Kept: within every cycle (empty line to broken line) the filling lines rise, and at least one cycle
    # shows a partial fill above 0 before its break, so the bar visibly fills rather than only flipping to broken.
    $cycles = New-Object System.Collections.Generic.List[object]
    $cycle = New-Object System.Collections.Generic.List[object]
    foreach ($b in $bars) {
        if ($b.Phase -eq 'broken') { $cycles.Add($cycle); $cycle = New-Object System.Collections.Generic.List[object]; continue }
        if ($b.Fill -eq 0) { $cycle = New-Object System.Collections.Generic.List[object]; continue }
        $cycle.Add($b)
    }
    $partialCycles = @($cycles | Where-Object { $_.Count -ge 1 }).Count
    if ($partialCycles -lt 1) { $out.Add("break-bar b: no filling line above 0 before any break ($($cycles.Count) break(s) seen): the bar only flips to broken") }
    foreach ($rising in $cycles) {
        for ($k = 1; $k -lt $rising.Count; $k++) {
            if ($rising[$k].Fill -le $rising[$k - 1].Fill -or $rising[$k].Tick -le $rising[$k - 1].Tick) {
                $out.Add("break-bar b: fill did not rise: '$($rising[$k - 1].Line)' then '$($rising[$k].Line)'")
            }
        }
    }
    # c. flash full on break.
    for ($i = 0; $i -lt $all.Count; $i++) {
        if ($all[$i] -match ('^EVENT \{"actorIds":\[901,902\],"barId":"' + [regex]::Escape($barId) + '",.*"type":"stagger"')) { $staggerAt = $i; break }
    }
    $broken = $bars | Where-Object { $_.Phase -eq 'broken' -and $_.Fill -eq 1000 } | Select-Object -First 1
    if ($staggerAt -lt 0) { $out.Add("break-bar c: no stagger EVENT for $barId (the Twins' bar never broke in this run)") }
    if (-not $broken) { $out.Add('break-bar c: no "fill=1000 phase=broken" line (the bar never showed the break)') }
    elseif ($staggerAt -ge 0 -and $broken.Index -lt $staggerAt) { $out.Add("break-bar c: broken line before the stagger EVENT: '$($broken.Line)'") }
    # d. resets after the break.
    if ($broken) {
        $reset = $bars | Where-Object { $_.Index -gt $broken.Index -and $_.Phase -eq 'filling' -and $_.Fill -eq 0 } | Select-Object -First 1
        if (-not $reset) { $out.Add('break-bar d: no "fill=0 phase=filling" line after the break (the bar never reset)') }
    }
    return , $out
}

if (-not $LogPath -and -not $PSBoundParameters.ContainsKey('Lines')) {
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
    $wrapper = Join-Path $repo 'tools/godot/godot.ps1'
    $guard = (3 * $Ticks) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rc -eq 2 -and $text -match 'Godot binary not found') { Write-Output 'BreakBarSceneTest: CANNOT RUN (Godot binary not found)'; exit 2 }
    $all = @($text -split "`r?`n")
    $f = Get-BreakBarFailures $all
    if ($rc -ne 0) { $f.Add("break-bar: godot exited $rc (expected 0)") }
} elseif ($LogPath) {
    $f = Get-BreakBarFailures @(Get-Content -LiteralPath $LogPath)
} else {
    return (Get-BreakBarFailures @($Lines))
}
if ($f.Count -gt 0) {
    Write-Output "BreakBarSceneTest: FAIL ($($f.Count))"
    $f | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output 'BreakBarSceneTest: PASS'
exit 0
