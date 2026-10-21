<#
.SYNOPSIS
  Tests for the run-log read at the end of tools/godot/screens.ps1 (a lock on the log must not abort the capture).

.DESCRIPTION
  Never launches the game and never runs Godot. Each case builds a fixture repo under the temp folder with copies of
  screens.ps1, launch-lock.ps1 and preflight.ps1, a project folder with a fresh assembly, and KEYRUN_GODOT pointing at a
  stub .cmd that exits at once. A separate holder process, started by this test and outside screens.ps1's job object,
  opens the run log with FileShare.None while screens.ps1 runs, so the read after the game exits meets a lock.

  Cases:
    K1  the holder keeps the log locked for 1.5 s (a transient lock) and writes one ACTOR line: screens.ps1 retries the
        read, prints 'SCREENS models actors=1 fallback=0', never throws, and exits with a documented code (0 or 1; the stub captures no frame)
    K2  the holder keeps the log locked for 8 s (a persistent lock): screens.ps1 prints exactly one
        'SCREENS log-locked ...' line, skips the model gate, never throws, and exits 1 (no frame)

  Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/screens-log-lock-test.ps1
  Exit 0 = PASS, 1 = FAIL.
#>
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('keyrun-screens-log-lock-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

$failures = New-Object System.Collections.Generic.List[string]

function Check([string]$case, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Output "ok   $case" } else { Write-Output "FAIL $case :: $detail"; $failures.Add("$case :: $detail") }
}

function Write-Text([string]$path, [string[]]$lines) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [IO.File]::WriteAllLines($path, $lines, (New-Object System.Text.UTF8Encoding($false)))
}

# Holder: waits until screens.ps1 removes the previous log, then holds a new log open with FileShare.None for HoldMs.
$holderScript = @'
param([string]$Log, [int]$HoldMs)
$deadline = (Get-Date).AddSeconds(30)
while ((Test-Path -LiteralPath $Log) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 10 }
$fs = [IO.File]::Open($Log, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes("ACTOR model=capsule fallback=0 id=1`r`n")
    $fs.Write($bytes, 0, $bytes.Length)
    $fs.Flush()
    Start-Sleep -Milliseconds $HoldMs
} finally {
    $fs.Dispose()
}
'@

# One case: builds the fixture, starts the holder, runs screens.ps1 to completion. Returns output lines and exit code.
function Invoke-LockCase([string]$tag, [int]$HoldMs) {
    $root = Join-Path $work $tag
    $tools = Join-Path $root 'tools\godot'
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    foreach ($f in @('screens.ps1', 'launch-lock.ps1', 'preflight.ps1')) {
        Copy-Item -LiteralPath (Join-Path $here $f) -Destination (Join-Path $tools $f)
    }
    $proj = Join-Path $root 'game'
    Write-Text (Join-Path $proj 'project.godot') @('config_version=5', '', '[dotnet]', '', 'project/assembly_name="Keyrun"')
    Write-Text (Join-Path $proj 'Keyrun.csproj') @('<Project Sdk="Godot.NET.Sdk/4.7.2">', '</Project>')
    $boot = Join-Path $proj 'src\Boot.cs'
    Write-Text $boot @('class Boot { }')
    (Get-Item -LiteralPath $boot).LastWriteTimeUtc = [DateTime]::Parse('2000-01-01T00:00:00Z').ToUniversalTime()
    $dll = Join-Path $proj '.godot\mono\temp\bin\Debug\Keyrun.dll'
    Write-Text $dll @('MZ')
    (Get-Item -LiteralPath $dll).LastWriteTimeUtc = (Get-Date).ToUniversalTime()

    # Stub Godot: exits at once and writes nothing.
    $stubDir = Join-Path $root 'stub'
    Write-Text (Join-Path $stubDir 'godot.cmd') @('@echo off', 'exit /b 0')
    # Fake .NET root: screens.ps1 only checks that dotnet.exe exists there.
    $dotnetRoot = Join-Path $root 'dotnet'
    Write-Text (Join-Path $dotnetRoot 'dotnet.exe') @('MZ')

    $logPath = Join-Path $root 'run-log.txt'
    Write-Text $logPath @('stale line from the previous run')
    $holderPath = Join-Path $root 'holder.ps1'
    Write-Text $holderPath ($holderScript -split "`r?`n")

    $holder = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $holderPath, '-Log', $logPath, '-HoldMs', [string]$HoldMs) -PassThru -WindowStyle Hidden

    $savedGodot = $env:KEYRUN_GODOT
    $savedDotnet = $env:DOTNET_ROOT
    $env:KEYRUN_GODOT = (Join-Path $stubDir 'godot.cmd')
    $env:DOTNET_ROOT = $dotnetRoot
    try {
        $ErrorActionPreference = 'Continue'
        $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $tools 'screens.ps1') `
            -OutDir (Join-Path $root 'frames') -Name lock -IntervalSeconds 1 -MaxSeconds 20 -NoVideo `
            -GodotArgs ("--path {0} --log {1}" -f $proj, $logPath) 2>&1
        $code = $LASTEXITCODE
    } finally {
        $env:KEYRUN_GODOT = $savedGodot
        $env:DOTNET_ROOT = $savedDotnet
        $holder.WaitForExit(20000) | Out-Null
    }
    return [pscustomobject]@{ Out = @($out | ForEach-Object { [string]$_ }); Code = $code }
}

# K1: transient lock, the read succeeds on a retry.
$k1 = Invoke-LockCase 'k1-transient' 1500
$k1Text = $k1.Out -join "`n"
Check 'K1.models-line-printed' ($k1Text -match 'SCREENS models actors=1 fallback=0') ("exit={0} output:`n{1}" -f $k1.Code, $k1Text)
Check 'K1.no-lock-exception' (-not ($k1Text -match 'being used by another process|Exception calling')) ("output:`n{0}" -f $k1Text)
Check 'K1.documented-exit' (@(0, 1) -contains $k1.Code) ("exit={0}" -f $k1.Code)

# K2: persistent lock, one line, the gate is skipped, the run ends with a documented code.
$k2 = Invoke-LockCase 'k2-persistent' 8000
$k2Text = $k2.Out -join "`n"
$lockLines = @($k2.Out | Where-Object { $_ -match '^SCREENS log-locked ' })
Check 'K2.one-log-locked-line' ($lockLines.Count -eq 1) ("count={0} output:`n{1}" -f $lockLines.Count, $k2Text)
Check 'K2.no-lock-exception' (-not ($k2Text -match 'being used by another process|Exception calling')) ("output:`n{0}" -f $k2Text)
Check 'K2.documented-exit' ($k2.Code -eq 1) ("exit={0} output:`n{1}" -f $k2.Code, $k2Text)

Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue

if ($failures.Count -gt 0) {
    Write-Output ("screens-log-lock-test: FAIL ({0})" -f $failures.Count)
    exit 1
}
Write-Output 'screens-log-lock-test: PASS'
exit 0
