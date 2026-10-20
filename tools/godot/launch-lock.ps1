<#
.SYNOPSIS
  Single-instance launch lock and wall-clock-capped runner for every Godot / keyrun launch on this machine.

.DESCRIPTION
  Dot-source this file:  . tools/godot/launch-lock.ps1

  Why: on 2026-10-20 parallel lanes launched the game over and over and the box hard-froze. Every launch path
  (tools/godot/godot.ps1, godot.sh on Windows, tools/godot/screens.ps1, the game/src/**/*SceneTest.ps1 harnesses)
  now takes this one lock, so at most one game runs at a time and a second launcher fails fast instead of piling up.

  The lock is a file (default %LOCALAPPDATA%\keyrun\godot-launch.lock, override KEYRUN_LAUNCH_LOCK_FILE) holding
  JSON {pid, start, token, owner, acquired}. It is created atomically under a named mutex. A lock is stale only when
  its owner process is gone (PID not running, or running with a different start time, i.e. a reused PID); its age
  never matters. A holder exports KEYRUN_LAUNCH_LOCK_TOKEN, and a launch that presents the holder's token while the
  holder is alive is a nested launch of the same sequence (a SceneTest running its launches one after another).

  Functions:
    Enter-KeyrunLaunchLock -Owner <label> [-ExitCodeOnBusy <n>]   take (or join) the lock; returns a handle
    Exit-KeyrunLaunchLock <handle>                                 release a lock this handle took
    Get-KeyrunGodotTimeoutSec                                      the per-launch wall-clock cap (default 300)
    Invoke-KeyrunCapped -FilePath -Arguments -TimeoutSec           run a process, stream its output to the pipeline,
                                                                   kill its whole tree at the cap (exit 124)
    New-KeyrunProcessJob / Stop-KeyrunProcessTree                  tree-kill helpers for windowed launches
#>

$script:KeyrunLockMessage = 'another Godot instance holds the launch lock'
$script:KeyrunDefaultTimeoutSec = 300

if (-not ('KeyrunLaunch.CappedRun' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace KeyrunLaunch
{
    // A Windows job object with KILL_ON_JOB_CLOSE: every process assigned to it (and every child it spawns) dies
    // when Terminate() is called or when the last handle closes, including when the owning PowerShell is killed.
    public sealed class ProcessJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool TerminateJobObject(IntPtr job, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        IntPtr handle;

        public ProcessJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) { return; }
            ExtendedLimits info = new ExtendedLimits();
            info.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf(typeof(ExtendedLimits))))
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
            }
        }

        public bool Attach(Process p)
        {
            if (handle == IntPtr.Zero) { return false; }
            return AssignProcessToJobObject(handle, p.Handle);
        }

        public void Terminate()
        {
            if (handle != IntPtr.Zero) { TerminateJobObject(handle, 124); }
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }

    public static class Proc
    {
        // True when a process with this id is running and (when startTicks is nonzero) started at that UTC tick.
        // A running process whose start time cannot be read counts as alive (fail closed: the lock stays held).
        public static bool IsAlive(int pid, long startTicks)
        {
            Process p;
            try { p = Process.GetProcessById(pid); } catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; }
            using (p)
            {
                try { if (p.HasExited) { return false; } } catch (Exception) { }
                if (startTicks == 0) { return true; }
                try { return p.StartTime.ToUniversalTime().Ticks == startTicks; } catch (Exception) { return true; }
            }
        }

        public static long StartTicks(int pid)
        {
            using (Process p = Process.GetProcessById(pid)) { return p.StartTime.ToUniversalTime().Ticks; }
        }

        // taskkill /T /F: the process and every descendant still parented to it.
        public static void KillTree(int pid)
        {
            try
            {
                ProcessStartInfo si = new ProcessStartInfo("taskkill.exe", "/T /F /PID " + pid);
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.RedirectStandardOutput = true;
                si.RedirectStandardError = true;
                using (Process k = Process.Start(si))
                {
                    k.StandardOutput.ReadToEnd();
                    k.StandardError.ReadToEnd();
                    k.WaitForExit(15000);
                }
            }
            catch (Exception) { }
        }

        // One Windows command line from an argv array (CommandLineToArgvW quoting rules).
        public static string JoinArgs(string[] args)
        {
            StringBuilder all = new StringBuilder();
            foreach (string a in args)
            {
                if (all.Length > 0) { all.Append(' '); }
                if (a.Length > 0 && a.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0) { all.Append(a); continue; }
                all.Append('"');
                int slashes = 0;
                foreach (char c in a)
                {
                    if (c == '\\') { slashes++; continue; }
                    if (c == '"') { all.Append('\\', slashes * 2 + 1); all.Append('"'); slashes = 0; continue; }
                    all.Append('\\', slashes); slashes = 0; all.Append(c);
                }
                all.Append('\\', slashes * 2);
                all.Append('"');
            }
            return all.ToString();
        }
    }

    // A child process whose stdout and stderr lines are queued for the caller, inside a kill-on-close job.
    public sealed class CappedRun : IDisposable
    {
        readonly Process proc;
        readonly ProcessJob job;
        readonly BlockingCollection<string> lines = new BlockingCollection<string>();
        int openStreams = 2;
        public bool Attached;

        CappedRun(Process p, ProcessJob j) { proc = p; job = j; }

        public static CappedRun Start(string file, string[] args, string cwd, Encoding enc)
        {
            ProcessStartInfo si = new ProcessStartInfo(file, Proc.JoinArgs(args));
            si.UseShellExecute = false;
            si.RedirectStandardOutput = true;
            si.RedirectStandardError = true;
            si.StandardOutputEncoding = enc;
            si.StandardErrorEncoding = enc;
            if (!string.IsNullOrEmpty(cwd)) { si.WorkingDirectory = cwd; }
            Process p = new Process();
            p.StartInfo = si;
            ProcessJob j = new ProcessJob();
            CappedRun run = new CappedRun(p, j);
            p.OutputDataReceived += run.OnData;
            p.ErrorDataReceived += run.OnData;
            p.Start();
            run.Attached = j.Attach(p);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            Thread t = new Thread(run.Watch);
            t.IsBackground = true;
            t.Start();
            return run;
        }

        void OnData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                try { lines.Add(e.Data); } catch (InvalidOperationException) { }
                return;
            }
            if (Interlocked.Decrement(ref openStreams) == 0)
            {
                try { lines.CompleteAdding(); } catch (Exception) { }
            }
        }

        // After the main process exits, end any leftover descendants (build daemons holding the pipes open), then
        // give the readers a moment to drain before declaring the output complete.
        void Watch()
        {
            proc.WaitForExit(int.MaxValue);
            job.Terminate();
            DateTime until = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref openStreams) > 0 && DateTime.UtcNow < until) { Thread.Sleep(50); }
            try { lines.CompleteAdding(); } catch (Exception) { }
        }

        public int Pid { get { return proc.Id; } }
        public bool TryTake(int ms, out string line) { return lines.TryTake(out line, ms); }
        public bool Completed { get { return lines.IsCompleted; } }
        public int ExitCode { get { return proc.ExitCode; } }
        public bool HasExited { get { return proc.HasExited; } }

        public void KillTree()
        {
            Proc.KillTree(proc.Id);
            job.Terminate();
        }

        public void Dispose()
        {
            job.Dispose();
            proc.Dispose();
        }
    }
}
'@
}

