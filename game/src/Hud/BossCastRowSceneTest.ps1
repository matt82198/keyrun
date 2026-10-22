<#
.SYNOPSIS
    Lane BOSS-CAST-ROW (HUD-DECLUTTER, Matt 2026-10-19: "reduce screen clutter essentially"). Reads the HUD's own
    "HUD unitframe rows" proof lines of one default autorun (the human Warden plus bots, through the boss pull) and
    asserts the boss unit frame keeps no blank cast row while the boss is idle:

      a. idle collapses: every boss rows line with cast=none has the next element directly under the health bar (the
         Break bar row's top, or the frame's bottom edge, at most 4 px under the health bar's bottom), and at least one
         such line carries a Break bar row (the Break bar sits directly under health);
      b. casting shows the row: at least one boss rows line with a cast row and a non-empty spell name, the cast row
         directly under the health bar, the Break bar row (if any) under the cast row, all inside the frame;
      c. no jump during a live cast: every boss rows line for one cast (same start tick) has the same cast row.

    On origin/main bc65a05 the frame reserves its 60 px cast-row height while idle (the Break bar at 58, 21 px under
    the health bar's bottom at 37): a fails (RED).

.DESCRIPTION
    Three ways in:
      -Lines <string[]>   the output lines of one autorun; the failures come back as strings, nothing is printed.
      -LogPath <file>     saved godot stdout of an autorun; prints each failure, then PASS or FAIL, exit 0 or 1.
      (neither)           runs the autorun itself (headless, --fixed-fps 60, --autorun --ticks <Ticks>) and checks it.
#>
param(
    [string[]]$Lines,
    [string]$LogPath,
    [int]$Ticks = 13000  # f-3dcc702c: pull to 100% forces puts the boss at tick 9746 and run_end at 12294 (was 7000)
)
$ErrorActionPreference = 'Stop'

function ConvertTo-Row([string]$s) {
    if ($s -eq 'none') { return $null }
    $p = $s -split '-'
    return [pscustomobject]@{ Top = [int]$p[0]; Bottom = [int]$p[1] }
}

function Get-BossCastRowFailures([string[]]$all) {
    $out = New-Object System.Collections.Generic.List[string]
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($l in $all) {
        if ($l -match '^HUD unitframe rows kind=boss actor=(\d+) height=(\d+) hp=(\d+)-(\d+) cast=(none|\d+-\d+) start=(-|\d+) spell="([^"]*)" break=(none|\d+-\d+) tick=(\d+)$') {
            $rows.Add([pscustomobject]@{ Actor = [int]$Matches[1]; Height = [int]$Matches[2]; HpBottom = [int]$Matches[4];
                Cast = (ConvertTo-Row $Matches[5]); Start = $Matches[6]; Spell = $Matches[7]; Break = (ConvertTo-Row $Matches[8]); Line = $l })
        }
    }
    if ($rows.Count -eq 0) {
        $out.Add('boss-cast-row a: no "HUD unitframe rows kind=boss" line (no boss frame layout to check)')
        return , $out
    }
    # a. idle: nothing reserved between the health bar and the next element.
    $idle = @($rows | Where-Object { $null -eq $_.Cast })
    if ($idle.Count -eq 0) { $out.Add('boss-cast-row a: no idle boss frame line (cast=none)') }
    foreach ($r in $idle) {
        $next = if ($r.Break) { $r.Break.Top } else { $r.Height }
        if ($next - $r.HpBottom -gt 4) { $out.Add("boss-cast-row a: idle frame keeps $($next - $r.HpBottom) px under the health bar (blank cast row): '$($r.Line)'") }
    }
    if (-not ($idle | Where-Object { $_.Break })) { $out.Add('boss-cast-row a: no idle boss frame line with a Break bar row') }
    # b. casting: the row shows with the spell name, directly under health.
    $casting = @($rows | Where-Object { $_.Cast })
    if ($casting.Count -eq 0) { $out.Add('boss-cast-row b: no casting boss frame line (the boss never cast on its frame)') }
    foreach ($r in $casting) {
        if ($r.Spell -eq '') { $out.Add("boss-cast-row b: cast row without a spell name: '$($r.Line)'") }
        if ($r.Cast.Top - $r.HpBottom -gt 4 -or $r.Cast.Top -lt $r.HpBottom) { $out.Add("boss-cast-row b: cast row not directly under the health bar: '$($r.Line)'") }
        $last = $r.Cast.Bottom
        if ($r.Break) {
            if ($r.Break.Top -lt $r.Cast.Bottom) { $out.Add("boss-cast-row b: Break bar row overlaps the cast row: '$($r.Line)'") }
            $last = $r.Break.Bottom
        }
        if ($r.Height -lt $last) { $out.Add("boss-cast-row b: rows spill out of the frame: '$($r.Line)'") }
    }
    # c. the cast row holds still for the whole cast.
    foreach ($g in ($casting | Group-Object { "$($_.Actor):$($_.Start)" })) {
        $shapes = @($g.Group | ForEach-Object { "$($_.Cast.Top)-$($_.Cast.Bottom)" } | Sort-Object -Unique)
        if ($shapes.Count -gt 1) { $out.Add("boss-cast-row c: the cast row moved during cast $($g.Name): $($shapes -join ',')") }
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
    if ($rc -eq 2 -and $text -match 'Godot binary not found') { Write-Output 'BossCastRowSceneTest: CANNOT RUN (Godot binary not found)'; exit 2 }
    $all = @($text -split "`r?`n")
    $f = Get-BossCastRowFailures $all
    if ($rc -ne 0) { $f.Add("boss-cast-row: godot exited $rc (expected 0)") }
} elseif ($LogPath) {
    $f = Get-BossCastRowFailures @(Get-Content -LiteralPath $LogPath)
} else {
    return (Get-BossCastRowFailures @($Lines))
}
foreach ($x in $f) { Write-Output $x }
if ($f.Count -gt 0) { Write-Output 'BossCastRowSceneTest: FAIL'; exit 1 }
Write-Output 'BossCastRowSceneTest: PASS'
exit 0
