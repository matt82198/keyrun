<#
.SYNOPSIS
  Record a Keyrun run as PNG frames of the game window plus an mp4 (rule D15).

.DESCRIPTION
  Launches the Godot 4.7.2 .NET console binary WINDOWED (no --headless), finds the
  game window by enumerating the visible top-level windows owned by the Godot
  processes, and captures that window's rectangle every IntervalSeconds to
  <OutDir>\<Name>-NNN-tSSSs.png. A capture is skipped (logged as SKIP) when no
  game window is visible; from the third consecutive miss the primary screen is
  captured instead and named *-full.png.
  Stops when the run-end EVENT line appears in the --log file (one more interval is
  captured after it so the run-end panel is in the frames), when the game process
  exits, or when MaxSeconds passes. A process still alive at the end is
  stopped with its whole process tree. Then builds <OutDir>\<Name>.mp4 from this run's frames with
  ffmpeg, unless -NoVideo is given.

  The Godot binary is resolved the same way as tools/godot/godot.ps1: the
  KEYRUN_GODOT environment variable, else the winget install under %LOCALAPPDATA%.
  Exit 2 when it cannot be found.

  Preflight (tools/godot/preflight.ps1, finding f-3ccab3b2): before the launch lock is taken, the project's .NET assembly
  must exist under <project>/.godot/mono/temp/bin/Debug and be newer than the .cs files under src, sim, bots and content.
  Otherwise the engine would boot the boot scene only and the C# autorun would never run. This prints
  "PREFLIGHT missing-assembly <path>" or "PREFLIGHT stale-assembly <path> newer=<file>" with the build command and exits 6
  without launching. It never builds; the orchestrator runs the build. A missing .NET runtime (no dotnet.exe under the
  resolved DOTNET_ROOT) is the same exit 6 with "PREFLIGHT missing-dotnet <path>".

  Import cache (finding f-30dcbb08): when any .glb under <project>/assets has no fresh import cache, this runs
  tools/godot/import.ps1 first (its own lock and wall-clock cap; MaxSeconds covers only the game launch) and continues.
  Exit 6 only when the import itself fails or leaves the cache missing.

  End check: when the capture stops, the --log file named in GodotArgs must exist. When it was never created the
  frames and the mp4 are kept, and this prints "SCREENS no-run-log <path>" and exits 7. A boot-scene-only run is never a success.

  Model gate (finding f-30dcbb08): src/View/ActorView.cs writes "ACTOR model=<res path|capsule> fallback=<0|1> id=<n>"
  per bound actor to the run log. This prints "SCREENS models actors=<n> fallback=<k>"; when k > 0 it prints
  "SCREENS FAIL model-fallback <k> actors rendered as capsules" and exits 9 (frames and mp4 kept). When the run log
  stays locked through every snapshot retry, the gate cannot run: this prints "SCREENS log-locked <path>", still builds
  the mp4, and exits 10. A capture whose model gate never ran is never a success.

  Single instance (tools/godot/launch-lock.ps1): the capture takes the same machine-wide launch lock as
  tools/godot/godot.ps1 before it starts Godot, and holds it until the game is stopped. While another live process
  holds it, this exits 5 at once and launches nothing. The game runs in a kill-on-close job object, so killing this
  script also kills the game. MaxSeconds is clamped to KEYRUN_GODOT_TIMEOUT_SEC (default 300). Captures are the
  orchestrator's step; lanes never run this script.

.PARAMETER OutDir
  Folder for the frames and the mp4. Created if missing. Required.

.PARAMETER IntervalSeconds
  Seconds between captures. Also the duration each frame gets in the mp4.

.PARAMETER MaxSeconds
  Hard cap on capture time before the game process is stopped.

.PARAMETER GodotArgs
  Arguments for Godot, split on whitespace, paths relative to the repo root.
  Default is the run C autorun: pull 1 party, key 2, seed 1, 7000 ticks.

.PARAMETER Name
  Prefix for the frame files and the mp4 name.

.PARAMETER NoVideo
  Capture frames only; do not build the mp4.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/screens.ps1 -OutDir C:/Users/Mattt/Desktop/keyrun-screens/my-lane

.NOTES
  Exit codes: 0 success, 1 no frames captured, 2 Godot binary missing,
  3 ffmpeg missing, 4 ffmpeg failed, 5 another Godot instance holds the launch lock,
  6 the .NET assembly is missing or stale (preflight; nothing was launched),
  7 the --log run file was never created (boot-scene-only run; frames and mp4 kept),
  9 at least one actor rendered the capsule fallback instead of its model (frames and mp4 kept),
  10 model gate not evaluated: the run log stayed locked after the snapshot retries (frames and mp4 kept).
  Frames are kept on every exit path.
