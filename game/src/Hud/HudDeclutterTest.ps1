<#
.SYNOPSIS
    Lane HUD-DECLUTTER (Matt 2026-10-19: "move friendly casts into party frames btw, only show the played characters
    damage numbers in fight, reduce screen clutter essentially... enemy health does not need to be overhead, they can
    have unitframes but not just a random number lol"). Reads the HUD's own proof lines of one three-seat party
    autorun (run C: the human Warden, seat 1, plus the Chanter and Cinder bots, seats 2 and 3) and asserts:

      a. friendly casts live in the party frames: at least one "HUD party cast seat=S" line for a bot seat (2 or 3),
         at least one "HUD castbar actor=A kind=player where=frame" line, and NO "HUD castbar ... kind=player
         where=overhead" line (no cast bar over a party member's head);
      b. only the played character's damage floats in the world: at least one "HUD fct source=1 target=T amount=N"
         line, NO "HUD fct" line whose source is not 1, while the run holds a bot's damage EVENT (sourceId 2 or 3,
         modifiedAmount > 0) that therefore spawned no number;
      c. no health number over enemies: every "HUD plate actor=A lines=..." line reads "lines=name" (the plate draws
         the name only, never "H / M"), at least one plate line for an enemy (actor 100+), and enemy health lives in
         unit frames: a "HUD unitframe kind=target actor=A" line with A an enemy and a "HUD unitframe kind=boss
         actor=900" line (the boss frame while the boss is engaged);
      d. enemy cast bars are kept (design/BIBLE.md section 3, "Cast bars are the enemy's language"): at least one
         "HUD castbar actor=A kind=enemy where=overhead" line and one "HUD unitframe cast" line (the enemy's cast is
         also on its unit frame).

    On origin/main dceca854 the HUD prints none of these proof lines, draws a cast bar over every caster's head
    (party members included), draws "H / M" under every enemy and bot name plate, has no unit frames and no
    floating combat text: a, b, c and d all fail (RED).

.DESCRIPTION
    Three ways in:
      -Lines <string[]>   the output lines of one autorun (HudSceneTest run C passes its first run's lines); the
                          failures come back as strings, nothing is printed.
      -LogPath <file>     saved godot stdout of a run C autorun; prints each failure, then PASS or FAIL, exit 0 or 1.
      (neither)           runs run C itself (headless, --fixed-fps 60, --autorun --ticks <Ticks>) and checks it.
#>
param(
    [string[]]$Lines,
    [string]$LogPath,
    [int]$Ticks = 7000
)
$ErrorActionPreference = 'Stop'

function Get-DeclutterFailures([string[]]$all) {
    $out = New-Object System.Collections.Generic.List[string]
    # a. friendly casts in the party frames.
    $botFrameCast = $all | Where-Object { $_ -match '^HUD party cast seat=[23] ability=\S+' } | Select-Object -First 1
    if (-not $botFrameCast) { $out.Add('hud-declutter a: no "HUD party cast seat=2|3" line (a bot''s cast never showed in its party frame)') }
    $frameCast = $all | Where-Object { $_ -match '^HUD castbar actor=\d+ kind=player where=frame ' } | Select-Object -First 1
    if (-not $frameCast) { $out.Add('hud-declutter a: no "HUD castbar ... kind=player where=frame" line') }
    $overheadFriend = $all | Where-Object { $_ -match '^HUD castbar actor=\d+ kind=player where=overhead' } | Select-Object -First 1
    if ($overheadFriend) { $out.Add("hud-declutter a: a party member's cast is drawn over its head: '$overheadFriend'") }
    # b. only the played character's damage numbers float in the world.
    $mine = $all | Where-Object { $_ -match '^HUD fct source=1 target=\d+ amount=[1-9]\d*$' } | Select-Object -First 1
    if (-not $mine) { $out.Add('hud-declutter b: no "HUD fct source=1" line (the played character''s hit spawned no damage number)') }
    $foreign = $all | Where-Object { $_ -match '^HUD fct ' -and $_ -notmatch '^HUD fct source=1 ' } | Select-Object -First 1
    if ($foreign) { $out.Add("hud-declutter b: a damage number for another source floats in the world: '$foreign'") }
    $botHit = $all | Where-Object { $_ -match '^EVENT \{.*"modifiedAmount":[1-9]\d*,.*"sourceId":[23],.*"type":"damage"' } | Select-Object -First 1
    if (-not $botHit) { $out.Add('hud-declutter b: no bot damage EVENT in the run, so "a bot''s hit spawns no number" is not exercised') }
    # c. no health number over enemies; enemy health in unit frames.
    $plates = @($all | Where-Object { $_ -match '^HUD plate actor=\d+ ' })
    $enemyPlate = $plates | Where-Object { $_ -match '^HUD plate actor=(1\d\d|9\d\d) ' } | Select-Object -First 1
    if (-not $enemyPlate) { $out.Add('hud-declutter c: no "HUD plate actor=<enemy>" line') }
    $healthPlate = $plates | Where-Object { $_ -notmatch ' lines=name$' } | Select-Object -First 1
    if ($healthPlate) { $out.Add("hud-declutter c: a name plate draws more than the name (an overhead health number): '$healthPlate'") }
    $target = $all | Where-Object { $_ -match '^HUD unitframe kind=target actor=(1\d\d|9\d\d) name="[^"]+" hp=\d+/[1-9]\d*$' } | Select-Object -First 1
    if (-not $target) { $out.Add('hud-declutter c: no "HUD unitframe kind=target actor=<enemy>" line (no target frame)') }
    $boss = $all | Where-Object { $_ -match '^HUD unitframe kind=boss actor=900 name="[^"]+" hp=\d+/[1-9]\d*$' } | Select-Object -First 1
    if (-not $boss) { $out.Add('hud-declutter c: no "HUD unitframe kind=boss actor=900" line (no boss frame while the boss is engaged)') }
    # d. enemy cast bars kept: over the head, and on the unit frame too.
    $enemyBar = $all | Where-Object { $_ -match '^HUD castbar actor=(1\d\d|9\d\d) kind=enemy where=overhead ' } | Select-Object -First 1
    if (-not $enemyBar) { $out.Add('hud-declutter d: no "HUD castbar actor=<enemy> kind=enemy where=overhead" line (enemy cast bars must stay)') }
    $frameEnemyCast = $all | Where-Object { $_ -match '^HUD unitframe cast kind=(target|boss) actor=(1\d\d|9\d\d) ability=\S+ interruptible=[01]$' } | Select-Object -First 1
    if (-not $frameEnemyCast) { $out.Add('hud-declutter d: no "HUD unitframe cast" line (the enemy cast is not on its unit frame)') }
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
    if ($rc -eq 2 -and $text -match 'Godot binary not found') { Write-Output 'HudDeclutterTest: CANNOT RUN (Godot binary not found)'; exit 2 }
    $all = @($text -split "`r?`n")
    $f = Get-DeclutterFailures $all
    if ($rc -ne 0) { $f.Add("hud-declutter: godot exited $rc (expected 0)") }
} elseif ($LogPath) {
    $f = Get-DeclutterFailures @(Get-Content -LiteralPath $LogPath)
} else {
    return (Get-DeclutterFailures @($Lines))
}
foreach ($x in $f) { Write-Output $x }
if ($f.Count -gt 0) { Write-Output 'HudDeclutterTest: FAIL'; exit 1 }
Write-Output 'HudDeclutterTest: PASS'
exit 0
