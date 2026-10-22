<#
.SYNOPSIS
  HUD scene run: the ability tooltip on a held key (Matt 2026-10-22, "can we please get some ability tooltips and
  cooler names for the players"). The default run (seed 1, key 2, the human Warden in seat 1) replays
  game/fixtures/input/hold-key1.input-log.json: Enter starts the run, Key1 goes down at input tick 40 and up at 48
  (8 ticks = 400 ms at 20 Hz, past the 300 ms hold threshold, AbilityTooltipLayout.HoldMs).
  -- --replay-input game/fixtures/input/hold-key1.input-log.json

  AbilityBar.Tooltip.cs prints, under --replay-input, one line each time the shown tooltip changes. This test asserts:
    a. the first "HUD tooltip slot=" line is "HUD tooltip slot=1 name=Sundering Verdict rect=X,Y,W,H viewport=WxH
       lines=N fits=1 by=hold tick=T": the held key 1 shows the Warden's slot 1 under its themed display name, by the
       hold (headless has no hover), with at least the five tooltip lines;
    b. its rect lies inside the viewport (and fits=1: the bar's own check that every wrapped line fits the box and the
       box is clear of the party frames and the top bar);
    c. a later "HUD tooltip hidden tick=H" line with 1 <= H - T <= 4: the key came up 400 ms after the press, so the
       tooltip showed after the 300 ms threshold and hid on the release;
    d. no "by=hover" line (nothing hovered the bar).
  On origin/main 55579b37 the bar has no tooltip and prints no "HUD tooltip" line: a and c fail (RED).

.DESCRIPTION
  -Lines <string[]>  the output lines of the launch; the failures come back as strings (no launch).
  (none)             launches headless at --fixed-fps 60 and checks it; prints "HudTooltip-SceneTest: PASS" or FAIL.
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [string[]]$Lines
)

function Get-TooltipFailures([string[]]$all) {
    $out = New-Object System.Collections.Generic.List[string]
    $shown = @($all | Where-Object { $_ -match '^HUD tooltip slot=' })
    if ($shown.Count -eq 0) {
        $out.Add('tooltip a: no "HUD tooltip slot=" line (holding key 1 for 400 ms showed no tooltip)')
        return , $out
    }
    $re = '^HUD tooltip slot=1 name=Sundering Verdict rect=(-?\d+),(-?\d+),(\d+),(\d+) viewport=(\d+)x(\d+) lines=(\d+) fits=(\d) by=hold tick=(\d+)$'
    if ($shown[0] -notmatch $re) {
        $out.Add("tooltip a: first tooltip line is '$($shown[0])', want 'HUD tooltip slot=1 name=Sundering Verdict rect=X,Y,W,H viewport=WxH lines=N fits=1 by=hold tick=T'")
        return , $out
    }
    $x = [int]$Matches[1]; $y = [int]$Matches[2]; $w = [int]$Matches[3]; $h = [int]$Matches[4]
    $vw = [int]$Matches[5]; $vh = [int]$Matches[6]; $n = [int]$Matches[7]; $fits = [int]$Matches[8]; $t = [int]$Matches[9]
    if ($n -lt 5) { $out.Add("tooltip a: $n lines, want at least 5 (title, role, body, stats, flavor)") }
    if ($x -lt 0 -or $y -lt 0 -or ($x + $w) -gt $vw -or ($y + $h) -gt $vh -or $w -le 0 -or $h -le 0) {
        $out.Add("tooltip b: rect $x,$y,$w,$h is not inside the ${vw}x$vh viewport")
    }
    if ($fits -ne 1) { $out.Add("tooltip b: fits=$fits (a wrapped line overflows the box, or the box covers the party frames or the top bar)") }
    $hidden = $null
    $after = $false
    foreach ($l in $all) {
        if ($l -eq $shown[0]) { $after = $true; continue }
        if ($after -and $l -match '^HUD tooltip hidden tick=(\d+)$') { $hidden = [int]$Matches[1]; break }
    }
    if ($null -eq $hidden) { $out.Add('tooltip c: no "HUD tooltip hidden tick=H" line after the tooltip showed (the release did not hide it)') }
    elseif (($hidden - $t) -lt 1 -or ($hidden - $t) -gt 4) {
        $out.Add("tooltip c: shown at tick $t, hidden at $hidden; want the hide 1 to 4 ticks after the show (shown past 300 ms of a 400 ms hold)")
    }
    $hover = $all | Where-Object { $_ -match '^HUD tooltip .* by=hover ' } | Select-Object -First 1
    if ($hover) { $out.Add("tooltip d: a hover tooltip showed with nothing hovering the bar: '$hover'") }
    return , $out
}

if ($PSBoundParameters.ContainsKey('Lines')) { return (Get-TooltipFailures @($Lines)) }

$SceneName = 'HudTooltip-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$hold = Join-Path $repo 'game/fixtures/input/hold-key1.input-log.json'
$ErrorActionPreference = 'Continue'
# --quit-after is a hang guard: the replay's events are all due by frame ~150, the run keeps going until the guard.
# --view-size 1280x720 (Main.cs ApplyViewSize): a headless window is 64x64, too small for the 39-line hold tooltip; the run
# lays out against the shipped window size, as the desktop game does. Render-only; the sim and the replay do not read it.
$text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after 600 '--' --view-size 1280x720 --replay-input $hold 2>&1 | Out-String
$rc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $rc $text
$all = @($text -split "`r?`n")
Write-SceneLog 'tooltip' $all
if ($rc -ne 0) { $failures.Add("tooltip: godot exited $rc (expected 0)") }
foreach ($f in (Get-TooltipFailures $all)) { $failures.Add($f) }
Write-Output '--- tooltip (key 1 held 400 ms in the default run) ---'
$all | Where-Object { $_ -match '^HUD tooltip ' } | Select-Object -First 4 | ForEach-Object { Write-Output $_ }
Complete-Scene 'hold=400ms'
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
