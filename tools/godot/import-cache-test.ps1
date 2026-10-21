<#
.SYNOPSIS
  Tests for the import-cache preflight (Test-KeyrunImportCache in tools/godot/preflight.ps1, finding f-30dcbb08).

.DESCRIPTION
  Never launches Godot and never runs an import. Every case builds a fixture project tree under the temp folder and calls
  the real function on it. A fresh worktree has no game/.godot/imported cache, so every .glb under game/assets stays
  unimported and the models render as capsules; the preflight must refuse that with exit 6 instead.

  Cases:
    I1  one game/assets/x.glb and no .godot folder: Code 6, the single line
        'PREFLIGHT missing-import-cache 1 models unimported; run tools/godot/import.ps1'
    I2  the same model with game/.godot/imported/x.glb-<hash>.scn newer than the .glb: Ok, Code 0, no lines
    I3  the cache file older than its .glb (stale): Code 6, the same line with 1 model
    I4  two models, a cache for only one of them: Code 6 with '... 1 models unimported ...'
    I5  a model in a sub folder (game/assets/sub/z.gltf.glb) with a fresh cache: Ok (the scan is recursive)
    I6  no .glb under game/assets at all (no assets folder): Ok, nothing to import

  Import-if-missing (Invoke-KeyrunImportIfMissing; screens.ps1 and setup-worktree.ps1 call it). The import script is a
  stub written into the fixture at tools/godot/import.ps1, so no Godot runs:
    J1  cache fresh: Ran false, Ok, the stub is never invoked (its marker file is absent)
    J2  cache missing, the stub fills it and exits 0: Ran true, Ok, Code 0
    J3  cache missing, the stub exits 8: Code 6, a line 'PREFLIGHT import failed exit=8'
    J4  cache missing, the stub exits 0 without filling it: Code 6, a line starting 'PREFLIGHT import-cache still missing'

  Model fallback gate (Measure-KeyrunActorModels; screens.ps1 exit 9). Run-log fixtures with ActorView's
  'ACTOR model=<res path|capsule> fallback=<0|1> id=<n>' lines:
    M1  three models and one capsule: Actors 4, Fallback 1, Code 9, the lines
        'SCREENS models actors=4 fallback=1' and 'SCREENS FAIL model-fallback 1 actors rendered as capsules'
    M2  three models: Ok, Code 0, the single line 'SCREENS models actors=3 fallback=0'
    M3  only the older 'ACTOR model id=' and 'ACTOR fallback capsule' console lines: neither is counted, actors=0
    M4  no log file: Ok, actors=0 fallback=0

  Launch-script project path (run from a foreign cwd, $env:TEMP; the scripts are copied into a fixture repo; a compiled
  stand-in .exe records its argv and exits 0, so no Godot runs). Finding: a relative "game" reaches Godot unresolved.
    S1  setup-worktree.ps1 -ImportScript stub: the stub receives an ABSOLUTE project folder equal to <fixture>\game; exit 0
    S2  import.ps1: the Godot argv is '--headless --path <absolute>\game --import'; exit 0
    S3  godot.ps1 --headless --path game --smoke: the Godot argv carries the ABSOLUTE project folder; exit 0

  Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/import-cache-test.ps1
  Exit 0 = PASS, 1 = FAIL.
#>
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'preflight.ps1')

$work = Join-Path ([IO.Path]::GetTempPath()) ('keyrun-import-cache-test-' + [guid]::NewGuid().ToString('N'))
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

function Set-Mtime([string]$path, [DateTime]$mtime) {
    (Get-Item -LiteralPath $path).LastWriteTimeUtc = $mtime
}

# A fixture repo root with game/ as the Godot project (project.godot only; the .NET check is not under test here).
function New-Fixture([string]$tag) {
    $root = Join-Path $work $tag
    $proj = Join-Path $root 'game'
    New-Item -ItemType Directory -Path $proj -Force | Out-Null
    Write-Text (Join-Path $proj 'project.godot') @('config_version=5')
    return $root
}

