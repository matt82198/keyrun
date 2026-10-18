<#
.SYNOPSIS
  Record a Keyrun run as PNG frames of the game window plus an mp4 (rule D15).

.DESCRIPTION
  Launches the Godot 4.7.2 .NET console binary WINDOWED (no --headless), finds the
  game window by enumerating the visible top-level windows owned by the Godot
  processes, and captures that window's rectangle every IntervalSeconds to
  <OutDir>\<Name>-NNN-tSSSs.png. A capture is skipped (logged as SKIP) when no
  game window is visible; the script never falls back to a whole-screen capture.
  Stops when the game process exits, when the HUD run-end line appears in the
  --log file, or when MaxSeconds passes. A process still alive at the end is
  stopped by PID. Then builds <OutDir>\<Name>.mp4 from this run's frames with
  ffmpeg, unless -NoVideo is given.

  The Godot binary is resolved the same way as tools/godot/godot.ps1: the
  KEYRUN_GODOT environment variable, else the winget install under %LOCALAPPDATA%.
  Exit 2 when it cannot be found.

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
  3 ffmpeg missing, 4 ffmpeg failed. Frames are kept on every exit path.
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

$godotArgList = @($GodotArgs -split '\s+' | Where-Object { $_ })

# The HUD run-end check reads the file named by --log. Godot resolves a relative --log
# against the project folder named by --path (default game), not the repo root.
$projRel = 'game'
$logRel = 'build/screens-run.txt'
for ($i = 0; $i -lt ($godotArgList.Count - 1); $i++) {
    if ($godotArgList[$i] -eq '--path') { $projRel = $godotArgList[$i + 1] }
    if ($godotArgList[$i] -eq '--log') { $logRel = $godotArgList[$i + 1] }
}
if ([IO.Path]::IsPathRooted($logRel)) {
    $logPath = $logRel
} else {
    $logPath = Join-Path (Join-Path $repo $projRel) $logRel
}
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
    # HUD lines go to the console, not to --log; this only catches a "HUD runend" line if one is written to the log.
    if (-not (Test-Path -LiteralPath $logPath)) { return $false }
    try {
        return [bool](Select-String -LiteralPath $logPath -Pattern '^HUD runend' -Quiet)
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

$proc = Start-Process -FilePath $godot -ArgumentList $godotArgList -WorkingDirectory $repo -PassThru
Write-Output ("LAUNCH pid={0} godot={1} log={2}" -f $proc.Id, $godot, $logPath)

$start = Get-Date
$index = 0
$nextShot = 0
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
            $index = $nextIndex
            $saved.Add($shot.Path)
            Write-Output ("SHOT {0} t={1}s size={2} windows=[{3}]" -f $frameFile, $elapsed, $shot.Size, $shot.Names)
        } else {
            Write-Output ("SKIP t={0}s no game window; windows=[{1}]" -f $elapsed, $shot.Names)
        }
        $nextShot += $IntervalSeconds
    }
    if ($proc.HasExited) { $reason = 'game exited'; break }
    if (Test-RunEndLogged) { $reason = 'HUD runend logged'; break }
    if ($elapsed -ge $MaxSeconds) { $reason = 'timeout'; break }
    Start-Sleep -Milliseconds 500
}

if (-not $proc.HasExited) {
    Stop-Process -Id $proc.Id -Force
    Write-Output ("STOP pid={0} (still alive, stopped by PID)" -f $proc.Id)
}
Write-Output ("DONE reason='{0}' frames={1} elapsed={2}s folder={3}" -f $reason, $saved.Count, [int](((Get-Date) - $start).TotalSeconds), $outDirFull)

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
exit 0