function Get-KeyrunLaunchLockPath {
    if ($env:KEYRUN_LAUNCH_LOCK_FILE) { return $env:KEYRUN_LAUNCH_LOCK_FILE }
    $base = $env:LOCALAPPDATA
    if (-not $base) { $base = [IO.Path]::GetTempPath() }
    return (Join-Path (Join-Path $base 'keyrun') 'godot-launch.lock')
}

function Get-KeyrunGodotTimeoutSec {
    $raw = $env:KEYRUN_GODOT_TIMEOUT_SEC
    if (-not $raw) { return $script:KeyrunDefaultTimeoutSec }
    $n = 0
    if (-not [int]::TryParse($raw, [ref]$n) -or $n -le 0) {
        throw "KEYRUN_GODOT_TIMEOUT_SEC='$raw' is not a positive whole number of seconds (there is no uncapped launch)"
    }
    return $n
}

function Read-KeyrunLockFile([string]$path) {
    try {
        $text = [IO.File]::ReadAllText($path)
        $obj = $text | ConvertFrom-Json
        if ($null -eq $obj.pid) { return $null }
        return $obj
    } catch {
        return $null
    }
}

function Format-KeyrunLockHolder($holder) {
    if (-not $holder) { return 'unknown holder' }
    return ("pid {0}, '{1}', since {2}" -f $holder.pid, $holder.owner, $holder.acquired)
}

