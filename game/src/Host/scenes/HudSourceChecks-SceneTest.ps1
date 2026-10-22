<#
.SYNOPSIS
  HUD source checks 25-26 (lane HUD-FU): no stale run-over banner in the ability bar, no stale queue caption on the
  screen. The ability bar draws with immediate-mode DrawString, so no node or line shows it; these read the source.
  No launch.

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3), where they ran before every
  launch and with -HeadingOnly. The check text and the RED notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudSourceChecks-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL. No launch: it never runs tools/godot/godot.ps1 and takes no launch lock.
#>
param()
$SceneName = 'HudSourceChecks-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')

# ---- Source checks 25-26 (lane HUD-FU): no stale run-over banner, no stale queue caption ----------------------
$barSrc = Get-Content -Raw (Join-Path $repo 'game/src/Hud/AbilityBar.cs')
$panelSrc = Get-Content -Raw (Join-Path $repo 'game/src/Hud/RunEndPanel.cs')
# Screen.cs plus its partials (S8, #556 split the lobby Screen into Screen.*.cs by tab and concern), so check 26 still
# reads the whole Screen class; the Screen.cs-only read would miss a caption that moved into a partial.
$screenFiles = @(Get-ChildItem -LiteralPath (Join-Path $repo 'game/src/Hud') -File -Filter 'Screen*.cs' | Where-Object { $_.Name -match '^Screen(\.[A-Za-z]+)?\.cs$' } | Sort-Object Name)
if ($screenFiles.Count -eq 0) { throw 'no game/src/Hud/Screen*.cs found: check 26 has nothing to read' }
$screenSrc = ($screenFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
if ($barSrc -match 'Run over') { $failures.Add('AbilityBar.cs still draws a "Run over" banner (the run-end panel owns the run-over state)') }
if ($barSrc -match 'Draw(String|Centered|Meter)\([^;]*(RunEnd|RunResult|\.ToJs\(\))') { $failures.Add('AbilityBar.cs draws run-over text (the run-end panel owns the result, rewards and Enter hint)') }
if ($barSrc -notmatch 'ShowsOutOfRange\([^;]*\brunOver:') { $failures.Add('AbilityBar.cs calls ShowsOutOfRange without a runOver argument (the range tint must be off once the run has ended, lane TINT-RUNOVER)') }
if ($panelSrc -notmatch 'Enter starts a fresh run') { $failures.Add('RunEndPanel.cs no longer draws "Enter starts a fresh run" (the only Enter hint once the bar banner is gone)') }
if ($screenSrc -match 'does not read the queued') { $failures.Add('Screen.cs still says the host does not read the queued key and Clause (false since 3I/3J)') }

Complete-Scene 'checks=25,26'