function Add-Model([string]$root, [string]$rel, [DateTime]$mtime) {
    $f = Join-Path $root ('game\' + $rel)
    Write-Text $f @('glTF')
    Set-Mtime $f $mtime
}

function Add-Cache([string]$root, [string]$name, [DateTime]$mtime) {
    $f = Join-Path $root ('game\.godot\imported\' + $name)
    Write-Text $f @('SCN')
    Set-Mtime $f $mtime
}

$past = [DateTime]::Parse('2020-06-01T00:00:00Z').ToUniversalTime()
$newer = [DateTime]::Parse('2030-01-01T00:00:00Z').ToUniversalTime()
$line1 = 'PREFLIGHT missing-import-cache 1 models unimported; run tools/godot/import.ps1'

try {
    # I1: a model and no cache at all.
    $root = New-Fixture 'i1'
    Add-Model $root 'assets\x.glb' $past
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I1.missing-cache-code-6' ((-not $r.Ok) -and $r.Code -eq 6) "Ok=$($r.Ok) Code=$($r.Code)"
    Check 'I1.missing-cache-exact-line' ($r.Lines.Count -eq 1 -and $r.Lines[0] -eq $line1) "lines=[$($r.Lines -join ' | ')]"

    # I2: the same model with a fresh cache file.
    $root = New-Fixture 'i2'
    Add-Model $root 'assets\x.glb' $past
    Add-Cache $root 'x.glb-0123456789abcdef0123456789abcdef.scn' $newer
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I2.fresh-cache-ok' ($r.Ok -and $r.Code -eq 0 -and $r.Lines.Count -eq 0) "Ok=$($r.Ok) Code=$($r.Code) lines=[$($r.Lines -join ' | ')]"

    # I3: a cache file older than its model is stale and counts as unimported.
    $root = New-Fixture 'i3'
    Add-Model $root 'assets\x.glb' $newer
    Add-Cache $root 'x.glb-0123456789abcdef0123456789abcdef.scn' $past
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I3.stale-cache-code-6' ((-not $r.Ok) -and $r.Code -eq 6) "Ok=$($r.Ok) Code=$($r.Code)"
    Check 'I3.stale-cache-exact-line' ($r.Lines.Count -eq 1 -and $r.Lines[0] -eq $line1) "lines=[$($r.Lines -join ' | ')]"

    # I4: two models, a cache for only one of them.
    $root = New-Fixture 'i4'
    Add-Model $root 'assets\a.glb' $past
    Add-Model $root 'assets\b.glb' $past
    Add-Cache $root 'a.glb-0123456789abcdef0123456789abcdef.scn' $newer
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I4.partial-cache-counts-missing' ((-not $r.Ok) -and $r.Code -eq 6 -and $r.Lines.Count -eq 1 -and $r.Lines[0] -eq $line1) "Ok=$($r.Ok) Code=$($r.Code) lines=[$($r.Lines -join ' | ')]"

    # I5: a sub-folder model with a double extension and a fresh cache is found (recursive scan).
    $root = New-Fixture 'i5'
    Add-Model $root 'assets\sub\z.gltf.glb' $past
    Add-Cache $root 'z.gltf.glb-0123456789abcdef0123456789abcdef.scn' $newer
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I5.subfolder-double-extension-ok' ($r.Ok -and $r.Code -eq 0) "Ok=$($r.Ok) Code=$($r.Code) lines=[$($r.Lines -join ' | ')]"

    # I6: no models at all.
    $root = New-Fixture 'i6'
    $r = Test-KeyrunImportCache -RepoRoot $root -ProjectRel 'game'
    Check 'I6.no-models-ok' ($r.Ok -and $r.Code -eq 0) "Ok=$($r.Ok) Code=$($r.Code) lines=[$($r.Lines -join ' | ')]"

    # J1..J4: Invoke-KeyrunImportIfMissing with a stub import script in the fixture's tools/godot/.
    # The stub resolves its repo root from its own location (as the real import.ps1 does), writes a marker, then
    # fills the cache when FILL is set and exits with CODE.
    function New-ImportStub([string]$root, [bool]$fill, [int]$code) {
        $fillLine = '$false'
        if ($fill) { $fillLine = '$true' }
        Write-Text (Join-Path $root 'tools\godot\import.ps1') @(
            'param([string]$ProjectRel = ''game'')',
            '$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot ''..\..'')).Path',
            '[IO.File]::WriteAllText((Join-Path $repo ''import-ran.txt''), ''ran'')',
            ('if ({0}) {{' -f $fillLine),
            '    $dir = Join-Path $repo ($ProjectRel + ''\.godot\imported'')',
            '    New-Item -ItemType Directory -Path $dir -Force | Out-Null',
            '    [IO.File]::WriteAllText((Join-Path $dir ''x.glb-0123456789abcdef0123456789abcdef.scn''), ''SCN'')',
            '}',
            'Write-Output ''IMPORT stub''',
            ('exit {0}' -f $code))
    }

    $root = New-Fixture 'j1'
    Add-Model $root 'assets\x.glb' $past
    Add-Cache $root 'x.glb-0123456789abcdef0123456789abcdef.scn' $newer
    New-ImportStub $root $true 0
    $r = Invoke-KeyrunImportIfMissing -RepoRoot $root -ProjectRel 'game'
    Check 'J1.cached-not-run' ($r.Ok -and $r.Code -eq 0 -and (-not $r.Ran) -and -not (Test-Path -LiteralPath (Join-Path $root 'import-ran.txt'))) "Ok=$($r.Ok) Code=$($r.Code) Ran=$($r.Ran) lines=[$($r.Lines -join ' | ')]"

    $root = New-Fixture 'j2'
    Add-Model $root 'assets\x.glb' $past
    New-ImportStub $root $true 0
    $r = Invoke-KeyrunImportIfMissing -RepoRoot $root -ProjectRel 'game'
    Check 'J2.missing-imports-then-ok' ($r.Ok -and $r.Code -eq 0 -and $r.Ran -and (Test-Path -LiteralPath (Join-Path $root 'import-ran.txt'))) "Ok=$($r.Ok) Code=$($r.Code) Ran=$($r.Ran) lines=[$($r.Lines -join ' | ')]"

    $root = New-Fixture 'j3'
    Add-Model $root 'assets\x.glb' $past
    New-ImportStub $root $false 8
    $r = Invoke-KeyrunImportIfMissing -RepoRoot $root -ProjectRel 'game'
    Check 'J3.import-fails-code-6' ((-not $r.Ok) -and $r.Code -eq 6 -and $r.Ran -and (@($r.Lines) -contains 'PREFLIGHT import failed exit=8')) "Ok=$($r.Ok) Code=$($r.Code) Ran=$($r.Ran) lines=[$($r.Lines -join ' | ')]"

    $root = New-Fixture 'j4'
    Add-Model $root 'assets\x.glb' $past
    New-ImportStub $root $false 0
    $r = Invoke-KeyrunImportIfMissing -RepoRoot $root -ProjectRel 'game'
    Check 'J4.import-leaves-cache-missing-code-6' ((-not $r.Ok) -and $r.Code -eq 6 -and @($r.Lines | Where-Object { $_ -like 'PREFLIGHT import-cache still missing*' }).Count -eq 1) "Ok=$($r.Ok) Code=$($r.Code) Ran=$($r.Ran) lines=[$($r.Lines -join ' | ')]"

    # M1..M4: the model fallback count over a run log.
    $logDir = Join-Path $work 'logs'
    $log = Join-Path $logDir 'm1.txt'
    Write-Text $log @(
        'TICK 0 warden=(0,0) yaw=0 hp=100 res=0 events=0',
        'ACTOR model=res://assets/kaykit-adventurers/Knight.glb fallback=0 id=1',
        'ACTOR model=res://assets/kaykit-adventurers/Mage.glb fallback=0 id=2',
        'ACTOR model=capsule fallback=1 id=7',
        'ACTOR model=res://assets/kaykit-skeletons/Skeleton_Minion.glb fallback=0 id=8')
    $r = Measure-KeyrunActorModels -LogPath $log
    Check 'M1.one-capsule-code-9' ((-not $r.Ok) -and $r.Code -eq 9 -and $r.Actors -eq 4 -and $r.Fallback -eq 1) "Ok=$($r.Ok) Code=$($r.Code) Actors=$($r.Actors) Fallback=$($r.Fallback)"
    Check 'M1.exact-lines' ($r.Lines.Count -eq 2 -and $r.Lines[0] -eq 'SCREENS models actors=4 fallback=1' -and $r.Lines[1] -eq 'SCREENS FAIL model-fallback 1 actors rendered as capsules') "lines=[$($r.Lines -join ' | ')]"

    $log = Join-Path $logDir 'm2.txt'
    Write-Text $log @(
        'ACTOR model=res://assets/kaykit-adventurers/Knight.glb fallback=0 id=1',
        'ACTOR model=res://assets/kaykit-adventurers/Mage.glb fallback=0 id=2',
        'ACTOR model=res://assets/kaykit-adventurers/Rogue_Hooded.glb fallback=0 id=3')
    $r = Measure-KeyrunActorModels -LogPath $log
    Check 'M2.all-models-ok' ($r.Ok -and $r.Code -eq 0 -and $r.Actors -eq 3 -and $r.Fallback -eq 0 -and $r.Lines.Count -eq 1 -and $r.Lines[0] -eq 'SCREENS models actors=3 fallback=0') "Ok=$($r.Ok) Code=$($r.Code) lines=[$($r.Lines -join ' | ')]"

    $log = Join-Path $logDir 'm3.txt'
    Write-Text $log @(
        'ACTOR model id=1 class=warden file=Knight.glb scale=1.000 clips=static',
        'ACTOR fallback capsule id=7 class=choir.melee')
    $r = Measure-KeyrunActorModels -LogPath $log
    Check 'M3.older-lines-not-counted' ($r.Ok -and $r.Actors -eq 0 -and $r.Fallback -eq 0) "Ok=$($r.Ok) Actors=$($r.Actors) Fallback=$($r.Fallback)"

    $r = Measure-KeyrunActorModels -LogPath (Join-Path $logDir 'absent.txt')
    Check 'M4.no-log-zero' ($r.Ok -and $r.Code -eq 0 -and $r.Actors -eq 0 -and $r.Fallback -eq 0) "Ok=$($r.Ok) Code=$($r.Code) Actors=$($r.Actors)"

    # S0..S3: the launch scripts hand Godot (and the import script) an ABSOLUTE project folder, so a foreign cwd cannot
    # make "game" resolve against the caller's folder (finding: tools/godot/setup-worktree.ps1 and import.ps1 pass a
    # relative project path). Each case copies the real scripts into a fixture repo, runs them from $env:TEMP in a child
    # Windows PowerShell, and records what reached the stand-in executable or the stub. No Godot is launched.
    $fakeExe = Join-Path $work 'fake-godot.exe'
    $fakeSrc = 'public static class FakeExe { public static int Main(string[] args) { string log = System.Environment.GetEnvironmentVariable("FAKE_ARGV_LOG"); if (!string.IsNullOrEmpty(log)) { System.IO.File.WriteAllLines(log, args); } return 0; } }'
    Add-Type -TypeDefinition $fakeSrc -OutputAssembly $fakeExe -OutputType ConsoleApplication
    # DOTNET_ROOT stand-in: a folder whose dotnet.exe is the same fake (preflight requires it to be a file).
    $fakeDotnet = Join-Path $work 'dotnet-root'
    New-Item -ItemType Directory -Path $fakeDotnet -Force | Out-Null
    Copy-Item -LiteralPath $fakeExe -Destination (Join-Path $fakeDotnet 'dotnet.exe')

    function New-ToolFixture([string]$tag, [string[]]$scripts) {
        $root = Join-Path $work $tag
        $dir = Join-Path $root 'tools\godot'
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        foreach ($s in $scripts) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $s) -Destination (Join-Path $dir $s) }
        return $root
    }

    function Invoke-FromForeignCwd([string]$scriptPath, [string[]]$scriptArgs) {
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        Push-Location $env:TEMP
        try {
            $o = & powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath @scriptArgs 2>&1
            return [pscustomobject]@{ Code = $LASTEXITCODE; Out = @($o) }
        } finally {
            Pop-Location
            $ErrorActionPreference = $prevEap
        }
    }

    function Read-Argv([string]$path) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
        return @([IO.File]::ReadAllLines($path))
    }

    # S1: setup-worktree.ps1 from a foreign cwd hands the import script an ABSOLUTE project folder under the fixture repo.
    $s1 = New-ToolFixture 's1' @('setup-worktree.ps1', 'preflight.ps1')
    Write-Text (Join-Path $s1 'game\Keyrun.csproj') @('<Project Sdk="Microsoft.NET.Sdk" />')
    Write-Text (Join-Path $s1 'game\project.godot') @('config_version=5')
    Add-Model $s1 'assets\x.glb' $past
    # Stub import script: records the ProjectRel it was handed; fills the cache only when that value is absolute.
    $s1Stub = Join-Path $work 's1-stub.ps1'
    Write-Text $s1Stub @(
        'param([string]$ProjectRel = ''game'')',
        '[IO.File]::WriteAllText((Join-Path $PSScriptRoot ''s1-received.txt''), $ProjectRel)',
        'if ([IO.Path]::IsPathRooted($ProjectRel)) {',
        '    $dir = Join-Path $ProjectRel ''.godot\imported''',
        '    New-Item -ItemType Directory -Path $dir -Force | Out-Null',
        '    [IO.File]::WriteAllText((Join-Path $dir ''x.glb-0123456789abcdef0123456789abcdef.scn''), ''SCN'')',
        '}',
        'Write-Output ''IMPORT stub''',
        'exit 0')
    $prevDotnetRoot = $env:DOTNET_ROOT; $prevArgv = $env:FAKE_ARGV_LOG
    $env:DOTNET_ROOT = $fakeDotnet; $env:FAKE_ARGV_LOG = Join-Path $work 's1-argv.txt'
    try {
        $r1 = Invoke-FromForeignCwd (Join-Path $s1 'tools\godot\setup-worktree.ps1') @('-ImportScript', $s1Stub)
    } finally {
        $env:DOTNET_ROOT = $prevDotnetRoot; $env:FAKE_ARGV_LOG = $prevArgv
    }
    $recv1 = ''
    $recv1Path = Join-Path $work 's1-received.txt'
    if (Test-Path -LiteralPath $recv1Path -PathType Leaf) { $recv1 = [IO.File]::ReadAllText($recv1Path) }
    $want1 = Join-Path $s1 'game'
    Check 'S1.setup-hands-import-absolute-project' ([IO.Path]::IsPathRooted($recv1) -and ($recv1 -ieq $want1)) "received=[$recv1] want=[$want1]"
    Check 'S1.setup-exit-0' ($r1.Code -eq 0) "code=$($r1.Code) out=[$($r1.Out -join ' | ')]"

    # S2: import.ps1 from a foreign cwd passes Godot --path as an ABSOLUTE folder (no models: the post-check passes).
    $s2 = New-ToolFixture 's2' @('import.ps1', 'launch-lock.ps1', 'preflight.ps1')
    Write-Text (Join-Path $s2 'game\project.godot') @('config_version=5')
    $s2Argv = Join-Path $work 's2-argv.txt'
    $prevGodot = $env:KEYRUN_GODOT; $prevLock = $env:KEYRUN_LAUNCH_LOCK_FILE; $prevArgv = $env:FAKE_ARGV_LOG
    $env:KEYRUN_GODOT = $fakeExe; $env:KEYRUN_LAUNCH_LOCK_FILE = Join-Path $work 's2.lock'; $env:FAKE_ARGV_LOG = $s2Argv
    try {
        $r2 = Invoke-FromForeignCwd (Join-Path $s2 'tools\godot\import.ps1') @()
    } finally {
        $env:KEYRUN_GODOT = $prevGodot; $env:KEYRUN_LAUNCH_LOCK_FILE = $prevLock; $env:FAKE_ARGV_LOG = $prevArgv
    }
    $a2 = Read-Argv $s2Argv
    Check 'S2.import-godot-gets-absolute-project' ($a2.Count -eq 4 -and [IO.Path]::IsPathRooted($a2[2]) -and ($a2[2] -ieq (Join-Path $s2 'game'))) "argv=[$($a2 -join ' ')]"
    Check 'S2.import-exit-0' ($r2.Code -eq 0) "code=$($r2.Code) out=[$($r2.Out -join ' | ')]"

    # S3: godot.ps1 from a foreign cwd passes Godot --path as an ABSOLUTE folder; its preflight still names the relative form.
    $s3 = New-ToolFixture 's3' @('godot.ps1', 'launch-lock.ps1', 'preflight.ps1')
    Write-Text (Join-Path $s3 'game\project.godot') @('config_version=5', 'project/assembly_name="Keyrun"')
    Write-Text (Join-Path $s3 'game\.godot\mono\temp\bin\Debug\Keyrun.dll') @('stand-in')
    $s3Argv = Join-Path $work 's3-argv.txt'
    $prevDotnetRoot = $env:DOTNET_ROOT; $prevGodot = $env:KEYRUN_GODOT; $prevLock = $env:KEYRUN_LAUNCH_LOCK_FILE; $prevArgv = $env:FAKE_ARGV_LOG
    $env:DOTNET_ROOT = $fakeDotnet; $env:KEYRUN_GODOT = $fakeExe; $env:KEYRUN_LAUNCH_LOCK_FILE = Join-Path $work 's3.lock'; $env:FAKE_ARGV_LOG = $s3Argv
    try {
        $r3 = Invoke-FromForeignCwd (Join-Path $s3 'tools\godot\godot.ps1') @('--headless', '--path', 'game', '--smoke')
    } finally {
        $env:DOTNET_ROOT = $prevDotnetRoot; $env:KEYRUN_GODOT = $prevGodot; $env:KEYRUN_LAUNCH_LOCK_FILE = $prevLock; $env:FAKE_ARGV_LOG = $prevArgv
    }
    $a3 = Read-Argv $s3Argv
    Check 'S3.godot-gets-absolute-project' ($a3.Count -eq 4 -and [IO.Path]::IsPathRooted($a3[2]) -and ($a3[2] -ieq (Join-Path $s3 'game'))) "argv=[$($a3 -join ' ')]"
    Check 'S3.godot-exit-0' ($r3.Code -eq 0) "code=$($r3.Code) out=[$($r3.Out -join ' | ')]"
} catch {
    Check 'import-cache.exception' $false $_.Exception.Message
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Output ("import-cache-test: FAIL ({0})" -f $failures.Count)
    exit 1
}
Write-Output 'import-cache-test: PASS'
exit 0