#>
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [int]$IntervalSeconds = 4,
    [int]$MaxSeconds = 150,
    [string]$GodotArgs = '--path game --fixed-fps 60 --resolution 1280x720 --position 0,0 -- --autorun --ticks 7000 --key 2 --seed 1 --log build/screens-run.txt',
    [string]$Name = 'run',
    [switch]$NoVideo
)
$ErrorActionPreference = 'Stop'

# Repo root is two levels above tools/godot/ (this script's own location).
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

# Window size the captured game window is pinned to.
$windowW = 1280
$windowH = 720

if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir | Out-Null
}
$outDirFull = (Resolve-Path -LiteralPath $OutDir).Path

# Godot binary lookup, same order as tools/godot/godot.ps1.
$godot = $env:KEYRUN_GODOT
if (-not $godot -and $env:LOCALAPPDATA) {
    $godot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
}
if (-not $godot -or -not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    Write-Output ("FAILED: Godot binary not found at '{0}'. Set KEYRUN_GODOT." -f $godot)
    exit 2
}

# Same .NET and build-daemon environment as tools/godot/godot.ps1.
$dotnetRoot = $env:DOTNET_ROOT
if (-not $dotnetRoot -and $env:USERPROFILE) { $dotnetRoot = Join-Path $env:USERPROFILE '.dotnet' }
if ($dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = $dotnetRoot + ';' + $env:PATH
}
if (-not $env:MSBUILDDISABLENODEREUSE) { $env:MSBUILDDISABLENODEREUSE = '1' }
if (-not $env:UseSharedCompilation) { $env:UseSharedCompilation = 'false' }
if (-not $env:DOTNET_CLI_USE_MSBUILD_SERVER) { $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0' }

# One launch path: the same lock, cap and tree kill as tools/godot/godot.ps1.
. (Join-Path $PSScriptRoot 'launch-lock.ps1')
. (Join-Path $PSScriptRoot 'preflight.ps1')
$capSeconds = Get-KeyrunGodotTimeoutSec
if ($MaxSeconds -gt $capSeconds) {
    Write-Output ("SCREENS MaxSeconds {0} clamped to the launch cap {1}s (KEYRUN_GODOT_TIMEOUT_SEC)" -f $MaxSeconds, $capSeconds)
    $MaxSeconds = $capSeconds
}

$godotArgList = @($GodotArgs -split '\s+' | Where-Object { $_ })

# --resolution WxH in -GodotArgs sets the window size the capture expects; the default above stays 1280x720.
for ($i = 0; $i -lt ($godotArgList.Count - 1); $i++) {
    if ($godotArgList[$i] -eq '--resolution' -and $godotArgList[$i + 1] -match '^(\d+)x(\d+)$') {
        $windowW = [int]$matches[1]
        $windowH = [int]$matches[2]
    }
}

# The HUD run-end check reads the file named by --log. Godot resolves a relative --log
# against the project folder named by --path (default game), not the repo root.
$projRel = 'game'
$logRel = 'build/screens-run.txt'
for ($i = 0; $i -lt ($godotArgList.Count - 1); $i++) {
    if ($godotArgList[$i] -eq '--path') { $projRel = $godotArgList[$i + 1] }
    if ($godotArgList[$i] -eq '--log') { $logRel = $godotArgList[$i + 1] }
}
$logPath = Resolve-KeyrunRunLogPath -RepoRoot $repo -ProjectRel $projRel -LogRel $logRel
$logDir = Split-Path -Parent $logPath
if (-not (Test-Path -LiteralPath $logDir)) {
    New-Item -ItemType Directory -Path $logDir | Out-Null
}
# Remove only the previous log of this same capture so an old run-end line cannot end this one early.
if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinCap
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);

    // Visible top-level windows owned by any of the given process ids, as "handle|title".
    public static List<string> WindowsFor(HashSet<uint> pids)
    {
        var found = new List<string>();
        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pids.Contains(pid) && IsWindowVisible(h))
            {
                var sb = new StringBuilder(512);
                GetWindowText(h, sb, 512);
                found.Add(h.ToString() + "|" + sb.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

function Test-RunEndLogged {
    # The run-end EVENT is written to the --log file by RunLog.cs: EVENT {"...","type":"run_end",...}.
    if (-not (Test-Path -LiteralPath $logPath)) { return $false }
    try {
        return [bool](Select-String -LiteralPath $logPath -Pattern '^EVENT \{.*"type":"run_end"' -Quiet)
    } catch {
        return $false
    }
}

function Get-GamePids {
    param([System.Diagnostics.Process]$Proc)
    $set = New-Object 'System.Collections.Generic.HashSet[uint32]'
    [void]$set.Add([uint32]$Proc.Id)
    foreach ($p in (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'Godot*' -or $_.ProcessName -like 'keyrun*' })) {
        [void]$set.Add([uint32]$p.Id)
    }
    return $set
}

# Captures the game window rectangle to $path. Returns the window names seen and the saved path (null when no game window).
function Save-GameShot {
    param([System.Diagnostics.Process]$Proc, [string]$Path)
    $pids = Get-GamePids -Proc $Proc
    $candidates = [WinCap]::WindowsFor($pids)
    $names = ($candidates | ForEach-Object { $_.Split('|', 2)[1] }) -join ' ; '
    $pick = $null
    foreach ($c in $candidates) {
        $parts = $c.Split('|', 2)
        $title = $parts[1]
        # Prefer the game's own window: a titled window that is not the terminal hosting the Godot console.
        if ($title -and $title -notmatch 'Console' -and $title -match 'Keyrun|Godot') { $pick = $parts[0]; break }
    }
    if (-not $pick) {
        return [pscustomobject]@{ Path = $null; Names = $names }
    }
    $hw = [IntPtr]::new([Int64]$pick)
    [void][WinCap]::ShowWindow($hw, 9)
    [void][WinCap]::SetWindowPos($hw, [IntPtr]::new(-1), 0, 0, $windowW, $windowH, 0x0040)
    [void][WinCap]::SetForegroundWindow($hw)
    Start-Sleep -Milliseconds 300
    $rect = New-Object 'WinCap+RECT'
    if (-not [WinCap]::GetWindowRect($hw, [ref]$rect)) {
        return [pscustomobject]@{ Path = $null; Names = $names }
    }
    $x = $rect.Left; $y = $rect.Top
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) {
        return [pscustomobject]@{ Path = $null; Names = $names }
    }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $g.Dispose()
        $bmp.Dispose()
    }
    return [pscustomobject]@{ Path = $Path; Names = $names; Size = ("{0}x{1}" -f $w, $h) }
}

