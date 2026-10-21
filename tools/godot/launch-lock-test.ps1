<#
.SYNOPSIS
  Tests for the single-instance Godot launch lock and wall-clock cap in tools/godot/.

.DESCRIPTION
  Never launches Godot. Every case points KEYRUN_GODOT at a stub .cmd (written to a temp folder) that echoes its
  arguments, optionally sleeps in a marked powershell child (so a process-tree kill can be checked), and exits with
  STUB_EXIT. The lock file is redirected to the temp folder with KEYRUN_LAUNCH_LOCK_FILE, so the real machine lock
  is never touched.

  Cases:
    A  two concurrent godot.ps1 launches: the second fails fast with the lock message, the first succeeds
    B  a launch past KEYRUN_GODOT_TIMEOUT_SEC is killed with its whole process tree and exits nonzero
    C1 the lock is released after a normal exit
    C2 the lock is recovered after its owner is killed (owner PID dead), and the owner's game tree dies with it
    C3 a lock whose owner PID is alive blocks however old it is; a lock whose owner PID is dead does not
    D  a sequence holder (Enter-KeyrunLaunchLock) runs its launches serially in-process; another process is refused
    E  output reaches the caller's pipeline and the stub's exit code passes through
    F  godot.sh honours the same lock
    H  godot.ps1 preflight fails closed before the lock: a project without its assembly, or a DOTNET_ROOT without
       dotnet.exe, exits 6, never runs the stub and leaves no lock file. Every other launch names a fixture project
       (--path <temp>/proj) with a fresh stand-in assembly, so the real game/ tree is never consulted.
    G  static routing (parse only, nothing runs): every game/src harness launches only through godot.ps1, every
       multi-launch harness holds one lock for its sequence, godot.ps1 and screens.ps1 take the lock and the cap

  Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/launch-lock-test.ps1
  Exit 0 = PASS, 1 = FAIL.
#>
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$wrapper = Join-Path $here 'godot.ps1'
$wrapperSh = Join-Path $here 'godot.sh'
$helper = Join-Path $here 'launch-lock.ps1'

$work = Join-Path ([IO.Path]::GetTempPath()) ('keyrun-launch-lock-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$stub = Join-Path $work 'stub-godot.cmd'
$stubText = @(
    '@echo off',
    'echo STUB start %*',
    'if "%STUB_SLEEP%"=="" set STUB_SLEEP=0',
    'if "%STUB_EXIT%"=="" set STUB_EXIT=0',
    'powershell -NoProfile -NonInteractive -Command "$null=''%STUB_MARKER%''; Start-Sleep -Seconds %STUB_SLEEP%"',
    'echo STUB end',
    'exit /b %STUB_EXIT%'
)
[IO.File]::WriteAllLines($stub, $stubText)
$lockFile = Join-Path $work 'godot-launch.lock'

# godot.ps1 runs the .NET preflight (tools/godot/preflight.ps1) before the lock, so every launch here names a fixture
# project (--path <absolute>) holding a fresh stand-in assembly; the real game/ tree is never consulted.
function New-PreflightFixture([string]$dir) {
    $dll = Join-Path $dir '.godot\mono\temp\bin\Debug\Keyrun.dll'
    New-Item -ItemType Directory -Path (Split-Path -Parent $dll) -Force | Out-Null
    [IO.File]::WriteAllLines((Join-Path $dir 'project.godot'), @('[dotnet]', '', 'project/assembly_name="Keyrun"'))
    [IO.File]::WriteAllText($dll, 'MZ')
    return $dir
}
$proj = New-PreflightFixture (Join-Path $work 'proj')
$projSpaced = New-PreflightFixture (Join-Path $work 'a b')

# Hard guard: this test must never resolve the real engine.
$env:KEYRUN_GODOT = $stub
$env:KEYRUN_LAUNCH_LOCK_FILE = $lockFile
Remove-Item Env:KEYRUN_LAUNCH_LOCK_TOKEN -ErrorAction SilentlyContinue
Remove-Item Env:KEYRUN_GODOT_TIMEOUT_SEC -ErrorAction SilentlyContinue
Remove-Item Env:KEYRUN_USER_ARGS -ErrorAction SilentlyContinue
if ($env:KEYRUN_GODOT -ne $stub) { throw 'KEYRUN_GODOT is not the stub; refusing to run' }

$failures = New-Object System.Collections.Generic.List[string]
$markers = New-Object System.Collections.Generic.List[string]

function Set-Stub([int]$sleep, [string]$marker, [int]$exit = 0) {
    $env:STUB_SLEEP = "$sleep"
    $env:STUB_EXIT = "$exit"
    $env:STUB_MARKER = $marker
    if ($marker) { $markers.Add($marker) }
}

function New-Marker { return 'kr-stub-' + [guid]::NewGuid().ToString('N') }

function Get-MarkerProcs([string]$marker) {
    return @(Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.Contains($marker) })
}

