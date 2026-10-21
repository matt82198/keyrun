<#
.SYNOPSIS
    Lane COMBATTEXT-OVERLAP (finding f-caae8653, Matt: "reduce screen clutter"). Reads the HUD's own proof lines of
    one three-seat party autorun at the project's 1280x720 viewport and asserts:

      a. the run-end panel sits clear of the HUD it used to cover: at least one
         "HUD runend layout panel=X,Y,W,H abilitybar=X,Y,W,H party=X,Y,W,H viewport=1280x720" line, and on every one
         the panel shares no pixel with the ability bar footprint (the player's health and resource meters plus the
         slot row, AbilityBar.Footprint) nor with the party frames block (HudObstacles.PartyFrames), and stays inside
         the viewport;
      b. a damage number never sits on its target's nameplate: at least one
         "HUD numbers place target=T plate=X,Y,W,H number=X,Y,W,H raw_hit=0|1" line with raw_hit=1 (the number spawned on
         top of the plate, so the collision is exercised), and on every one the placed number shares no pixel with the
         plate;
      c. opening the run-end panel clears the live damage numbers: a "HUD numbers cleared reason=runend live=N" line, a
         "HUD numbers drawn after=runend live=0" line (the first frame after the panel opened draws no number), and no
         "HUD fct source=" or "HUD numbers place" line after the clear (the run is over; nothing new floats over the panel).

    On origin/main 4a19bd34 none of these proof lines exist, the panel is centred at y 0.37 of the viewport across
    the party frames' right edge, numbers are drawn 2.6 m over the target on top of its plate, and numbers alive at
    the run end keep floating over the panel: a, b and c all fail (RED).

.DESCRIPTION
    Three ways in:
      -Lines <string[]>   the output lines of one autorun; the failures come back as strings, nothing is printed.
      -LogPath <file>     saved godot stdout of an autorun; prints each failure, then PASS or FAIL, exit 0 or 1.
      (neither)           runs the autorun itself (headless, --fixed-fps 60, --autorun --ticks <Ticks> --view-size
                          1280x720: a headless window is 64x64) and checks it.
#>
param(
    [string[]]$Lines,
    [string]$LogPath,
    [int]$Ticks = 9000  # R-DOOR-1: party autorun budget moves with the door at 100% (was 7000)
)
$ErrorActionPreference = 'Stop'

function ConvertTo-Rect([string]$text) {
    $p = $text -split ','
    return [pscustomobject]@{ X = [int]$p[0]; Y = [int]$p[1]; W = [int]$p[2]; H = [int]$p[3] }
}

function Test-Intersects($a, $b) {
    return ($a.X -lt ($b.X + $b.W)) -and ($b.X -lt ($a.X + $a.W)) -and ($a.Y -lt ($b.Y + $b.H)) -and ($b.Y -lt ($a.Y + $a.H))
}

