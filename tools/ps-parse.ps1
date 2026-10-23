<#
.SYNOPSIS
  Parse-checks PowerShell files with the PowerShell language parser. Never executes the files.

.DESCRIPTION
  Usage: ./tools/ps-parse.ps1 [file ...]
  With no arguments it moves to the repo root and parses every tracked *.ps1 (git ls-files).
  Prints "ps-parse: OK FILE" for a clean file and "ps-parse: ERROR FILE line N: message" per parse error,
  then "ps-parse: PASS N files" (exit 0) or "ps-parse: FAIL N errors" (exit 1).
#>
param([string[]]$Files)
$ErrorActionPreference = 'Stop'
if (-not $Files -or $Files.Count -eq 0) {
    Set-Location (Split-Path -Parent $PSScriptRoot)
    $Files = @(git ls-files -- '*.ps1')
}
$count = 0
$errors = 0
foreach ($f in $Files) {
    $tokens = $null
    $perr = $null
    $full = (Resolve-Path -LiteralPath $f).ProviderPath
    [void][System.Management.Automation.Language.Parser]::ParseFile($full, [ref]$tokens, [ref]$perr)
    $count++
    if (@($perr).Count -eq 0) {
        Write-Output "ps-parse: OK $f"
        continue
    }
    foreach ($e in $perr) {
        $errors++
        Write-Output "ps-parse: ERROR $f line $($e.Extent.StartLineNumber): $($e.Message)"
    }
}
if ($errors -eq 0) {
    Write-Output "ps-parse: PASS $count files"
    exit 0
}
Write-Output "ps-parse: FAIL $errors errors"
exit 1
