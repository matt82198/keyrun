<#
.SYNOPSIS
    Lane HUD-TEXT-OVERLAYS (finding f-6917dd70; Matt: "reduce screen clutter"). The controls hint (Arena.tscn
    Hud/Help, "Hollow Vault - WASD move, ... Esc frees the mouse") must not sit at the top of the screen for the whole
    run: it shows at run start, then fades out after HelpHint.HoldTicks of run time (100 ticks, 5 s) or at the human
    Warden's first completed cast, whichever comes first, over HelpHint.FadeTicks (20 ticks, 1 s).

    Reads the hint's own proof lines (HelpHint.cs prints them under --autorun and --replay-input; every alpha it prints
    is read back from the Label's Modulate) and asserts, for each run:

      a. one "HUD help shown tick=T alpha=1.00 hold=100 fade=20" line: the hint starts fully visible;
      b. one "HUD help fade tick=F shown=S reason=R" line, with
           run A (--autorun, the scripted Warden casts at tick 50): R=cast and F = the tick of the first
             cast_complete EVENT of actorId 1 (the first ability press dismisses the hint early);
           run B (--replay-input of the Enter+W walk fixture, the Warden never casts): R=time and S=100;
      c. "HUD help alpha" lines strictly decreasing from below 1 to 0 over the fade, the last one a=0.00;
      d. one "HUD help gone tick=G shown=S2 alpha=0.00" line with S2 = S + 20, and no "HUD help alpha" line after it
         (the hint stays faded for the rest of the run).

    On origin/main 042c3ad the Help label has no script and prints no "HUD help" line: every check fails (RED).

.DESCRIPTION
      -LinesA/-LinesB <string[]>  the output lines of the two runs; the failures come back as strings.
      (neither)                   runs both headless at --fixed-fps 60 and checks them; prints PASS or FAIL.
#>
param(
    [string[]]$LinesA,
    [string[]]$LinesB
)
$ErrorActionPreference = 'Stop'

