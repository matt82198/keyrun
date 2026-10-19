<#
.SYNOPSIS
  Headless scene test for GATE-CUTAWAY (STATE.md D27, D15): a shut room entry gate (GATE-VIEW) that sits between the
  camera and the party is cut away like a room wall, and a shut gate that does not is fully visible.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 and replays the deterministic run C (the
  scripted Warden plus the two bot seats from the Entrance, the default seed):

      godot --headless --path game --fixed-fps 60 -- --autorun --ticks N

  src/View/RoomGateView.cs prints, on stdout only:
    GATEVIEW shut room=R tick=C ... box=(minX,maxX,minZ,maxZ,minY,maxY) inTree=True   the shut gate's solid box, mm
    GATEVIEW probe tick=T shut=R,..|none cut=R,..|none cam=(x,y,z) at=(x,y,z);..|none
  where cut= lists the shut gate nodes whose Visible flag is false (read from the nodes), cam= is the camera and at=
  the sight-line targets (the pivot and each living party body at chest height) of the CameraRig frame the gates were
  last cut for. This test recomputes, on its own, which shut gate boxes those sight lines cross (the CameraBoom.Occluding
  rule the walls obey: the box grown by 250 mm in x and z, real height, a strict slab test) and asserts:
    1. the process exits 0 and prints "AUTORUN done ticks=N";
    2. every shut line carries a box, every probe line carries cut=, cam= and at=;
    3. at every probe, cut= is exactly the shut gates some sight line crosses (cut away when between camera and party,
       visible otherwise; never a gate that is not shut);
    4. at least one probe has a shut gate between camera and party (cut away), and at least one has a shut gate that
       is not (fully visible: the player still sees the door shut behind the party).
  Before GATE-CUTAWAY the probe carried no sight lines and no gate was ever cut away (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/GateCutawaySceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 3600
)
$ErrorActionPreference = 'Stop'

# CameraBoom.OcclusionPadMm: the XZ growth the wall cut-away applies to every occluder box.
$PadMm = 250

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

$failures = New-Object System.Collections.Generic.List[string]

$ErrorActionPreference = 'Continue'
# --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
$guardFrames = (3 * $Ticks) + 240
$out = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guardFrames '--' --autorun --ticks $Ticks 2>&1 | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

if ($code -eq 2 -and $out -match 'Godot binary not found') {
    Write-Output $out
    Write-Output 'GateCutawaySceneTest: CANNOT RUN (Godot binary not found)'
    exit 2
}
if ($code -ne 0) {
    $failures.Add("godot exited $code (expected 0)")
}

$outLines = @($out -split "`r?`n")
$summary = $outLines | Where-Object { $_ -match '^AUTORUN done ' } | Select-Object -Last 1
if (-not $summary -or $summary -notmatch "^AUTORUN done ticks=$Ticks ") {
    $failures.Add("no 'AUTORUN done ticks=$Ticks' summary (got '$summary')")
}

function Parse-Point([string]$text) {
    $p = $text.Trim('(', ')') -split ','
    return @([double]$p[0], [double]$p[1], [double]$p[2])
}

# Strict slab test of the segment from -> to against the box grown by $PadMm in x and z (CameraBoom.Occluding).
function Test-Crosses($from, $to, $box) {
    $axes = @(
        @($from[0], ($to[0] - $from[0]), ($box.MinX - $PadMm), ($box.MaxX + $PadMm)),
        @($from[1], ($to[1] - $from[1]), $box.MinY, $box.MaxY),
        @($from[2], ($to[2] - $from[2]), ($box.MinZ - $PadMm), ($box.MaxZ + $PadMm))
    )
    $lo = 0.0
    $hi = 1.0
    foreach ($ax in $axes) {
        $a = $ax[0]; $d = $ax[1]; $min = $ax[2]; $max = $ax[3]
        if ($d -eq 0) {
            if ($a -le $min -or $a -ge $max) { return $false }
            continue
        }
        if ($d -gt 0) { $t0 = ($min - $a) / $d; $t1 = ($max - $a) / $d } else { $t0 = ($a - $max) / (-$d); $t1 = ($a - $min) / (-$d) }
        if ($t0 -gt $lo) { $lo = $t0 }
        if ($t1 -lt $hi) { $hi = $t1 }
    }
    return ($lo -lt $hi)
}

