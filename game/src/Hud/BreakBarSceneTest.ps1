<#
.SYNOPSIS
    Lane BREAK-BAR (design/BIBLE.md section 9, The Sundered Twins, mechanic 2: the Twins "share one Break bar").
    Reads the HUD's own "HUD breakbar" proof lines of one default autorun (the human Warden plus bots, through the
    boss pull) and asserts the boss unit frame draws the Twins' shared Break bar from the sim's value:

      a. the bar shows on the boss frame once the Twins are up: a "HUD breakbar boss=900 bar=B ... fill=0
         phase=filling" line after the boss_add_spawn EVENTs, and every breakbar line names that one bar B (one bar,
         shared, never one per twin);
      b. it fills as the sim value rises: at least two "phase=filling" lines at different ticks with fill above 0,
         each fill higher than the last, and every line's fill equal to (max - current) * 1000 / max;
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
    $first = $bars[0]
    if ($spawnAt -lt 0) { $out.Add('break-bar a: no boss_add_spawn EVENT (the Twins never came up)') }
    elseif ($first.Index -lt $spawnAt) { $out.Add("break-bar a: a Break bar line before the Twins spawned: '$($first.Line)'") }
    if ($first.Boss -ne 900 -or $first.Fill -ne 0 -or $first.Phase -ne 'filling') { $out.Add("break-bar a: first line is not an empty boss=900 bar: '$($first.Line)'") }
    $ids = @($bars | ForEach-Object { $_.Bar } | Sort-Object -Unique)
    if ($ids.Count -ne 1) { $out.Add("break-bar a: more than one bar on the boss frame: $($ids -join ',')") }
    $barId = $first.Bar
    # b. fills as the sim value rises; the fill is the sim's value.
    foreach ($b in $bars) {
        $want = if ($b.Phase -eq 'broken') { 1000 } elseif ($b.Max -le 0) { 0 } else { [int][math]::Floor(($b.Max - [math]::Min($b.Current, $b.Max)) * 1000 / $b.Max) }
        if ($b.Fill -ne $want) { $out.Add("break-bar b: fill $($b.Fill) is not the sim value (want $want): '$($b.Line)'") }
    }
    $rising = New-Object System.Collections.Generic.List[object]
    foreach ($b in $bars) {
        if ($b.Phase -eq 'broken') { break }
        if ($b.Fill -gt 0) { $rising.Add($b) }
    }
    if ($rising.Count -lt 2) { $out.Add("break-bar b: fewer than two filling lines above 0 before the break ($($rising.Count))") }
    for ($k = 1; $k -lt $rising.Count; $k++) {
        if ($rising[$k].Fill -le $rising[$k - 1].Fill -or $rising[$k].Tick -le $rising[$k - 1].Tick) {
            $out.Add("break-bar b: fill did not rise: '$($rising[$k - 1].Line)' then '$($rising[$k].Line)'")
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
foreach ($x in $f) { Write-Output $x }
if ($f.Count -gt 0) { Write-Output 'BreakBarSceneTest: FAIL'; exit 1 }
Write-Output 'BreakBarSceneTest: PASS'
exit 0