# Take the machine-wide launch lock, or join it as a nested launch of the live holder whose token this process
# carries in KEYRUN_LAUNCH_LOCK_TOKEN. Returns @{ Acquired; Nested; Path; Token; Message; PrevToken }.
# Busy: -ExitCodeOnBusy n prints the reason to stderr and exits the calling script with n; otherwise Acquired=$false.
function Enter-KeyrunLaunchLock {
    param([Parameter(Mandatory = $true)][string]$Owner, [int]$ExitCodeOnBusy = -1)
    $path = Get-KeyrunLaunchLockPath
    $dir = Split-Path -Parent $path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $sha = [Security.Cryptography.SHA1]::Create()
    $key = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($path.ToLowerInvariant()))).Replace('-', '').Substring(0, 16)
    $mutex = $null
    try { $mutex = New-Object Threading.Mutex($false, "Global\KeyrunGodotLaunchLock-$key") } catch { $mutex = New-Object Threading.Mutex($false, "Local\KeyrunGodotLaunchLock-$key") }

    $result = [pscustomobject]@{ Acquired = $false; Nested = $false; Path = $path; Token = $null; Message = $null; PrevToken = $env:KEYRUN_LAUNCH_LOCK_TOKEN }
    $owned = $false
    try {
        try { $owned = $mutex.WaitOne(15000) } catch [Threading.AbandonedMutexException] { $owned = $true }
        if (-not $owned) { throw "launch-lock guard mutex for '$path' not obtained within 15s" }

        if (Test-Path -LiteralPath $path) {
            $holder = Read-KeyrunLockFile $path
            $alive = $false
            if ($holder) {
                $ticks = 0L
                [void][long]::TryParse([string]$holder.start, [ref]$ticks)
                $alive = [KeyrunLaunch.Proc]::IsAlive([int]$holder.pid, $ticks)
            }
            if ($alive) {
                if ($env:KEYRUN_LAUNCH_LOCK_TOKEN -and $holder.token -eq $env:KEYRUN_LAUNCH_LOCK_TOKEN) {
                    $result.Acquired = $true
                    $result.Nested = $true
                    $result.Token = $holder.token
                    return $result
                }
                $result.Message = ("{0} ({1}; lock file {2}). Launches are serial: lanes never launch the game, the orchestrator runs launches and captures one at a time." -f $script:KeyrunLockMessage, (Format-KeyrunLockHolder $holder), $path)
            } else {
                # Owner process is gone (or the file is unreadable): stale, recover it.
                Remove-Item -LiteralPath $path -Force
            }
        }

        if (-not $result.Message) {
            $token = [guid]::NewGuid().ToString('N')
            $json = '{"pid":' + $PID + ',"start":"' + [KeyrunLaunch.Proc]::StartTicks($PID) + '","token":"' + $token +
                '","owner":' + (ConvertTo-Json ([string]$Owner) -Compress) + ',"acquired":"' + [DateTime]::UtcNow.ToString('o') + '"}'
            $fs = New-Object IO.FileStream($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes($json)
                $fs.Write($bytes, 0, $bytes.Length)
            } finally {
                $fs.Dispose()
            }
            $env:KEYRUN_LAUNCH_LOCK_TOKEN = $token
            $result.Acquired = $true
            $result.Token = $token
        }
    } finally {
        if ($owned) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }

    if (-not $result.Acquired -and $ExitCodeOnBusy -ge 0) {
        [Console]::Error.WriteLine("${Owner}: $($result.Message)")
        exit $ExitCodeOnBusy
    }
    return $result
}

# Release a lock taken by Enter-KeyrunLaunchLock. A nested handle releases nothing. The file is removed only while
# it still carries this handle's token.
function Exit-KeyrunLaunchLock($Handle) {
    if (-not $Handle -or -not $Handle.Acquired -or $Handle.Nested) { return }
    $holder = Read-KeyrunLockFile $Handle.Path
    if ($holder -and $holder.token -eq $Handle.Token) {
        Remove-Item -LiteralPath $Handle.Path -Force -ErrorAction SilentlyContinue
    }
    if ($env:KEYRUN_LAUNCH_LOCK_TOKEN -eq $Handle.Token) {
        if ($Handle.PrevToken) { $env:KEYRUN_LAUNCH_LOCK_TOKEN = $Handle.PrevToken } else { Remove-Item Env:KEYRUN_LAUNCH_LOCK_TOKEN -ErrorAction SilentlyContinue }
    }
}

# Run FilePath with Arguments; each stdout/stderr line goes to the pipeline as it arrives (the pipeline carries only
# the child's output). At TimeoutSec the whole process tree is killed and a message goes to stderr. The exit code
# (124 after a timeout kill) is left in $script:KeyrunLastExitCode.
function Invoke-KeyrunCapped {
    param([Parameter(Mandatory = $true)][string]$FilePath, [string[]]$Arguments = @(), [Parameter(Mandatory = $true)][int]$TimeoutSec, [string]$Label = 'godot.ps1')
    $cwd = $PWD.ProviderPath
    $run = [KeyrunLaunch.CappedRun]::Start($FilePath, [string[]]@($Arguments), $cwd, [Console]::OutputEncoding)
    $script:KeyrunLastExitCode = $null
    try {
        if (-not $run.Attached) {
            [Console]::Error.WriteLine("${Label}: could not place pid $($run.Pid) in a kill-on-close job; the timeout still tree-kills it")
        }
        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        $line = $null
        $timedOut = $false
        while (-not $run.Completed) {
            if ($run.TryTake(200, [ref]$line)) { Write-Output $line }
            if (-not $timedOut -and -not $run.HasExited -and (Get-Date) -ge $deadline) {
                $timedOut = $true
                $run.KillTree()
            }
        }
        if ($timedOut) {
            [Console]::Error.WriteLine("${Label}: wall-clock cap of ${TimeoutSec}s reached; killed the process tree of pid $($run.Pid). Set KEYRUN_GODOT_TIMEOUT_SEC to change the cap.")
            $script:KeyrunLastExitCode = 124
        } else {
            $script:KeyrunLastExitCode = $run.ExitCode
        }
    } finally {
        $run.Dispose()
    }
}

function New-KeyrunProcessJob { return (New-Object KeyrunLaunch.ProcessJob) }

function Stop-KeyrunProcessTree([int]$Id) { [KeyrunLaunch.Proc]::KillTree($Id) }