function Stop-Tree([int]$id) {
    & taskkill.exe /T /F /PID $id 2>&1 | Out-Null
}

# Start a launcher (godot.ps1 or godot.sh) in its own process; stdout/stderr go to files.
function Start-Launcher([string]$tag, [switch]$Sh) {
    $out = Join-Path $work "$tag.out.txt"
    $err = Join-Path $work "$tag.err.txt"
    if ($Sh) {
        $p = Start-Process -FilePath 'bash.exe' -ArgumentList @($wrapperSh.Replace('\', '/'), '--headless', '--path', $proj.Replace('\', '/'), '--', '--smoke') `
            -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    } else {
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $wrapper, '--headless', '--path', $proj, '--smoke') `
            -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    }
    $null = $p.Handle  # cache the handle so ExitCode survives the exit
    return [pscustomobject]@{ Proc = $p; Out = $out; Err = $err; Started = Get-Date }
}

# Wait for a launcher; a launcher still alive after $seconds is tree-killed and reported as TimedOut.
function Wait-Launcher($l, [int]$seconds) {
    $done = $l.Proc.WaitForExit($seconds * 1000)
    if (-not $done) { Stop-Tree $l.Proc.Id; $l.Proc.WaitForExit(10000) | Out-Null }
    $text = ''
    foreach ($f in @($l.Out, $l.Err)) { if (Test-Path -LiteralPath $f) { $text += [IO.File]::ReadAllText($f) } }
    $code = $null
    if ($done) { $code = $l.Proc.ExitCode }
    return [pscustomobject]@{ TimedOut = (-not $done); Code = $code; Text = $text; Seconds = ((Get-Date) - $l.Started).TotalSeconds }
}

function Wait-For([scriptblock]$cond, [int]$seconds) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
        if (& $cond) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return [bool](& $cond)
}

function Write-ForgedLock([int]$ownerPid, [string]$startTicks, [string]$acquired) {
    $json = '{"pid":' + $ownerPid + ',"start":"' + $startTicks + '","token":"forged-' + [guid]::NewGuid().ToString('N') +
        '","owner":"forged-test-owner","acquired":"' + $acquired + '"}'
    [IO.File]::WriteAllText($lockFile, $json)
}

function Check([string]$case, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Output "ok   $case" } else { Write-Output "FAIL $case :: $detail"; $failures.Add("$case :: $detail") }
}

$lockMsg = 'another Godot instance holds the launch lock'

try {
    # ---- A: concurrent launches ------------------------------------------------------------------------------
    Set-Stub 8 (New-Marker)
    $a1 = Start-Launcher 'A1'
    $held = Wait-For { Test-Path -LiteralPath $lockFile } 15
    Check 'A.lock-file-appears' $held "no lock file at $lockFile while the first launch runs"
    Set-Stub 0 ''
    $a2 = Wait-Launcher (Start-Launcher 'A2') 30
    Check 'A.second-fails-fast' ((-not $a2.TimedOut) -and $a2.Seconds -lt 6) ("second launch took {0:N1}s timedOut={1}" -f $a2.Seconds, $a2.TimedOut)
    Check 'A.second-exits-nonzero' ($a2.Code -ne $null -and $a2.Code -ne 0) "second launch exit=$($a2.Code)"
    Check 'A.second-says-lock-held' ($a2.Text -match [regex]::Escape($lockMsg)) "second launch output: $($a2.Text.Trim())"
    Check 'A.second-did-not-run-stub' ($a2.Text -notmatch 'STUB start') 'second launch ran the binary'
    $a1r = Wait-Launcher $a1 40
    Check 'A.first-succeeds' ((-not $a1r.TimedOut) -and $a1r.Code -eq 0) "first launch exit=$($a1r.Code) timedOut=$($a1r.TimedOut)"

    # ---- C1: released after normal exit ------------------------------------------------------------------------
    Check 'C1.released-after-exit' (-not (Test-Path -LiteralPath $lockFile)) 'lock file still present after a normal exit'

    # ---- B: wall-clock cap kills the whole tree ----------------------------------------------------------------
    $mB = New-Marker
    Set-Stub 120 $mB
    $env:KEYRUN_GODOT_TIMEOUT_SEC = '3'
    $b = Wait-Launcher (Start-Launcher 'B') 30
    Remove-Item Env:KEYRUN_GODOT_TIMEOUT_SEC
    Check 'B.killed-by-cap' ((-not $b.TimedOut) -and $b.Seconds -lt 20) ("launch ran {0:N1}s timedOut={1}" -f $b.Seconds, $b.TimedOut)
    Check 'B.exits-nonzero' ($b.Code -ne $null -and $b.Code -ne 0) "exit=$($b.Code)"
    Check 'B.says-timeout' ($b.Text -match 'wall-clock') "output: $($b.Text.Trim())"
    $gone = Wait-For { (Get-MarkerProcs $mB).Count -eq 0 } 10
    Check 'B.tree-killed' $gone 'the stub''s child process survived the timeout kill'
    Check 'B.released-after-kill' (-not (Test-Path -LiteralPath $lockFile)) 'lock file still present after a timeout kill'

    # ---- C2: owner killed -> stale by PID, and the game tree dies with the owner -------------------------------
    $mC = New-Marker
    Set-Stub 120 $mC
    $c2 = Start-Launcher 'C2'
    $null = Wait-For { (Get-MarkerProcs $mC).Count -gt 0 } 20
    Stop-Process -Id $c2.Proc.Id -Force
    $null = $c2.Proc.WaitForExit(10000)
    $orphanGone = Wait-For { (Get-MarkerProcs $mC).Count -eq 0 } 10
    Check 'C2.game-dies-with-owner' $orphanGone 'the stub tree outlived its killed godot.ps1 owner'
    Set-Stub 0 ''
    $c2b = Wait-Launcher (Start-Launcher 'C2b') 30
    Check 'C2.recovers-dead-owner' ((-not $c2b.TimedOut) -and $c2b.Code -eq 0) "launch after a killed owner exit=$($c2b.Code): $($c2b.Text.Trim())"

    # ---- C3: liveness, not age ----------------------------------------------------------------------------------
    $sleeper = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 60') -PassThru -WindowStyle Hidden
    try {
        $ticks = $sleeper.StartTime.ToUniversalTime().Ticks
        Write-ForgedLock $sleeper.Id "$ticks" '2000-01-01T00:00:00Z'
        $c3 = Wait-Launcher (Start-Launcher 'C3live') 30
        Check 'C3.old-live-owner-blocks' ($c3.Code -ne $null -and $c3.Code -ne 0 -and $c3.Text -match [regex]::Escape($lockMsg)) "exit=$($c3.Code): $($c3.Text.Trim())"
        Check 'C3.live-owner-lock-kept' (Test-Path -LiteralPath $lockFile) 'a live owner''s lock was removed'
    } finally {
        Stop-Tree $sleeper.Id
    }
    $null = $sleeper.WaitForExit(10000)
    Write-ForgedLock $sleeper.Id "$ticks" ([DateTime]::UtcNow.ToString('o'))
    $c3d = Wait-Launcher (Start-Launcher 'C3dead') 30
    Check 'C3.fresh-dead-owner-recovered' ((-not $c3d.TimedOut) -and $c3d.Code -eq 0) "exit=$($c3d.Code): $($c3d.Text.Trim())"
    Check 'C3.released-after-recovery' (-not (Test-Path -LiteralPath $lockFile)) 'lock file left behind after the recovered launch'

    # ---- D: a sequence holder runs launches in-process; others are refused --------------------------------------
    $dOk = $true
    try { . $helper } catch { $dOk = $false; Check 'D.helper-loads' $false "cannot dot-source ${helper}: $($_.Exception.Message)" }
    if ($dOk) {
        Set-Stub 0 ''
        $seq = Enter-KeyrunLaunchLock -Owner 'launch-lock-test sequence'
        try {
            $ErrorActionPreference = 'Continue'
            $t1 = & $wrapper --headless --path $proj '--' --one 2>&1 | Out-String
            $r1 = $LASTEXITCODE
            $t2 = & $wrapper --headless --path $proj '--' --two 2>&1 | Out-String
            $r2 = $LASTEXITCODE
            $ErrorActionPreference = 'Stop'
            Check 'D.nested-launches-run' ($r1 -eq 0 -and $r2 -eq 0 -and $t1 -match 'STUB start' -and $t2 -match 'STUB start') "r1=$r1 r2=$r2 t1=$($t1.Trim()) t2=$($t2.Trim())"
            $savedToken = $env:KEYRUN_LAUNCH_LOCK_TOKEN
            Remove-Item Env:KEYRUN_LAUNCH_LOCK_TOKEN -ErrorAction SilentlyContinue
            $other = Wait-Launcher (Start-Launcher 'Dother') 30
            $env:KEYRUN_LAUNCH_LOCK_TOKEN = $savedToken
            Check 'D.other-process-refused' ($other.Code -ne $null -and $other.Code -ne 0 -and $other.Text -match [regex]::Escape($lockMsg)) "exit=$($other.Code): $($other.Text.Trim())"
            Check 'D.lock-held-through-sequence' (Test-Path -LiteralPath $lockFile) 'a nested launch released the sequence lock'
        } finally {
            Exit-KeyrunLaunchLock $seq
        }
        Check 'D.released-by-holder' (-not (Test-Path -LiteralPath $lockFile)) 'lock file still present after Exit-KeyrunLaunchLock'
        Check 'D.token-cleared' (-not $env:KEYRUN_LAUNCH_LOCK_TOKEN) 'KEYRUN_LAUNCH_LOCK_TOKEN still set after Exit-KeyrunLaunchLock'
    }

    # ---- E: pipeline output and exit code pass through ------------------------------------------------------------
    Set-Stub 0 '' 7
    $ErrorActionPreference = 'Continue'
    $e = & $wrapper --headless --path $projSpaced '--' --smoke 2>&1 | Out-String
    $eCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Check 'E.exit-code-passthrough' ($eCode -eq 7) "exit=$eCode"
    Check 'E.output-in-pipeline' ($e -match [regex]::Escape('STUB start --headless --path "' + $projSpaced + '" -- --smoke') -and $e -match 'STUB end') "output: $($e.Trim())"

    # ---- H: preflight fails closed before the lock (exit 6, the stub never runs, no lock file) ----------------------
    Set-Stub 0 ''
    $noAsm = Join-Path $work 'no-assembly'
    New-Item -ItemType Directory -Path $noAsm | Out-Null
    [IO.File]::WriteAllLines((Join-Path $noAsm 'project.godot'), @('[dotnet]', '', 'project/assembly_name="Keyrun"'))
    $ErrorActionPreference = 'Continue'
    $h1 = & $wrapper --headless --path $noAsm '--' --smoke 2>&1 | Out-String
    $h1Code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Check 'H1.missing-assembly-exit-6' ($h1Code -eq 6 -and $h1 -match 'PREFLIGHT missing-assembly ') "exit=$h1Code output: $($h1.Trim())"
    Check 'H1.stub-not-run' ($h1 -notmatch 'STUB start') 'the engine ran despite a failed preflight'
    Check 'H1.no-lock-taken' (-not (Test-Path -LiteralPath $lockFile)) 'a failed preflight left a lock file'
    $savedRoot = $env:DOTNET_ROOT
    $savedPath = $env:PATH
    try {
        $env:DOTNET_ROOT = Join-Path $work 'no-such-dotnet'
        $ErrorActionPreference = 'Continue'
        $h2 = & $wrapper --headless --path $proj '--' --smoke 2>&1 | Out-String
        $h2Code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
    } finally {
        $env:DOTNET_ROOT = $savedRoot
        if (-not $savedRoot) { Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue }
        $env:PATH = $savedPath
    }
    Check 'H2.missing-dotnet-exit-6' ($h2Code -eq 6 -and $h2 -match 'PREFLIGHT missing-dotnet ') "exit=$h2Code output: $($h2.Trim())"
    Check 'H2.stub-not-run' ($h2 -notmatch 'STUB start') 'the engine ran without a .NET runtime'

    # ---- F: godot.sh shares the lock ------------------------------------------------------------------------------
    if (Get-Command bash.exe -ErrorAction SilentlyContinue) {
        Set-Stub 0 ''
        $live = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 60') -PassThru -WindowStyle Hidden
        try {
            Write-ForgedLock $live.Id ("{0}" -f $live.StartTime.ToUniversalTime().Ticks) ([DateTime]::UtcNow.ToString('o'))
            $f = Wait-Launcher (Start-Launcher 'Fsh' -Sh) 30
            Check 'F.sh-refused-while-held' ($f.Code -ne $null -and $f.Code -ne 0 -and $f.Text -match [regex]::Escape($lockMsg)) "exit=$($f.Code): $($f.Text.Trim())"
        } finally {
            Stop-Tree $live.Id
        }
        $null = $live.WaitForExit(10000)
        $f2 = Wait-Launcher (Start-Launcher 'Fsh2' -Sh) 30
        Check 'F.sh-runs-when-free' ((-not $f2.TimedOut) -and $f2.Code -eq 0 -and $f2.Text -match 'STUB start') "exit=$($f2.Code): $($f2.Text.Trim())"
    } else {
        Write-Output 'skip F (bash.exe not on PATH)'
    }

    # ---- G: static routing: every launcher goes through the lock (parsed, never run) -------------------------------
    $repo = (Resolve-Path (Join-Path $here '..\..')).Path
    $launchers = @(Get-ChildItem -Path (Join-Path $repo 'game\src') -Recurse -Filter '*.ps1') + @(Get-ChildItem -Path $here -Filter '*.ps1')
    foreach ($file in $launchers) {
        if ($file.FullName -eq $PSCommandPath) { continue }
        $tokens = $null; $errs = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errs)
        $rel = $file.FullName.Substring($repo.Length + 1)
        $text = [IO.File]::ReadAllText($file.FullName)
        $isLauncher = ($text -match 'tools/godot/godot\.ps1' -and $file.Name -match 'Test\.ps1$') -or $file.Name -in @('godot.ps1', 'screens.ps1')
        if (-not $isLauncher) { continue }
        Check "G.parses $rel" ($errs.Count -eq 0) (($errs | ForEach-Object { $_.Message }) -join '; ')
        # A harness never starts the engine itself: its only launch is & $wrapper (godot.ps1).
        if ($file.Name -match 'Test\.ps1$') {
            Check "G.no-direct-launch $rel" ($text -notmatch 'Start-Process|Godot_v4|KEYRUN_GODOT') 'starts a process or names the engine binary instead of going through tools/godot/godot.ps1'
            $calls = @($ast.FindAll({ param($n)
                        $n -is [System.Management.Automation.Language.CommandAst] -and
                        $n.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Ampersand -and
                        $n.CommandElements[0] -is [System.Management.Automation.Language.VariableExpressionAst] -and
                        $n.CommandElements[0].VariablePath.UserPath -eq 'wrapper' }, $true))
            $inFunction = @($calls | Where-Object { $p = $_.Parent; while ($p -and -not ($p -is [System.Management.Automation.Language.FunctionDefinitionAst])) { $p = $p.Parent }; $p })
            if ($calls.Count -ge 2 -or $inFunction.Count -gt 0) {
                Check "G.sequence-lock $rel" ($text -match 'Enter-KeyrunLaunchLock' -and $text -match 'Exit-KeyrunLaunchLock') ("{0} launches (or a launch helper) without one held launch lock" -f $calls.Count)
            }
        }
    }
    $godotText = [IO.File]::ReadAllText($wrapper)
    Check 'G.godot.ps1-capped' ($godotText -match 'Enter-KeyrunLaunchLock' -and $godotText -match 'Invoke-KeyrunCapped' -and $godotText -notmatch '(?m)^\s*&\s*\$godot\b') 'godot.ps1 runs the engine outside the lock or the cap'
    $screensText = [IO.File]::ReadAllText((Join-Path $here 'screens.ps1'))
    Check 'G.screens.ps1-locked' ($screensText -match 'Enter-KeyrunLaunchLock' -and $screensText -match 'Stop-KeyrunProcessTree' -and $screensText -match 'Attach\(\$proc\)' -and $screensText -notmatch 'Stop-Process') 'screens.ps1 launches outside the lock or stops the game without its tree'
} finally {
    foreach ($m in $markers) { foreach ($p in (Get-MarkerProcs $m)) { Stop-Tree ([int]$p.ProcessId) } }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Output ("launch-lock-test: FAIL ({0})" -f $failures.Count)
    exit 1
}
Write-Output 'launch-lock-test: PASS'
exit 0
