<#
.SYNOPSIS
    Lane U4 (keyrun MVP unit U4, BIBLE s10 kick-ready readout, lines 553-555): every party frame shows its member's kick
    as a ready pip when the kick is ready and as an arc that drains while it is on cooldown, so "who has a kick" reads
    without a ping. This test reads the HUD's own proof lines of one three-seat party autorun (the lines PartyFrames.cs
    LogKicks prints once per change of state and once per tenth of the cooldown while it drains):

      k1. every party seat's first kick line is "state=ready" and names its class Interrupt slot: seat 1 Warden
          warden.shieldBash, seat 2 Chanter chanter.rebuke, seat 3 Cinder cinder.quench (the frame shows a ready pip
          before any kick is spent);
      k2. the Warden's kick is spent in pull 1 (the Choir lieutenant, ScriptedWarden's Kick): at least one
          "HUD party kick seat=1 ability=warden.shieldBash state=cooldown left=N" line, with 0 < N <= 480 (the arc
          starts full);
      k3. the arc drains: the seat 1 cooldown lines carry at least three distinct left=N values, strictly falling in
          the order printed (the arc shrinks as the cooldown runs);
      k4. the kick comes back: after the seat 1 cooldown line, a "HUD party kick seat=1 ... state=ready" line appears
          (the pip returns when the cooldown ends, 480 ticks after the kick).

    Shape and colour: the pip is a filled disc with a ring, the cooldown a hollow ring with an arc (PartyFrames.cs
    DrawKick); the log lines are the state, so the test never asserts colour.

    Pure rule: PartyFrames.cs KickReadiness.IsReady(tick, cooldownEndTick) = tick >= cooldownEndTick. No HUD xUnit host
    exists under game/src (the solution holds sim, content, bots, and playtest projects only), so the rule is proven
    here through the real HUD path, not by an xUnit test.

    RED on the base commit: no "HUD party kick" line exists (k1 to k4 all fail).

    Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Hud/KickReadySceneTest.ps1
    Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 1500
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

# --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
$guard = (3 * $Ticks) + 240
$ErrorActionPreference = 'Continue'
$text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks 2>&1 | Out-String
$rc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($rc -eq 2 -and $text -match 'Godot binary not found') { Write-Output 'KickReadySceneTest: CANNOT RUN (Godot binary not found)'; exit 2 }
if ($rc -ne 0) { $failures.Add("godot exited $rc (expected 0)") }
$all = @($text -split "`r?`n")

$kickLines = @($all | Where-Object { $_ -match '^HUD party kick seat=\d+ ability=\S+ state=(ready|cooldown left=\d+)$' })

# k1. every party seat starts ready on its own class Interrupt slot.
$expect = @{ 1 = 'warden.shieldBash'; 2 = 'chanter.rebuke'; 3 = 'cinder.quench' }
foreach ($seat in 1..3) {
    $first = $kickLines | Where-Object { $_ -match "^HUD party kick seat=$seat " } | Select-Object -First 1
    if (-not $first) { $failures.Add("k1: no 'HUD party kick seat=$seat' line (the frame shows no kick readout)"); continue }
    if ($first -notmatch " ability=$([regex]::Escape($expect[$seat])) state=ready$") {
        $failures.Add("k1: seat $seat's first kick line is not 'ready' on $($expect[$seat]): '$first'")
    }
}

# k2. the Warden's kick is spent in pull 1, and the arc starts full (left <= 480).
$cool1 = @($kickLines | Where-Object { $_ -match '^HUD party kick seat=1 ability=warden\.shieldBash state=cooldown left=(\d+)$' })
if ($cool1.Count -eq 0) {
    $failures.Add('k2: no "HUD party kick seat=1 ability=warden.shieldBash state=cooldown" line (the Warden never spent its kick)')
}
foreach ($l in $cool1) {
    $left = [int]([regex]::Match($l, 'left=(\d+)$').Groups[1].Value)
    if ($left -le 0 -or $left -gt 480) { $failures.Add("k2: seat 1 cooldown left=$left outside (0, 480]: '$l'") }
}

# k3. the arc drains: at least three distinct left values, falling in print order.
$lefts = @($cool1 | ForEach-Object { [int]([regex]::Match($_, 'left=(\d+)$').Groups[1].Value) })
if (@($lefts | Select-Object -Unique).Count -lt 3) {
    $failures.Add("k3: fewer than three distinct seat 1 cooldown values (the arc did not drain): [$($lefts -join ',')]")
} else {
    for ($i = 1; $i -lt $lefts.Count; $i++) {
        if ($lefts[$i] -ge $lefts[$i - 1]) { $failures.Add("k3: seat 1 cooldown left did not fall: $($lefts[$i - 1]) then $($lefts[$i])"); break }
    }
}

# k4. the pip returns once the cooldown ends.
$ready = $null
if ($cool1.Count -gt 0) {
    $afterCool = $false
    foreach ($l in $kickLines) {
        if ($l -eq $cool1[0]) { $afterCool = $true; continue }
        if ($afterCool -and $l -match '^HUD party kick seat=1 ability=warden\.shieldBash state=ready$') { $ready = $l; break }
    }
}
if (-not $ready) { $failures.Add('k4: no seat 1 "state=ready" line after its cooldown (the pip never came back)') }

Write-Output '--- asserted lines ---'
foreach ($l in @($kickLines | Where-Object { $_ -match '^HUD party kick seat=[123] ' } | Select-Object -First 3) + @($cool1 | Select-Object -First 2) + @($ready)) {
    if ($l) { Write-Output $l }
}
if ($failures.Count -gt 0) {
    Write-Output "KickReadySceneTest: FAIL ($($failures.Count))"
    $failures | Select-Object -First 8 | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "KickReadySceneTest: PASS ticks=$Ticks cooldownLines=$($cool1.Count)"
exit 0
