<#
.SYNOPSIS
    Lane NAMEPLATE-DECLUTTER (findings f-bdaf75c7, f-a63f66b9; Matt 2026-10-19: "enemy health does not need to be
    overhead, they can have unitframes but not just a random number lol" and "reduce screen clutter essentially").
    After HUD-DECLUTTER (#208) the boss cast still showed twice (an over-head bar "The Hollow Warden... 1.7s" AND the
    boss unit frame's "Cleave 1.7s"), "SPREAD: move apart" showed twice (the callout banner AND a world label), and the
    Pair's five "Paired Warrior" plates stood in one tall column over the target. This test reads the HUD's own proof
    lines of one three-seat party autorun (run C, the same run HudDeclutterTest reads) and one probe scene:

      a. one enemy cast bar: every enemy cast a unit frame shows ("HUD unitframe cast kind=K actor=A ability=X") has a
         "HUD castbar actor=A kind=enemy where=frame ability=X" line (its bar lives on the unit frame), the boss
         (actor 900, always on the boss frame) never gets "where=overhead", and an enemy no unit frame shows still
         gets an over-head bar ("where=overhead", the enemy's language, BIBLE s3);
      b. one SPREAD text: at least one "HUD pingtext label=SPREAD banner=B world=W" line, and on every one B is 1 and W
         is 0 (the banner only, no world label);
      c. readable stacked plates: at least one "HUD platecrowd name=... crowd=C" line with C >= 2 (same-name plates on
         one spot, the Pair), and on every one drawn=1 (the crowd draws one plate) and marked equals target (the
         Warden's target in the crowd keeps its "> name <" mark);
      p. the probe scene res://src/Hud/NameplateDeclutterProbe.tscn prints "NAMEPLATE-DECLUTTER PASS cases=5": five
         plates at one anchor fold to one plate, the target's, drawn "> Paired Warrior x5 <"; plates of two names at
         one spot both draw and, after HudLayout.Separate, share no pixel; far-apart plates are not folded; SPREAD has
         no world label and STACK keeps one.

    On origin/main 4a19bd34 (with this lane's proof lines added) a, b, c and p fail (RED).

    Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Hud/NameplateDeclutterTest.ps1
    Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 9000  # R-DOOR-1: party autorun budget moves with the door at 100% (was 7000)
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

# One launch lock for both launches (tools/godot/launch-lock.ps1); they run one after another. Exit 2 when another
# Godot instance holds the lock.
. (Join-Path $repo 'tools/godot/launch-lock.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner 'NameplateDeclutterTest.ps1' -ExitCodeOnBusy 2
try {

# ---- Run C: the three-seat party autorun ---------------------------------------------------------------------
$guard = (3 * $Ticks) + 240
$ErrorActionPreference = 'Continue'
$text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks 2>&1 | Out-String
$rc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($rc -eq 2 -and $text -match 'Godot binary not found') { Write-Output 'NameplateDeclutterTest: CANNOT RUN (Godot binary not found)'; exit 2 }
if ($rc -ne 0) { $failures.Add("run C: godot exited $rc (expected 0)") }
$all = @($text -split "`r?`n")

# a. one enemy cast bar.
$frameCasts = @($all | Where-Object { $_ -match '^HUD unitframe cast kind=(target|boss) actor=(\d+) ability=(\S+) interruptible=[01]$' })
if ($frameCasts.Count -eq 0) { $failures.Add('a: no "HUD unitframe cast" line (no enemy cast on a unit frame to check)') }
foreach ($l in $frameCasts) {
    $null = $l -match '^HUD unitframe cast kind=\S+ actor=(\d+) ability=(\S+) '
    $actor = $Matches[1]; $ability = [regex]::Escape($Matches[2])
    $inFrame = $all | Where-Object { $_ -match "^HUD castbar actor=$actor kind=enemy where=frame ability=$ability$" } | Select-Object -First 1
    if (-not $inFrame) {
        $failures.Add("a: '$l' has no 'HUD castbar actor=$actor kind=enemy where=frame' line (the cast also draws over the head)")
        # Diagnostic only (assertions unchanged): every castbar and unit frame cast line this actor produced in the run.
        $diag = @($all | Where-Object { $_ -match "^HUD (castbar|unitframe cast) .*actor=$actor( |$)" })
        $failures.Add("a diagnostic: $($diag.Count) castbar/unitframe cast line(s) for actor=$actor in the run")
        foreach ($d in $diag) { $failures.Add("a diagnostic:   $d") }
    }
}
$bossOver = $all | Where-Object { $_ -match '^HUD castbar actor=900 kind=enemy where=overhead ' } | Select-Object -First 1
if ($bossOver) { $failures.Add("a: the boss's cast is drawn over its head as well as on the boss frame: '$bossOver'") }
$bossFrame = $all | Where-Object { $_ -match '^HUD castbar actor=900 kind=enemy where=frame ' } | Select-Object -First 1
if (-not $bossFrame) { $failures.Add('a: no "HUD castbar actor=900 kind=enemy where=frame" line (the boss cast is not placed on its frame)') }
$unframedOver = $all | Where-Object { $_ -match '^HUD castbar actor=(1\d\d|9\d\d) kind=enemy where=overhead ' } | Select-Object -First 1
if (-not $unframedOver) { $failures.Add('a: no enemy over-head bar at all (an enemy no unit frame shows must keep its cast bar)') }

# b. one SPREAD text.
$spread = @($all | Where-Object { $_ -match '^HUD pingtext label=SPREAD banner=\d+ world=\d+$' })
if ($spread.Count -eq 0) { $failures.Add('b: no "HUD pingtext label=SPREAD" line (the SPREAD call never showed)') }
foreach ($l in $spread) {
    if ($l -notmatch ' banner=1 world=0$') { $failures.Add("b: SPREAD text is not the banner alone: '$l'") }
}

# c. readable stacked plates.
$crowds = @($all | Where-Object { $_ -match '^HUD platecrowd name="[^"]+" crowd=\d+ drawn=\d+ target=[01] marked=[01]$' })
$big = $crowds | Where-Object { $_ -match ' crowd=([2-9]|\d\d+) ' } | Select-Object -First 1
if (-not $big) { $failures.Add('c: no "HUD platecrowd ... crowd>=2" line (no same-name plates on one spot to check)') }
foreach ($l in $crowds) {
    if ($l -notmatch ' drawn=1 ') { $failures.Add("c: a same-name crowd draws more than one plate: '$l'") }
    $null = $l -match ' target=([01]) marked=([01])$'
    if ($Matches[1] -ne $Matches[2]) { $failures.Add("c: the Warden's target in a crowd lost its mark: '$l'") }
}

# ---- Run P: the probe scene -------------------------------------------------------------------------------------
$ErrorActionPreference = 'Continue'
$pText = & $wrapper --headless --path (Join-Path $repo 'game') --quit-after 600 'res://src/Hud/NameplateDeclutterProbe.tscn' 2>&1 | Out-String
$pCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$pLines = @($pText -split "`r?`n")
if ($pCode -ne 0) { $failures.Add("p: godot exited $pCode (expected 0)") }
foreach ($l in @($pLines | Where-Object { $_ -match '^NAMEPLATE-DECLUTTER FAIL ' })) { $failures.Add("p: $l") }
if (-not ($pLines | Where-Object { $_ -match '^NAMEPLATE-DECLUTTER PASS cases=5$' } | Select-Object -First 1)) {
    $failures.Add('p: no "NAMEPLATE-DECLUTTER PASS cases=5" line')
}

Write-Output '--- asserted lines ---'
foreach ($l in @($bossFrame, $unframedOver) + @($spread | Select-Object -First 3) + @($crowds | Select-Object -First 4)) {
    if ($l) { Write-Output $l }
}
if ($failures.Count -gt 0) {
    Write-Output "NameplateDeclutterTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "NameplateDeclutterTest: PASS ticks=$Ticks frameCasts=$($frameCasts.Count) spreadLines=$($spread.Count) crowdLines=$($crowds.Count)"
exit 0
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