# The view's shut gate boxes, by room (the latest shut line wins; a room is shut once per run C).
$boxes = @{}
$shutLines = @($outLines | Where-Object { $_ -match '^GATEVIEW shut ' })
foreach ($l in $shutLines) {
    if ($l -notmatch '^GATEVIEW shut room=(\S+) .*box=\((-?\d+),(-?\d+),(-?\d+),(-?\d+),(-?\d+),(-?\d+)\)') {
        $failures.Add("shut line without a box=(minX,maxX,minZ,maxZ,minY,maxY): '$l'")
        continue
    }
    $boxes[$Matches[1]] = [pscustomobject]@{
        MinX = [double]$Matches[2]; MaxX = [double]$Matches[3]; MinZ = [double]$Matches[4]
        MaxZ = [double]$Matches[5]; MinY = [double]$Matches[6]; MaxY = [double]$Matches[7]
    }
}
if ($shutLines.Count -lt 2) { $failures.Add("the view shut $($shutLines.Count) gate(s) (expected at least 2)") }

$probes = @($outLines | Where-Object { $_ -match '^GATEVIEW probe ' })
$between = New-Object System.Collections.Generic.List[string]
$behind = New-Object System.Collections.Generic.List[string]
$checked = 0
$noSight = 0
foreach ($p in $probes) {
    if ($p -notmatch '^GATEVIEW probe tick=(\d+) shut=(\S+) cut=(\S+) cam=(\(\S+?\)) at=(\S+)') {
        $noSight++
        if ($noSight -le 3) { $failures.Add("probe line without cut=, cam= and at=: '$p'") }
        continue
    }
    $t = [int]$Matches[1]
    $shut = if ($Matches[2] -eq 'none') { @() } else { @($Matches[2] -split ',') }
    $cut = if ($Matches[3] -eq 'none') { @() } else { @($Matches[3] -split ',') }
    $cam = Parse-Point $Matches[4]
    $targets = if ($Matches[5] -eq 'none') { @() } else { @($Matches[5] -split ';' | ForEach-Object { , (Parse-Point $_) }) }
    if ($shut.Count -eq 0) {
        if ($cut.Count -gt 0) { $failures.Add("probe tick=$t cuts '$($Matches[3])' with no gate shut") }
        continue
    }
    $checked++
    $want = New-Object System.Collections.Generic.List[string]
    foreach ($room in $shut) {
        if (-not $boxes.ContainsKey($room)) {
            $failures.Add("probe tick=$t names shut room '$room' with no shut box line")
            continue
        }
        $crossed = $false
        foreach ($to in $targets) {
            if (Test-Crosses $cam $to $boxes[$room]) { $crossed = $true; break }
        }
        if ($crossed) { $want.Add($room) }
        $isCut = $cut -contains $room
        if ($crossed -and $isCut) { $between.Add("tick=$t room=$room") }
        if (-not $crossed -and -not $isCut) { $behind.Add("tick=$t room=$room") }
        if ($crossed -and -not $isCut) {
            $failures.Add("probe tick=${t}: the shut $room gate sits between camera and party but is not cut away (cam=$($Matches[4]))")
        }
        if (-not $crossed -and $isCut) {
            $failures.Add("probe tick=${t}: the shut $room gate is cut away but no sight line crosses it")
        }
    }
    foreach ($room in $cut) {
        if ($shut -notcontains $room) { $failures.Add("probe tick=$t cuts '$room', which is not shut") }
    }
}
if ($noSight -gt 3) { $failures.Add("... $noSight probe lines without sight lines in all") }
if ($between.Count -lt 1) { $failures.Add('no probe with a shut gate between camera and party in the cut-away state') }
if ($behind.Count -lt 1) { $failures.Add('no probe with a shut gate fully visible (not between camera and party)') }

Write-Output '--- engine output (tail) ---'
$outLines | Select-Object -Last 4 | ForEach-Object { Write-Output $_ }
Write-Output '--- view gate lines ---'
foreach ($l in $shutLines) { Write-Output $l }
Write-Output "probes=$($probes.Count) shutProbes=$checked between=$($between.Count) behind=$($behind.Count)"
if ($between.Count -gt 0) { Write-Output "first cut: $($between[0])" }
if ($behind.Count -gt 0) { Write-Output "first visible: $($behind[0])" }

if ($failures.Count -gt 0) {
    Write-Output "GateCutawaySceneTest: FAIL ($($failures.Count))"
    $failures | Select-Object -First 12 | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "GateCutawaySceneTest: PASS ticks=$Ticks gates=$($shutLines.Count) shutProbes=$checked cut=$($between.Count) visible=$($behind.Count)"
exit 0
