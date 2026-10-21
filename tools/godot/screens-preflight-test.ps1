<#
.SYNOPSIS
  Tests for the .NET assembly preflight (tools/godot/preflight.ps1) and the run-log check that screens.ps1 ends with.

.DESCRIPTION
  Never launches Godot and never runs a build. Every case builds a fixture project tree under the temp folder and calls
  the real functions on it: Test-KeyrunDotnetAssembly (missing, stale, fresh, naming, scan scope) and
  Test-KeyrunRunLogWritten (the --log file exists or not). The fixture tree has no engine binary, so nothing can start.

  Cases:
    P1  no .godot folder: Code 6, first line 'PREFLIGHT missing-assembly' names game/.godot/mono/temp/bin/Debug/Keyrun.dll
    P2  assembly older than a .cs under game/src: Code 6, 'PREFLIGHT stale-assembly' with newer=game/src/Boot.cs
    P3  assembly newer than every .cs: Ok, Code 0, no lines
    P4  assembly with the same mtime as the newest .cs is not older: Ok
    P5  a newer .cs under game/src/obj is ignored (generated output); the same .cs under game/src is not ignored
    P6  the assembly name comes from project.godot: with assembly_name="Other", Keyrun.dll does not count
    P6b with Other.dll present and fresh: Ok
    P7  a newer .cs outside src, sim, bots and content (game/tests) does not make the assembly stale
    P8  no project.godot and no .csproj AssemblyName: Code 6, 'PREFLIGHT unknown-assembly'
    P9  every failure carries a build-command line naming game/Keyrun.csproj and -c Debug
    P10 an absolute ProjectRel (--path C:\...\game) is the project folder itself: fresh -> Ok; missing -> names that path
    D0  Test-KeyrunDotnetRuntime is defined
    D1  a .NET root without dotnet.exe: Code 6, 'PREFLIGHT missing-dotnet <root>/dotnet.exe'
    D2  a .NET root with dotnet.exe: Ok
    D3  no root and no dotnet on PATH (PATH narrowed to an empty folder for the case): Code 6
    R1  Test-KeyrunRunLogWritten: no log file -> false
    R2  Test-KeyrunRunLogWritten: log file present -> true

  Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/screens-preflight-test.ps1
  Exit 0 = PASS, 1 = FAIL.
#>
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'preflight.ps1')

$work = Join-Path ([IO.Path]::GetTempPath()) ('keyrun-preflight-test-' + [guid]::NewGuid().ToString('N'))
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

# A fixture repo root with game/ as the Godot project: project.godot, Keyrun.csproj and one .cs per scanned folder.
function New-Fixture([string]$tag, [switch]$NoProject) {
    $root = Join-Path $work $tag
    $proj = Join-Path $root 'game'
    foreach ($d in @('src', 'sim', 'bots', 'content', 'tests')) { New-Item -ItemType Directory -Path (Join-Path $proj $d) -Force | Out-Null }
    if (-not $NoProject) {
        Write-Text (Join-Path $proj 'project.godot') @('config_version=5', '', '[dotnet]', '', 'project/assembly_name="Keyrun"')
        Write-Text (Join-Path $proj 'Keyrun.csproj') @('<Project Sdk="Godot.NET.Sdk/4.7.2">', '  <PropertyGroup><AssemblyName>Keyrun</AssemblyName></PropertyGroup>', '</Project>')
    }
    $boot = Join-Path $proj 'src\Boot.cs'
    Write-Text $boot @('class Boot { }')
    (Get-Item -LiteralPath $boot).LastWriteTimeUtc = [DateTime]::Parse('2000-01-01T00:00:00Z').ToUniversalTime()
    return $root
}

function Add-Assembly([string]$root, [DateTime]$mtime, [string]$name = 'Keyrun') {
    $dll = Join-Path $root "game\.godot\mono\temp\bin\Debug\$name.dll"
    Write-Text $dll @('MZ')
    (Get-Item -LiteralPath $dll).LastWriteTimeUtc = $mtime
}

