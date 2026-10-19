<#
.SYNOPSIS
    Lane WARDEN-ENGAGES (KF2b): the autorun Warden (ScriptedWarden, PlayerInput.cs) keeps fighting a pull until it is
    clear. From the pull's pull_start EVENT to its pull_clear EVENT he lands damage (an EVENT with "sourceId":1 and a
    positive "modifiedAmount" on one of the pull's actorIds) at least once every MaxGap ticks: pull_start to the first
    hit, hit to hit, and the last hit to pull_clear are each at most MaxGap.

    On main 94ba345 the Choir fails it: the Warden kicks and kills the lieutenant (actor 102, dies 1126) then stands
    still looping Guard Break into empty air until the Cinder kills both melee (pull_clear 3097).

.DESCRIPTION
    Two ways in:
      -Lines <string[]>   the output lines of one autorun (HudSceneTest run C passes its first run's lines); the
                          failures come back as strings, nothing is printed.
      -LogPath <file>     a saved run log (godot stdout, or a --log file); prints each failure, then
                          "WardenEngagesTest: PASS" or "WardenEngagesTest: FAIL" and exits 0 or 1.
#>
param(
    [string[]]$Lines,
    [string]$LogPath,
    [string]$PullId = 'choir',
    [int]$MaxGap = 300
)
$ErrorActionPreference = 'Stop'

function Get-EngageFailures([string[]]$all, [string]$pull, [int]$gap) {
    $out = New-Object System.Collections.Generic.List[string]
    $p = [regex]::Escape($pull)
    $start = $all | Where-Object { $_ -match "^EVENT \{`"actorIds`":\[([\d,]+)\],`"pullId`":`"$p`",.*`"tick`":(\d+),`"type`":`"pull_start`"" } | Select-Object -First 1
    if (-not $start) { $out.Add("warden-engages: no pull_start EVENT for pull '$pull'"); return , $out }
    $null = $start -match '"actorIds":\[([\d,]+)\].*"tick":(\d+),'
    $ids = @($Matches[1] -split ',' | ForEach-Object { [int]$_ })
    $t0 = [int]$Matches[2]
    $clear = $all | Where-Object { $_ -match "^EVENT \{`"pullId`":`"$p`",.*`"tick`":(\d+),`"type`":`"pull_clear`"" } | Select-Object -First 1
    if (-not $clear) { $out.Add("warden-engages: no pull_clear EVENT for pull '$pull'"); return , $out }
    $null = $clear -match '"tick":(\d+),'
    $t1 = [int]$Matches[1]
    $hits = New-Object System.Collections.Generic.List[int]
    foreach ($l in $all) {
        if ($l -match '^EVENT \{.*"modifiedAmount":([1-9]\d*),.*"sourceId":1,"targetId":(\d+),"tick":(\d+),"type":"damage"') {
            $tick = [int]$Matches[3]
            if ($ids -contains [int]$Matches[2] -and $tick -ge $t0 -and $tick -le $t1) { $hits.Add($tick) }
        }
    }
    $marks = @($t0) + @($hits | Sort-Object -Unique) + @($t1)
    for ($i = 1; $i -lt $marks.Count; $i++) {
        if ($marks[$i] - $marks[$i - 1] -gt $gap) {
            $out.Add("warden-engages: pull '$pull' (start $t0, clear $t1): the Warden landed no damage on the pull between ticks $($marks[$i - 1]) and $($marks[$i]) ($($marks[$i] - $marks[$i - 1]) > $gap)")
        }
    }
    return , $out
}

if ($LogPath) {
    $all = @(Get-Content -LiteralPath $LogPath)
    $f = Get-EngageFailures $all $PullId $MaxGap
    foreach ($x in $f) { Write-Output $x }
    if ($f.Count -gt 0) { Write-Output 'WardenEngagesTest: FAIL'; exit 1 }
    Write-Output 'WardenEngagesTest: PASS'
    exit 0
}
return (Get-EngageFailures @($Lines) $PullId $MaxGap)
