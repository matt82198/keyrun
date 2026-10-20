<#
.SYNOPSIS
  Headless scene test for GATE-CUTAWAY (STATE.md D27, D15): a shut room entry gate (GATE-VIEW) that sits between the
  camera and the party is cut away like a room wall, and a shut gate that does not is fully visible.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 and replays the deterministic run C (the
  scripted Warden plus the two bot seats from the Entrance, the default seed):

      godot --headless --path game --fixed-fps 60 -- --autorun --ticks N --view-size 1280x720

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

  GATE-FOREGROUND (BIBLE s1, the dungeon reads clearly from Queue into the first pull): the probe also prints
    scr=R:(x0,y0,x1,y1,near);..|none bar=(x0,y0,x1,y1) party=(x,y,depth);..|none
  (every on-screen shut gate's projected screen rect and nearest view depth, cut or drawn, the ability bar footprint,
  each sight target projected with its view depth; px and mm, exactly what the view's last cut used). A gate is in the
  foreground when its rect overlaps the bar, or contains a party point and its nearest corner is nearer the camera than
  that point; the view cuts such a gate too, so check 3 reads "cut= is exactly the shut gates some sight line crosses or
  that stand in the foreground". This test asserts, at every probe with a gate shut:
    5. the probe carries scr=, bar= and party=;
    6. no drawn (not cut) gate's screen rect overlaps the ability bar footprint;
    7. no drawn gate stands in front of a party point inside its screen rect;
    8. at least one probe cut a gate for the foreground alone, and at least one projected a drawn gate.
  Before GATE-FOREGROUND the Choir entry gate was drawn in the foreground under the ability bar (RED: 51 probes).

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
# --view-size: a headless window is 64x64; the screen checks (5-7) frame and project at the shipped 1280x720.
$out = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guardFrames '--' --autorun --ticks $Ticks --view-size 1280x720 2>&1 | Out-String
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
$noScreen = 0
$drawn = 0
$fore = New-Object System.Collections.Generic.List[string]
$underBar = New-Object System.Collections.Generic.List[string]
$overParty = New-Object System.Collections.Generic.List[string]
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
    $camText = $Matches[4]
    # GATE-FOREGROUND: the screen rects the view cut on (every shut gate on screen), the bar and the party points.
    $rects = @{}
    $bar = $null
    $pts = @()
    if ($p -notmatch ' scr=(\S+) bar=\((-?\d+),(-?\d+),(-?\d+),(-?\d+)\) party=(\S+)') {
        $noScreen++
        if ($noScreen -le 3) { $failures.Add("probe line without scr=, bar= and party=: '$p'") }
    } else {
        $bar = @([double]$Matches[2], [double]$Matches[3], [double]$Matches[4], [double]$Matches[5])
        $scrText = $Matches[1]
        $partyText = $Matches[6]
        $pts = if ($partyText -eq 'none') { @() } else { @($partyText -split ';' | ForEach-Object { , (Parse-Point $_) }) }
        if ($scrText -ne 'none') {
            foreach ($r in ($scrText -split ';')) {
                if ($r -notmatch '^(\S+?):\((-?\d+),(-?\d+),(-?\d+),(-?\d+),(-?\d+)\)$') {
                    $failures.Add("probe tick=${t}: bad scr= entry '$r'")
                    continue
                }
                $rects[$Matches[1]] = @([double]$Matches[2], [double]$Matches[3], [double]$Matches[4], [double]$Matches[5], [double]$Matches[6])
            }
        }
    }
    $want = New-Object System.Collections.Generic.List[string]
    foreach ($room in $shut) {
        # Foreground (recomputed here): the gate's rect overlaps the bar, or holds a party point it is nearer than.
        $underThis = $false
        $overThis = $null
        if ($rects.ContainsKey($room) -and $null -ne $bar) {
            $x0, $y0, $x1, $y1, $near = $rects[$room]
            if ($x0 -lt $bar[2] -and $x1 -gt $bar[0] -and $y0 -lt $bar[3] -and $y1 -gt $bar[1]) { $underThis = $true }
            foreach ($q in $pts) {
                if ($q[0] -ge $x0 -and $q[0] -le $x1 -and $q[1] -ge $y0 -and $q[1] -le $y1 -and $near -lt $q[2]) { $overThis = $q; break }
            }
            if (-not ($cut -contains $room)) {
                $drawn++
                if ($underThis) { $underBar.Add("tick=$t room=$room scr=($x0,$y0,$x1,$y1) bar=($($bar -join ','))") }
                if ($null -ne $overThis) { $overParty.Add("tick=$t room=$room scr=($x0,$y0,$x1,$y1) near=$near party=($($overThis -join ','))") }
            }
        }
        $foreground = $underThis -or ($null -ne $overThis)
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
        if (-not $crossed -and $foreground -and $isCut) { $fore.Add("tick=$t room=$room") }
        if ($crossed -and -not $isCut) {
            $failures.Add("probe tick=${t}: the shut $room gate sits between camera and party but is not cut away (cam=$camText)")
        }
        if (-not $crossed -and -not $foreground -and $isCut) {
            $failures.Add("probe tick=${t}: the shut $room gate is cut away but no sight line crosses it and it is not in the foreground")
        }
    }
    foreach ($room in $cut) {
        if ($shut -notcontains $room) { $failures.Add("probe tick=$t cuts '$room', which is not shut") }
    }
}
if ($noScreen -gt 3) { $failures.Add("... $noScreen probe lines without scr=/bar=/party= in all") }
if ($underBar.Count -gt 0) {
    $failures.Add("$($underBar.Count) probe(s) draw a shut gate over the ability bar; first: $($underBar[0])")
}
if ($overParty.Count -gt 0) {
    $failures.Add("$($overParty.Count) probe(s) draw a shut gate in front of the party; first: $($overParty[0])")
}
if ($noSight -gt 3) { $failures.Add("... $noSight probe lines without sight lines in all") }
if ($between.Count -lt 1) { $failures.Add('no probe with a shut gate between camera and party in the cut-away state') }
if ($behind.Count -lt 1) { $failures.Add('no probe with a shut gate fully visible (not between camera and party)') }

Write-Output '--- engine output (tail) ---'
$outLines | Select-Object -Last 4 | ForEach-Object { Write-Output $_ }
Write-Output '--- view gate lines ---'
foreach ($l in $shutLines) { Write-Output $l }
Write-Output "probes=$($probes.Count) shutProbes=$checked between=$($between.Count) behind=$($behind.Count)"
if ($drawn -lt 1) { $failures.Add('no probe projected a drawn shut gate (scr= never named a gate)') }
if ($fore.Count -lt 1) { $failures.Add('no probe with a shut gate cut for standing in the foreground') }
Write-Output "drawn=$drawn fore=$($fore.Count) underBar=$($underBar.Count) overParty=$($overParty.Count)"
if ($fore.Count -gt 0) { Write-Output "first foreground cut: $($fore[0])" }
if ($between.Count -gt 0) { Write-Output "first cut: $($between[0])" }
if ($behind.Count -gt 0) { Write-Output "first visible: $($behind[0])" }

if ($failures.Count -gt 0) {
    Write-Output "GateCutawaySceneTest: FAIL ($($failures.Count))"
    $failures | Select-Object -First 12 | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "GateCutawaySceneTest: PASS ticks=$Ticks gates=$($shutLines.Count) shutProbes=$checked cut=$($between.Count) visible=$($behind.Count) drawn=$drawn fore=$($fore.Count) underBar=0 overParty=0"
exit 0