# Captures the whole primary screen to $Path (the fallback when no game window is found). Returns the saved path and size.
function Save-ScreenShot {
    param([string]$Path)
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.CopyFromScreen($b.X, $b.Y, 0, 0, $b.Size)
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $g.Dispose()
        $bmp.Dispose()
    }
    return [pscustomobject]@{ Path = $Path; Size = ("{0}x{1}" -f $b.Width, $b.Height) }
}

# Preflight before the lock (finding f-3ccab3b2): a capture of a project without a fresh .NET assembly is a boot-scene-only run.
$preflight = Test-KeyrunDotnetRuntime -DotnetRoot $dotnetRoot
if ($preflight.Ok) { $preflight = Test-KeyrunDotnetAssembly -RepoRoot $repo -ProjectRel $projRel }
# Import cache (finding f-30dcbb08; Matt 2026-10-21 "so no capture ever runs without assets"): a fresh worktree has no
# .godot/imported, so the .glb models would render as capsules. Run tools/godot/import.ps1 first (its own lock and cap; the
# capture cap below covers only the game launch) and continue; exit 6 only when the import itself fails.
if ($preflight.Ok) {
    $preflight = Invoke-KeyrunImportIfMissing -RepoRoot $repo -ProjectRel $projRel
    if ($preflight.Ok -and $preflight.Ran) { foreach ($line in $preflight.Lines) { Write-Output $line } }
}
if (-not $preflight.Ok) {
    foreach ($line in $preflight.Lines) { Write-Output $line }
    exit $preflight.Code
}
# Copies the run log to $Dest through a shared read, so a writer that still holds the file does not fail the copy. Returns
# $true when the copy is made (or the log is absent, which Measure-KeyrunActorModels counts as zero actors), $false when the
# log stays locked after 5 attempts 500 ms apart. The model gate reads the copy, never the live log.
function Copy-KeyrunRunLogSnapshot {
    param([string]$Source, [string]$Dest)
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { return $true }
    $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            $in = New-Object System.IO.FileStream -ArgumentList @($Source, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
            try {
                $out = [System.IO.File]::Create($Dest)
                try { $in.CopyTo($out) } finally { $out.Dispose() }
            } finally {
                $in.Dispose()
            }
            return $true
        } catch {
            if ($attempt -lt 5) { Start-Sleep -Milliseconds 500 }
        }
    }
    return $false
}