function Get-OverlapFailures([string[]]$all) {
    $out = New-Object System.Collections.Generic.List[string]
    $rect = '(-?\d+,-?\d+,\d+,\d+)'

    # a. the run-end panel is clear of the ability bar (health, resource, slots) and the party frames.
    $layouts = @($all | Where-Object { $_ -match '^HUD runend layout ' })
    if ($layouts.Count -eq 0) { $out.Add('combattext-overlap a: no "HUD runend layout" line (the run-end panel never reported its rect)') }
    foreach ($l in $layouts) {
        if ($l -notmatch "^HUD runend layout panel=$rect abilitybar=$rect party=$rect viewport=(\d+)x(\d+)$") {
            $out.Add("combattext-overlap a: malformed layout line: '$l'")
            continue
        }
        $panel = ConvertTo-Rect $Matches[1]; $bar = ConvertTo-Rect $Matches[2]; $party = ConvertTo-Rect $Matches[3]
        $vw = [int]$Matches[4]; $vh = [int]$Matches[5]
        if ($vw -ne 1280 -or $vh -ne 720) { $out.Add("combattext-overlap a: viewport is ${vw}x$vh, expected 1280x720: '$l'") }
        if (Test-Intersects $panel $bar) { $out.Add("combattext-overlap a: the run-end panel covers the ability bar / player health bar: '$l'") }
        if (Test-Intersects $panel $party) { $out.Add("combattext-overlap a: the run-end panel covers the party frames: '$l'") }
        if ($panel.X -lt 0 -or $panel.Y -lt 0 -or ($panel.X + $panel.W) -gt $vw -or ($panel.Y + $panel.H) -gt $vh) {
            $out.Add("combattext-overlap a: the run-end panel leaves the viewport: '$l'")
        }
    }

    # b. a damage number is placed off its target's nameplate.
    $places = @($all | Where-Object { $_ -match '^HUD numbers place ' })
    if ($places.Count -eq 0) { $out.Add('combattext-overlap b: no "HUD numbers place" line (no damage number was laid out against a plate)') }
    $rawHits = 0
    foreach ($l in $places) {
        if ($l -notmatch "^HUD numbers place target=\d+ plate=$rect number=$rect raw_hit=([01])$") {
            $out.Add("combattext-overlap b: malformed place line: '$l'")
            continue
        }
        $plate = ConvertTo-Rect $Matches[1]; $number = ConvertTo-Rect $Matches[2]
        if ($Matches[3] -eq '1') { $rawHits++ }
        if (Test-Intersects $plate $number) { $out.Add("combattext-overlap b: a damage number overlaps its target's nameplate: '$l'") }
    }
    if ($places.Count -gt 0 -and $rawHits -eq 0) {
        $out.Add('combattext-overlap b: no number spawned on its plate (raw_hit=1), so "moved off the plate" is not exercised')
    }

    # c. the run-end panel clears live numbers, and none spawn after.
    $cleared = -1
    for ($i = 0; $i -lt $all.Count; $i++) { if ($all[$i] -match '^HUD numbers cleared reason=runend live=\d+$') { $cleared = $i; break } }
    if ($cleared -lt 0) {
        $out.Add('combattext-overlap c: no "HUD numbers cleared reason=runend" line (live damage numbers keep floating over the run-end panel)')
    } else {
        $late = $all[($cleared + 1)..($all.Count - 1)] | Where-Object { $_ -match '^HUD (fct source=|numbers place )' } | Select-Object -First 1
        if ($late) { $out.Add("combattext-overlap c: a damage number spawned after the run-end panel opened: '$late'") }
    }
    $drawn = $all | Where-Object { $_ -match '^HUD numbers drawn after=runend live=\d+$' } | Select-Object -First 1
    if (-not $drawn) {
        $out.Add('combattext-overlap c: no "HUD numbers drawn after=runend" line (the frame after the run-end panel opened was not observed)')
    } elseif ($drawn -notmatch ' live=0$') {
        $out.Add("combattext-overlap c: damage numbers still float over the open run-end panel: '$drawn'")
    }
    return , $out
}

if (-not $LogPath -and -not $PSBoundParameters.ContainsKey('Lines')) {
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
    $wrapper = Join-Path $repo 'tools/godot/godot.ps1'
    $guard = (3 * $Ticks) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks --view-size 1280x720 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rc -eq 2 -and $text -match 'Godot binary not found') { Write-Output 'CombatTextOverlapTest: CANNOT RUN (Godot binary not found)'; exit 2 }
    $all = @($text -split "`r?`n")
    $f = Get-OverlapFailures $all
    if ($rc -ne 0) { $f.Add("combattext-overlap: godot exited $rc (expected 0)") }
} elseif ($LogPath) {
    $f = Get-OverlapFailures @(Get-Content -LiteralPath $LogPath)
} else {
    return (Get-OverlapFailures @($Lines))
}
foreach ($x in $f) { Write-Output $x }
if ($f.Count -gt 0) { Write-Output 'CombatTextOverlapTest: FAIL'; exit 1 }
Write-Output 'CombatTextOverlapTest: PASS'
exit 0
