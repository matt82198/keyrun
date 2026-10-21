<#
.SYNOPSIS
  Preflight shared by tools/godot/godot.ps1 and tools/godot/screens.ps1: the project's .NET assembly must exist and be fresh.

.DESCRIPTION
  Dot-source this file:  . tools/godot/preflight.ps1

  Why: Godot loads the project's .NET assembly from <project>/.godot/mono/temp/bin/<config>/<AssemblyName>.dll. Without it the
  engine still boots the boot scene, the C# autorun never runs, and a D15 capture reports success on a boot-scene-only run
  (finding f-3ccab3b2). With it older than the newest .cs file under src, sim, bots or content, the engine runs stale code.

  Test-KeyrunDotnetAssembly checks both conditions and returns the verdict. It never builds: the orchestrator decides when to
  run the build command it prints. The assembly name is read from project/assembly_name in project.godot, falling back to the
  AssemblyName of the project's .csproj. Generated .cs under bin, obj or .godot is not scanned.

  Functions:
    Get-KeyrunAssemblyName -ProjectDir <dir>                          the assembly name; $null when neither file names it
    Get-KeyrunDotnetCommand                                           'dotnet' when on PATH, else the %USERPROFILE%\.dotnet\dotnet.exe path
    Test-KeyrunDotnetAssembly -RepoRoot <dir> [-ProjectRel game] [-Config Debug]
                                                                      @{ Ok; Code; Lines } where Code is 0 or 6
                                                                      (an absolute ProjectRel is the project folder itself)
    Test-KeyrunDotnetRuntime -DotnetRoot <dir or ''>                  @{ Ok; Code; Lines }: <dir>/dotnet.exe exists, or with no
                                                                      root, dotnet resolves on PATH; else Code 6 'PREFLIGHT missing-dotnet'
    Test-KeyrunImportCache -RepoRoot <dir> [-ProjectRel game]         @{ Ok; Code; Lines } (finding f-30dcbb08): every .glb under
                                                                      <project>/assets needs a fresh <name>.glb-<hash>.scn in .godot/imported;
                                                                      else Code 6 'PREFLIGHT missing-import-cache <n> models unimported; run tools/godot/import.ps1'
    Test-KeyrunRunLogWritten -LogPath <file>                          $true when the --log file was created
    Resolve-KeyrunRunLogPath -RepoRoot <dir> -ProjectRel <p> -LogRel <f>
                                                                      where screens.ps1 writes the --log file (absolute --path or --log honoured)
#>

$script:KeyrunPreflightSourceDirs = @('src', 'sim', 'bots', 'content')
$script:KeyrunPreflightSkipSegments = @('bin', 'obj', '.godot')