$launchLock = Enter-KeyrunLaunchLock -Owner ("screens.ps1 -Name {0}" -f $Name)
if (-not $launchLock.Acquired) {
    Write-Output ("FAILED: {0}" -f $launchLock.Message)
    exit 5
}
$job = New-KeyrunProcessJob
$proc = $null
try {
$proc = Start-Process -FilePath $godot -ArgumentList $godotArgList -WorkingDirectory $repo -PassThru
if (-not $job.Attach($proc)) { Write-Output ("WARN pid={0} not placed in a kill-on-close job; the stop still tree-kills it" -f $proc.Id) }
Write-Output ("LAUNCH pid={0} godot={1} log={2}" -f $proc.Id, $godot, $logPath)

$start = Get-Date
$index = 0
$nextShot = 0
$misses = 0
$fullscreenNoted = $false
$runEndAt = -1
$saved = New-Object System.Collections.Generic.List[string]
$reason = 'timeout'
while ($true) {
    $elapsed = [int](((Get-Date) - $start).TotalSeconds)
    if ($elapsed -ge $nextShot) {
        $nextIndex = $index + 1
        # PowerShell variables are case-insensitive: this must not be named $name, which is the -Name parameter.
        $frameFile = '{0}-{1:D3}-t{2:D3}s.png' -f $Name, $nextIndex, $elapsed
        $shot = Save-GameShot -Proc $proc -Path (Join-Path $outDirFull $frameFile)
        if ($shot.Path) {
            $misses = 0
            $index = $nextIndex
            $saved.Add($shot.Path)
            Write-Output ("SHOT {0} t={1}s size={2} windows=[{3}]" -f $frameFile, $elapsed, $shot.Size, $shot.Names)
        } else {
            $misses++
            if ($misses -ge 3) {
                $fullFile = '{0}-{1:D3}-t{2:D3}s-full.png' -f $Name, $nextIndex, $elapsed
                $full = Save-ScreenShot -Path (Join-Path $outDirFull $fullFile)
                $index = $nextIndex
                $saved.Add($full.Path)
                if (-not $fullscreenNoted) { Write-Output 'SCREENS fallback=fullscreen'; $fullscreenNoted = $true }
                Write-Output ("SHOT {0} t={1}s size={2} fallback=fullscreen windows=[{3}]" -f $fullFile, $elapsed, $full.Size, $shot.Names)
            } else {
                Write-Output ("SKIP t={0}s no game window; miss={1} windows=[{2}]" -f $elapsed, $misses, $shot.Names)
            }
        }
        $nextShot += $IntervalSeconds
    }
    if ($runEndAt -lt 0 -and (Test-RunEndLogged)) {
        $runEndAt = $elapsed
        Write-Output ("RUNEND logged t={0}s; one more interval is captured" -f $elapsed)
    }
    if ($runEndAt -ge 0 -and $elapsed -ge ($runEndAt + $IntervalSeconds)) { $reason = 'run_end'; break }
    if ($proc.HasExited) {
        if ($runEndAt -ge 0) { $reason = 'run_end' } else { $reason = 'exit' }
        break
    }
    if ($elapsed -ge $MaxSeconds) { $reason = 'timeout'; break }
    Start-Sleep -Milliseconds 500
}

if (-not $proc.HasExited) {
    Stop-KeyrunProcessTree $proc.Id
    Write-Output ("STOP pid={0} (still alive, stopped with its process tree)" -f $proc.Id)
}
# The game may still hold the run log for a moment after it exits: wait for the process (bounded by a 5 s grace) before the read.
if (-not $proc.WaitForExit(5000)) { Write-Output ("SCREENS pid={0} still running 5 s after the stop; the log read may be locked" -f $proc.Id) }
} finally {
    if ($proc -and -not $proc.HasExited) { Stop-KeyrunProcessTree $proc.Id }
    $job.Terminate()
    $job.Dispose()
    Exit-KeyrunLaunchLock $launchLock
}
Write-Output ("DONE reason='{0}' frames={1} elapsed={2}s folder={3}" -f $reason, $saved.Count, [int](((Get-Date) - $start).TotalSeconds), $outDirFull)
Write-Output ("SCREENS stop={0}" -f $reason)
# Model fallback gate (finding f-30dcbb08): ActorView writes 'ACTOR model=<path|capsule> fallback=<0|1>' per actor to the run
# log. Printed here, enforced (exit 9) after the run-log check below, so a capture with placeholders is never a success.
# A transient lock is retried inside Copy-KeyrunRunLogSnapshot; a persistent one prints one line, keeps the frames, still
# builds the mp4, and fails closed with exit 10 at the end (the gate never ran, so the capture is never a success).
$models = $null
$logLocked = $false
$logSnap = Join-Path ([IO.Path]::GetTempPath()) ('screens-runlog-' + [guid]::NewGuid().ToString('N') + '.txt')
if (Copy-KeyrunRunLogSnapshot -Source $logPath -Dest $logSnap) {
    $models = Measure-KeyrunActorModels -LogPath $logSnap
    Remove-Item -LiteralPath $logSnap -Force -ErrorAction SilentlyContinue
    Write-Output $models.Lines[0]
} else {
    $logLocked = $true
    Write-Output ("SCREENS log-locked {0}; the model gate was not evaluated (frames kept)" -f $logPath)
}