function Add-Source([string]$root, [string]$rel, [DateTime]$mtime) {
    $f = Join-Path $root ('game\' + $rel)
    Write-Text $f @('class X { }')
    (Get-Item -LiteralPath $f).LastWriteTimeUtc = $mtime
}

$past = [DateTime]::Parse('2020-06-01T00:00:00Z').ToUniversalTime()
$older = [DateTime]::Parse('2019-01-01T00:00:00Z').ToUniversalTime()
$newer = [DateTime]::Parse('2030-01-01T00:00:00Z').ToUniversalTime()

try {
    # P1: no .godot folder at all.
    $r = Test-KeyrunDotnetAssembly -RepoRoot (New-Fixture 'p1') -ProjectRel 'game'
    Check 'P1.missing-code-6' ($r.Ok -eq $false -and $r.Code -eq 6) "Ok=$($r.Ok) Code=$($r.Code)"
    Check 'P1.missing-names-path' ($r.Lines.Count -ge 1 -and $r.Lines[0] -like 'PREFLIGHT missing-assembly *game/.godot/mono/temp/bin/Debug/Keyrun.dll') "line0=$($r.Lines[0])"

    # P2: assembly older than the newest .cs under src.
    $root = New-Fixture 'p2'
    Add-Assembly $root $older
    Add-Source $root 'src\Hud\Late.cs' $newer
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    Check 'P2.stale-code-6' ($r.Ok -eq $false -and $r.Code -eq 6) "Ok=$($r.Ok) Code=$($r.Code)"
    Check 'P2.stale-names-newer' ($r.Lines.Count -ge 1 -and $r.Lines[0] -like 'PREFLIGHT stale-assembly *game/.godot/mono/temp/bin/Debug/Keyrun.dll newer=game/src/Hud/Late.cs') "line0=$($r.Lines[0])"

    # P3: assembly newer than every .cs.
    $root = New-Fixture 'p3'
    Add-Assembly $root $newer
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    Check 'P3.fresh-ok' ($r.Ok -eq $true -and $r.Code -eq 0 -and $r.Lines.Count -eq 0) "Ok=$($r.Ok) Code=$($r.Code) lines=$($r.Lines -join '|')"

    # P4: same mtime is not older.
    $root = New-Fixture 'p4'
    Add-Assembly $root $past
    Add-Source $root 'sim\Same.cs' $past
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    Check 'P4.equal-mtime-ok' ($r.Ok -eq $true -and $r.Code -eq 0) "Ok=$($r.Ok) Code=$($r.Code) line0=$($r.Lines[0])"

    # P5: generated .cs under an obj folder is ignored; the same file outside obj is not.
    $root = New-Fixture 'p5'
    Add-Assembly $root $past
    Add-Source $root 'src\obj\Gen.cs' $newer
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    Check 'P5.obj-cs-ignored' ($r.Ok -eq $true -and $r.Code -eq 0) "Ok=$($r.Ok) Code=$($r.Code) line0=$($r.Lines[0])"
    $root = New-Fixture 'p5b'
    Add-Assembly $root $past
    Add-Source $root 'src\Real.cs' $newer
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    Check 'P5b.src-cs-counts' ($r.Ok -eq $false -and $r.Lines[0] -like 'PREFLIGHT stale-assembly *newer=game/src/Real.cs') "Ok=$($r.Ok) line0=$($r.Lines[0])"

    # P6: the name comes from project.godot.
    $root = New-Fixture 'p6'
    Write-Text (Join-Path $root 'game\project.godot') @('[dotnet]', '', 'project/assembly_name="Other"')
    Add-Assembly $root $newer 'Keyrun'
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    Check 'P6.name-from-project-godot-missing' ($r.Code -eq 6 -and $r.Lines[0] -like 'PREFLIGHT missing-assembly *game/.godot/mono/temp/bin/Debug/Other.dll') "Code=$($r.Code) line0=$($r.Lines[0])"
    Add-Assembly $root $newer 'Other'
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    Check 'P6b.name-from-project-godot-ok' ($r.Ok -eq $true -and $r.Code -eq 0) "Ok=$($r.Ok) Code=$($r.Code) line0=$($r.Lines[0])"

    # P7: a newer .cs outside the four scanned folders does not count.
    $root = New-Fixture 'p7'
    Add-Assembly $root $past
    Add-Source $root 'tests\Only.cs' $newer
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    Check 'P7.tests-cs-not-scanned' ($r.Ok -eq $true -and $r.Code -eq 0) "Ok=$($r.Ok) Code=$($r.Code) line0=$($r.Lines[0])"

    # P8: nothing names the assembly.
    $root = New-Fixture 'p8' -NoProject
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    Check 'P8.unknown-code-6' ($r.Ok -eq $false -and $r.Code -eq 6 -and $r.Lines[0] -like 'PREFLIGHT unknown-assembly *') "Ok=$($r.Ok) Code=$($r.Code) line0=$($r.Lines[0])"

    # P9: failures carry the build command.
    $root = New-Fixture 'p9'
    $r = Test-KeyrunDotnetAssembly -RepoRoot $root -ProjectRel 'game'
    $buildLine = @($r.Lines | Where-Object { $_ -match 'build-command' })
    Check 'P9.build-command-line' ($buildLine.Count -eq 1 -and $buildLine[0] -match ' build game/Keyrun\.csproj -c Debug$') ("lines=" + ($r.Lines -join ' | '))

    # P10: an absolute --path (Godot accepts one) names the project folder itself, not a folder under the repo root.
    $root = New-Fixture 'p10'
    Add-Assembly $root $newer
    $absProj = Join-Path $root 'game'
    $r = Test-KeyrunDotnetAssembly -RepoRoot (Join-Path $work 'elsewhere') -ProjectRel $absProj
    Check 'P10.absolute-path-fresh-ok' ($r.Ok -eq $true -and $r.Code -eq 0) "Ok=$($r.Ok) Code=$($r.Code) line0=$($r.Lines[0])"
    $root = New-Fixture 'p10b'
    $absProj = Join-Path $root 'game'
    $r = Test-KeyrunDotnetAssembly -RepoRoot (Join-Path $work 'elsewhere') -ProjectRel $absProj
    $want = ($absProj -replace '\\', '/') + '/.godot/mono/temp/bin/Debug/Keyrun.dll'
    Check 'P10b.absolute-path-missing-names-it' ($r.Code -eq 6 -and $r.Lines[0] -eq ('PREFLIGHT missing-assembly ' + $want)) "Code=$($r.Code) line0=$($r.Lines[0])"

    # D1-D3: the .NET runtime the engine loads. Test-KeyrunDotnetRuntime must exist (a missing function is a failure).
    $haveRuntimeCheck = [bool](Get-Command Test-KeyrunDotnetRuntime -ErrorAction SilentlyContinue)
    Check 'D0.runtime-check-defined' $haveRuntimeCheck 'preflight.ps1 defines no Test-KeyrunDotnetRuntime'
    if ($haveRuntimeCheck) {
        $fakeRoot = Join-Path $work 'no-such-dotnet'
        $r = Test-KeyrunDotnetRuntime -DotnetRoot $fakeRoot
        $want = 'PREFLIGHT missing-dotnet ' + ((Join-Path $fakeRoot 'dotnet.exe') -replace '\\', '/')
        Check 'D1.root-without-dotnet-code-6' ($r.Ok -eq $false -and $r.Code -eq 6 -and $r.Lines[0] -eq $want) "Ok=$($r.Ok) Code=$($r.Code) line0=$($r.Lines[0])"

        $goodRoot = Join-Path $work 'fake-dotnet'
        Write-Text (Join-Path $goodRoot 'dotnet.exe') @('MZ')
        $r = Test-KeyrunDotnetRuntime -DotnetRoot $goodRoot
        Check 'D2.root-with-dotnet-ok' ($r.Ok -eq $true -and $r.Code -eq 0 -and $r.Lines.Count -eq 0) "Ok=$($r.Ok) Code=$($r.Code) line0=$($r.Lines[0])"

        # No root: dotnet must resolve on PATH. PATH is narrowed to an empty folder for this case only and restored.
        $savedPath = $env:PATH
        $emptyDir = Join-Path $work 'empty-path'
        New-Item -ItemType Directory -Path $emptyDir | Out-Null
        try {
            $env:PATH = $emptyDir
            $r = Test-KeyrunDotnetRuntime -DotnetRoot ''
        } finally { $env:PATH = $savedPath }
        Check 'D3.no-root-no-path-code-6' ($r.Ok -eq $false -and $r.Code -eq 6 -and $r.Lines[0] -like 'PREFLIGHT missing-dotnet *') "Ok=$($r.Ok) Code=$($r.Code) line0=$($r.Lines[0])"
    }

    # R1/R2: the run-log check screens.ps1 ends with.
    $logDir = Join-Path $work 'r'
    New-Item -ItemType Directory -Path $logDir | Out-Null
    $log = Join-Path $logDir 'screens-run.txt'
    Check 'R1.no-log-false' ((Test-KeyrunRunLogWritten -LogPath $log) -eq $false) 'reported a log that does not exist'
    Write-Text $log @('EVENT {"type":"run_end"}')
    Check 'R2.log-true' ((Test-KeyrunRunLogWritten -LogPath $log) -eq $true) 'a present log was reported missing'
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Output ("screens-preflight-test: FAIL ({0})" -f $failures.Count)
    exit 1
}
Write-Output 'screens-preflight-test: PASS'
exit 0