function Get-KeyrunAssemblyName {
    param([Parameter(Mandatory = $true)][string]$ProjectDir)
    $godotFile = Join-Path $ProjectDir 'project.godot'
    if (Test-Path -LiteralPath $godotFile -PathType Leaf) {
        $m = [regex]::Match([IO.File]::ReadAllText($godotFile), '(?m)^\s*project/assembly_name\s*=\s*"([^"]+)"')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    $csproj = Get-ChildItem -LiteralPath $ProjectDir -Filter '*.csproj' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($csproj) {
        $m = [regex]::Match([IO.File]::ReadAllText($csproj.FullName), '<AssemblyName>\s*([^<\s]+)\s*</AssemblyName>')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    return $null
}

function Get-KeyrunDotnetCommand {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { return 'dotnet' }
    if ($env:USERPROFILE) {
        $exe = Join-Path (Join-Path $env:USERPROFILE '.dotnet') 'dotnet.exe'
        if (Test-Path -LiteralPath $exe -PathType Leaf) { return ($exe -replace '\\', '/') }
    }
    return 'dotnet'
}

function Test-KeyrunDotnetAssembly {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$ProjectRel = 'game',
        [string]$Config = 'Debug'
    )
    # Godot accepts an absolute --path; only a relative one is under the repo root.
    if ([IO.Path]::IsPathRooted($ProjectRel)) { $projectDir = $ProjectRel } else { $projectDir = Join-Path $RepoRoot $ProjectRel }
    $csproj =Get-ChildItem -LiteralPath $projectDir -Filter '*.csproj' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    $csprojName = 'Keyrun.csproj'
    if ($csproj) { $csprojName = $csproj.Name }
    $buildLine = '{0} build {1}/{2} -c {3}' -f (Get-KeyrunDotnetCommand), $ProjectRel, $csprojName, $Config

    $name = Get-KeyrunAssemblyName -ProjectDir $projectDir
    if (-not $name) {
        $where = ($projectDir -replace '\\', '/')
        return [pscustomobject]@{ Ok = $false; Code = 6; Lines = @(
            ("PREFLIGHT unknown-assembly {0} (project.godot has no project/assembly_name and no .csproj names an AssemblyName)" -f $where)) }
    }

    $dll = [IO.Path]::Combine($projectDir, '.godot', 'mono', 'temp', 'bin', $Config, ($name + '.dll'))
    $shown = ($dll -replace '\\', '/')
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        return [pscustomobject]@{ Ok = $false; Code = 6; Lines = @(
            ("PREFLIGHT missing-assembly {0}" -f $shown),
            ("PREFLIGHT build-command (from repo root): {0}" -f $buildLine)) }
    }

    $asm = Get-Item -LiteralPath $dll
    $newest = $null
    foreach ($d in $script:KeyrunPreflightSourceDirs) {
        $dir = Join-Path $projectDir $d
        if (-not (Test-Path -LiteralPath $dir -PathType Container)) { continue }
        foreach ($f in @(Get-ChildItem -LiteralPath $dir -Recurse -Filter '*.cs' -File)) {
            $segments = $f.FullName.Substring($projectDir.Length) -split '[\\/]'
            if (@($segments | Where-Object { $script:KeyrunPreflightSkipSegments -contains $_ }).Count -gt 0) { continue }
            if ($null -eq $newest -or $f.LastWriteTimeUtc -gt $newest.LastWriteTimeUtc) { $newest = $f }
        }
    }
    if ($newest -and $newest.LastWriteTimeUtc -gt $asm.LastWriteTimeUtc) {
        $rel = $newest.FullName.Substring($projectDir.Length).TrimStart('\', '/') -replace '\\', '/'
        return [pscustomobject]@{ Ok = $false; Code = 6; Lines = @(
            ("PREFLIGHT stale-assembly {0} newer={1}/{2}" -f $shown, $ProjectRel, $rel),
            ("PREFLIGHT build-command (from repo root): {0}" -f $buildLine)) }
    }

    return [pscustomobject]@{ Ok = $true; Code = 0; Lines = @() }
}

function Test-KeyrunImportCache {
    # Godot imports every .glb under <project>/assets into <project>/.godot/imported as "<file name>-<hash>.scn".
    # A fresh worktree has no such cache, so the models never import and Actor falls back to capsules (finding f-30dcbb08).
    # A model is unimported when it has no cache file or its newest cache file is older than the model.
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$ProjectRel = 'game'
    )
    if ([IO.Path]::IsPathRooted($ProjectRel)) { $projectDir = $ProjectRel } else { $projectDir = Join-Path $RepoRoot $ProjectRel }
    $models = @()
    $assets = Join-Path $projectDir 'assets'
    if (Test-Path -LiteralPath $assets -PathType Container) {
        $models = @(Get-ChildItem -LiteralPath $assets -Recurse -Filter '*.glb' -File)
    }
    $cacheDir = Join-Path (Join-Path $projectDir '.godot') 'imported'
    $cacheMtime = @{}
    if (Test-Path -LiteralPath $cacheDir -PathType Container) {
        foreach ($c in @(Get-ChildItem -LiteralPath $cacheDir -Filter '*.scn' -File)) {
            $m = [regex]::Match($c.Name, '^(.+\.glb)-\w+\.scn$')
            if (-not $m.Success) { continue }
            $key = $m.Groups[1].Value
            if (-not $cacheMtime.ContainsKey($key) -or $c.LastWriteTimeUtc -gt $cacheMtime[$key]) { $cacheMtime[$key] = $c.LastWriteTimeUtc }
        }
    }
    $missing = 0
    foreach ($g in $models) {
        if (-not $cacheMtime.ContainsKey($g.Name) -or $g.LastWriteTimeUtc -gt $cacheMtime[$g.Name]) { $missing++ }
    }
    if ($missing -gt 0) {
        return [pscustomobject]@{ Ok = $false; Code = 6; Lines = @(
            ("PREFLIGHT missing-import-cache {0} models unimported; run tools/godot/import.ps1" -f $missing)) }
    }
    return [pscustomobject]@{ Ok = $true; Code = 0; Lines = @() }
}

function Test-KeyrunDotnetRuntime {
    # The .NET runtime the engine loads: <DotnetRoot>/dotnet.exe when a root was resolved, else dotnet on PATH.
    param([AllowEmptyString()][AllowNull()][string]$DotnetRoot)
    if ($DotnetRoot) {
        $exeName = 'dotnet'
        if ($env:OS -eq 'Windows_NT') { $exeName = 'dotnet.exe' }
        $exe = Join-Path $DotnetRoot $exeName
        if (Test-Path -LiteralPath $exe -PathType Leaf) { return [pscustomobject]@{ Ok = $true; Code = 0; Lines = @() } }
        return [pscustomobject]@{ Ok = $false; Code = 6; Lines = @(
            ("PREFLIGHT missing-dotnet {0}" -f ($exe -replace '\\', '/')),
            'PREFLIGHT set DOTNET_ROOT to a folder holding dotnet.exe (default %USERPROFILE%\.dotnet)') }
    }
    if (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue) { return [pscustomobject]@{ Ok = $true; Code = 0; Lines = @() } }
    return [pscustomobject]@{ Ok = $false; Code = 6; Lines = @(
        'PREFLIGHT missing-dotnet (no DOTNET_ROOT, no %USERPROFILE%\.dotnet\dotnet.exe, no dotnet on PATH)',
        'PREFLIGHT set DOTNET_ROOT to a folder holding dotnet.exe (default %USERPROFILE%\.dotnet)') }
}

function Test-KeyrunRunLogWritten {
    param([Parameter(Mandatory = $true)][string]$LogPath)
    return (Test-Path -LiteralPath $LogPath -PathType Leaf)
}

function Resolve-KeyrunRunLogPath {
    # Where Godot writes the --log file: a relative --log resolves against the --path project folder (default game), not the repo root.
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectRel,
        [Parameter(Mandatory = $true)][string]$LogRel
    )
    # Godot accepts an absolute --path; only a relative one is under the repo root (same rule as Test-KeyrunDotnetAssembly).
    if ([IO.Path]::IsPathRooted($LogRel)) { return $LogRel }
    if ([IO.Path]::IsPathRooted($ProjectRel)) { return (Join-Path $ProjectRel $LogRel) }
    return (Join-Path (Join-Path $RepoRoot $ProjectRel) $LogRel)
}