function Get-HelpFailures([string]$run, [string[]]$all, [string]$wantReason) {
    $out = New-Object System.Collections.Generic.List[string]
    $shown = @($all | Where-Object { $_ -match '^HUD help shown ' })
    if ($shown.Count -ne 1 -or $shown[0] -notmatch '^HUD help shown tick=\d+ alpha=1\.00 hold=100 fade=20$') {
        $out.Add("help-hint $run a: want one 'HUD help shown tick=T alpha=1.00 hold=100 fade=20' line, got $($shown.Count): '$($shown -join ' | ')'")
    }
    $fade = @($all | Where-Object { $_ -match '^HUD help fade ' })
    if ($fade.Count -ne 1 -or $fade[0] -notmatch '^HUD help fade tick=(\d+) shown=(\d+) reason=(time|cast)$') {
        $out.Add("help-hint $run b: want one 'HUD help fade tick=F shown=S reason=time|cast' line, got $($fade.Count): '$($fade -join ' | ')'")
        return , $out
    }
    $fadeTick = [int]$Matches[1]; $fadeShown = [int]$Matches[2]; $reason = $Matches[3]
    if ($reason -ne $wantReason) { $out.Add("help-hint $run b: fade reason=$reason, want $wantReason") }
    $firstCast = $all | Where-Object { $_ -match '^EVENT \{.*"actorId":1,"tick":(\d+),"type":"cast_complete"\}$' } | Select-Object -First 1
    if ($wantReason -eq 'cast') {
        if (-not $firstCast) { $out.Add("help-hint $run b: no cast_complete EVENT for actorId 1, so the cast dismissal is not exercised") }
        else {
            $null = $firstCast -match '"tick":(\d+)'
            if ([int]$Matches[1] -ne $fadeTick) { $out.Add("help-hint $run b: fade tick=$fadeTick, want the first Warden cast_complete tick $($Matches[1])") }
            if ($fadeShown -ge 100) { $out.Add("help-hint $run b: cast dismissal at shown=$fadeShown is not earlier than the 100-tick hold") }
        }
    } else {
        if ($firstCast) { $out.Add("help-hint $run b: the Warden cast ('$firstCast'), so the time path is not exercised") }
        if ($fadeShown -ne 100) { $out.Add("help-hint $run b: time fade at shown=$fadeShown, want 100") }
    }
    # c. the fade itself, read back from the Label's Modulate.
    $alphas = @()
    $gone = $null
    $afterGone = $null
    foreach ($l in $all) {
        if ($l -match '^HUD help alpha tick=\d+ shown=\d+ a=(\d\.\d\d)$') {
            if ($gone) { $afterGone = $l; break }
            $alphas += [double]$Matches[1]
        } elseif ($l -match '^HUD help gone ') { $gone = $l }
    }
    if ($alphas.Count -lt 2) { $out.Add("help-hint $run c: want the fade as 'HUD help alpha' lines, got $($alphas.Count)") }
    else {
        if ($alphas[0] -ge 1.0) { $out.Add("help-hint $run c: first fade alpha $($alphas[0]) is not below 1") }
        for ($i = 1; $i -lt $alphas.Count; $i++) {
            if ($alphas[$i] -ge $alphas[$i - 1]) { $out.Add("help-hint $run c: alpha rose or held: $($alphas[$i - 1]) -> $($alphas[$i])"); break }
        }
        if ($alphas[-1] -ne 0.0) { $out.Add("help-hint $run c: last alpha $($alphas[-1]), want 0.00") }
    }
    # d. gone, and stays gone.
    if (-not $gone -or $gone -notmatch '^HUD help gone tick=\d+ shown=(\d+) alpha=0\.00$') { $out.Add("help-hint $run d: no 'HUD help gone tick=G shown=S alpha=0.00' line") }
    elseif ([int]$Matches[1] -ne $fadeShown + 20) { $out.Add("help-hint $run d: gone at shown=$($Matches[1]), want $($fadeShown + 20)") }
    if ($afterGone) { $out.Add("help-hint $run d: the hint changed after it was gone: '$afterGone'") }
    return , $out
}

function Get-AllFailures([string[]]$a, [string[]]$b) {
    $f = New-Object System.Collections.Generic.List[string]
    foreach ($x in (Get-HelpFailures 'A' $a 'cast')) { $f.Add($x) }
    foreach ($x in (Get-HelpFailures 'B' $b 'time')) { $f.Add($x) }
    return , $f
}

if ($PSBoundParameters.ContainsKey('LinesA')) { return (Get-AllFailures @($LinesA) @($LinesB)) }

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$game = Join-Path $repo 'game'
$walk = Join-Path $game 'fixtures/input/example-yaw-walk.input-log.json'
$ErrorActionPreference = 'Continue'
$textA = & $wrapper --headless --path $game --fixed-fps 60 --quit-after 1500 '--' --autorun --ticks 400 2>&1 | Out-String
$rcA = $LASTEXITCODE
$textB = & $wrapper --headless --path $game --fixed-fps 60 --quit-after 600 '--' --replay-input $walk 2>&1 | Out-String
$rcB = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($rcA -eq 2 -and $textA -match 'Godot binary not found') { Write-Output 'HelpHintSceneTest: CANNOT RUN (Godot binary not found)'; exit 2 }
$f = Get-AllFailures @($textA -split "`r?`n") @($textB -split "`r?`n")
if ($rcA -ne 0) { $f.Add("help-hint A: godot exited $rcA (expected 0)") }
if ($rcB -ne 0) { $f.Add("help-hint B: godot exited $rcB (expected 0)") }
foreach ($x in $f) { Write-Output $x }
if ($f.Count -gt 0) { Write-Output 'HelpHintSceneTest: FAIL'; exit 1 }
Write-Output 'HelpHintSceneTest: PASS'
exit 0