if ($saved.Count -eq 0) {
    Write-Output ("SCREENS frames=0 mp4=none dir={0}" -f $outDirFull)
    exit 1
}

$mp4Out = 'none'
if (-not $NoVideo) {
    $ffmpegFallback = 'C:/Users/Mattt/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.1-full_build/bin/ffmpeg.exe'
    $ffmpeg = $null
    $ffCmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($ffCmd) {
        $ffmpeg = $ffCmd.Source
    } elseif (Test-Path -LiteralPath $ffmpegFallback -PathType Leaf) {
        $ffmpeg = $ffmpegFallback
    }
    if (-not $ffmpeg) {
        Write-Output ("FAILED: ffmpeg not on PATH and not at {0}; frames kept in {1}" -f $ffmpegFallback, $outDirFull)
        Write-Output ("SCREENS frames={0} mp4=none dir={1}" -f $saved.Count, $outDirFull)
        exit 3
    }

    # Concat list: one file line and one duration line per frame; the last frame is repeated without a duration so it is not dropped.
    $listPath = Join-Path ([IO.Path]::GetTempPath()) ('screens-' + [guid]::NewGuid().ToString('N') + '.txt')
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($f in $saved) {
        $lines.Add("file '" + $f.Replace('\', '/') + "'")
        $lines.Add("duration $IntervalSeconds")
    }
    $lines.Add("file '" + $saved[$saved.Count - 1].Replace('\', '/') + "'")
    [IO.File]::WriteAllLines($listPath, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))

    $mp4 = Join-Path $outDirFull ($Name + '.mp4')
    $ffArgs = @('-y', '-f', 'concat', '-safe', '0', '-i', $listPath,
        '-vf', 'scale=trunc(iw/2)*2:trunc(ih/2)*2',
        '-r', '25', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', $mp4)
    $ErrorActionPreference = 'Continue'
    $ffOut = & $ffmpeg @ffArgs 2>&1
    $ffCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Remove-Item -LiteralPath $listPath -Force -ErrorAction SilentlyContinue
    $ffOut | Select-Object -Last 5 | ForEach-Object { Write-Output ("FFMPEG {0}" -f $_) }
    if ($ffCode -ne 0 -or -not (Test-Path -LiteralPath $mp4 -PathType Leaf)) {
        Write-Output ("FAILED: ffmpeg exit {0}; frames kept in {1}" -f $ffCode, $outDirFull)
        Write-Output ("SCREENS frames={0} mp4=none dir={1}" -f $saved.Count, $outDirFull)
        exit 4
    }
    $mp4Out = $mp4
}

Write-Output ("SCREENS frames={0} mp4={1} dir={2}" -f $saved.Count, $mp4Out, $outDirFull)
# End check (finding f-3ccab3b2): the run log is written by the game's C# autorun. Without it the run was boot-scene only.
if (-not (Test-KeyrunRunLogWritten -LogPath $logPath)) {
    Write-Output ("SCREENS no-run-log {0}" -f $logPath)
    exit 7
}
if ($models -and -not $models.Ok) {
    foreach ($line in @($models.Lines | Select-Object -Skip 1)) { Write-Output $line }
    exit $models.Code
}
if ($logLocked) {
    Write-Output ("SCREENS FAIL model-gate-not-evaluated: run log stayed locked after the snapshot retries; frames kept in {0}" -f $outDirFull)
    exit 10
}
exit 0
