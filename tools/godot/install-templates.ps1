<#
.SYNOPSIS
  Install the Godot 4.7.2 .NET Windows x86_64 export templates (no login, no GUI).

.DESCRIPTION
  Downloads Godot_v4.7.2-stable_mono_export_templates.tpz from the official
  godotengine/godot GitHub release, verifies it against the release's
  SHA512-SUMS.txt (fails closed on mismatch), and extracts ONLY the Windows
  x86_64 templates plus version.txt into
  <TemplatesRoot>/4.7.2.stable.mono/ (default %APPDATA%\Godot\export_templates).
  The full archive is about 1.2 GB; the extracted Windows subset is small, so
  CI caches the extracted directory, not the archive.

  Idempotent: exits 0 without downloading when every required file is present.

.PARAMETER TemplatesRoot
  Godot's export_templates directory. Default: $env:APPDATA\Godot\export_templates.

.PARAMETER DownloadDir
  Where the .tpz is downloaded. Default: $env:RUNNER_TEMP, else $env:TEMP.
  The archive is deleted after a successful install.

.EXAMPLE
  pwsh tools/godot/install-templates.ps1
#>
param(
    [string]$TemplatesRoot = '',
    [string]$DownloadDir = ''
)
$ErrorActionPreference = 'Stop'

$version = '4.7.2'
$versionDir = "$version.stable.mono"
$asset = "Godot_v$version-stable_mono_export_templates.tpz"
$base = "https://github.com/godotengine/godot/releases/download/$version-stable"
$required = @(
    'version.txt',
    'windows_release_x86_64.exe',
    'windows_release_x86_64_console.exe',
    'windows_debug_x86_64.exe',
    'windows_debug_x86_64_console.exe'
)

if (-not $TemplatesRoot) {
    if (-not $env:APPDATA) { throw 'install-templates.ps1: APPDATA is not set; pass -TemplatesRoot.' }
    $TemplatesRoot = Join-Path (Join-Path $env:APPDATA 'Godot') 'export_templates'
}
if (-not $DownloadDir) {
    $DownloadDir = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }
}
$dest = Join-Path $TemplatesRoot $versionDir

$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $dest $_) -PathType Leaf) })
if ($missing.Count -eq 0) {
    Write-Output "TEMPLATES present: $dest"
    exit 0
}

New-Item -ItemType Directory -Force -Path $DownloadDir | Out-Null
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$tpz = Join-Path $DownloadDir $asset
$sums = Join-Path $DownloadDir 'godot-SHA512-SUMS.txt'

& curl.exe --fail --location --silent --show-error --output $sums "$base/SHA512-SUMS.txt"
if ($LASTEXITCODE -ne 0) { throw "install-templates.ps1: download of SHA512-SUMS.txt failed ($LASTEXITCODE)" }
$line = Get-Content -LiteralPath $sums | Where-Object { $_ -match ([regex]::Escape($asset) + '$') } | Select-Object -First 1
if (-not $line) { throw "install-templates.ps1: $asset not listed in SHA512-SUMS.txt" }
$expected = ($line -split '\s+')[0].ToLowerInvariant()

Write-Output "DOWNLOAD $base/$asset"
& curl.exe --fail --location --silent --show-error --output $tpz "$base/$asset"
if ($LASTEXITCODE -ne 0) { throw "install-templates.ps1: download of $asset failed ($LASTEXITCODE)" }

$actual = (Get-FileHash -LiteralPath $tpz -Algorithm SHA512).Hash.ToLowerInvariant()
if ($actual -ne $expected) {
    Remove-Item -LiteralPath $tpz -Force
    throw "install-templates.ps1: SHA512 mismatch for $asset (expected $expected, got $actual)"
}
Write-Output "SHA512 ok $asset"

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($tpz)
try {
    foreach ($name in $required) {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq "templates/$name" } | Select-Object -First 1
        if (-not $entry) { throw "install-templates.ps1: templates/$name not found in $asset" }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $dest $name), $true)
    }
}
finally {
    $zip.Dispose()
}
Remove-Item -LiteralPath $tpz -Force

$installed = (Get-Content -LiteralPath (Join-Path $dest 'version.txt') -Raw).Trim()
if ($installed -ne $versionDir) {
    throw "install-templates.ps1: version.txt says '$installed', expected '$versionDir'"
}
Write-Output "TEMPLATES installed: $dest ($installed)"
