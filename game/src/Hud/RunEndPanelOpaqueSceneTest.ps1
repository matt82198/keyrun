<#
.SYNOPSIS
  Headless scene test for lane RUNEND-OPAQUE: the run-end panel's background is fully opaque, so the world name
  plates (drawn by LabelView on CanvasLayer 0, under the Hud layer the panel sits on) never show through it.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 with --autorun --ticks <Ticks> --solo (the
  solo Warden wipes in pull 1, so the run ends and the run-end panel is built) and reads the panel's own proof lines:

    1. godot exits 0;
    2. one "HUD runend fill r=R g=G b=B a=A panel=X,Y,WxH" line (RunEndPanel prints the PanelFill it draws with);
    3. A is 1.00 (fully opaque: nothing from the world layer bleeds through the panel);
    4. the line's panel rectangle equals the "HUD runend layout panel=..." rectangle (the fill covers the laid-out
       panel region, which is HudClearance.RunEndPanel's region, not the whole viewport).

  Before the lane the fill is alpha 0.94 (about 6% of the plates show through): 3 fails (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Hud/RunEndPanelOpaqueSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 600
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
    Write-Output 'RunEndPanelOpaqueSceneTest: CANNOT RUN (Godot binary not found)'
    exit 2
}
$lines = @($text -split "`r?`n")

if ($rc -ne 0) { $failures.Add("godot exited $rc (expected 0)") }
$fill = $lines | Where-Object { $_ -match '^HUD runend fill ' } | Select-Object -First 1
$layout = $lines | Where-Object { $_ -match '^HUD runend layout panel=' } | Select-Object -First 1
$alpha = $null
if (-not $fill) {
    $failures.Add('no "HUD runend fill" line (the run-end panel did not report the fill it draws)')
} elseif ($fill -notmatch '^HUD runend fill r=\d\.\d\d g=\d\.\d\d b=\d\.\d\d a=(\d\.\d\d) panel=(\S+)$') {
    $failures.Add("malformed fill line '$fill'")
} else {
    $alpha = $Matches[1]
    $fillRect = $Matches[2]
    if ($alpha -ne '1.00') {
        $failures.Add("want the run-end panel background alpha 1.00 (opaque), got a=$alpha in '$fill'")
    }
    if (-not $layout) {
        $failures.Add('no "HUD runend layout panel=" line to compare the fill rectangle with')
    } elseif ($layout -notmatch "^HUD runend layout panel=$([regex]::Escape($fillRect)) ") {
        $failures.Add("fill rectangle '$fillRect' is not the laid-out panel in '$layout'")
    }
}

Write-Output '--- asserted lines ---'
foreach ($l in @($fill, $layout)) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "RunEndPanelOpaqueSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "RunEndPanelOpaqueSceneTest: PASS ticks=$Ticks alpha=$alpha"
exit 0
