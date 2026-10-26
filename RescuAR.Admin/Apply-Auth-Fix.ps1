# Apply from RescuAR.Admin after extracting rbac_auth_fix_patch/ here.
# All original project files are checked before anything is overwritten.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PatchRoot = $PSScriptRoot
$ProjectRoot = Split-Path -Parent $PatchRoot
$Items = @(
    @{ Relative = "src/components/Auth.jsx"; ExpectedSha256 = "CF947DDE9AD378A52299B3052BD437806FF8B6A49218CBEF9A29D2B4D1EAE4C9" },
    @{ Relative = "src/AuthWrapper.jsx"; ExpectedSha256 = "DC48831C63B3941015833D182BA41363E9C68865AD6AA06CDF46D125A10CD68F" },
    @{ Relative = "src/components/Header.jsx"; ExpectedSha256 = "7F095B474B6F483E782A18FFDAE1E24B57FE4C536F6F66E1C3E25A40B25D377A" }
)

$ToCopy = @()
foreach ($item in $Items) {
  $relativeWindows = $item.Relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
  $target = Join-Path $ProjectRoot $relativeWindows
  $replacement = Join-Path (Join-Path $PatchRoot 'payload') $relativeWindows
  if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw "Target file missing: $target" }
  if (!(Test-Path -LiteralPath $replacement -PathType Leaf)) { throw "Patch file missing: $replacement" }
  $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
  $desired = (Get-FileHash -LiteralPath $replacement -Algorithm SHA256).Hash
  if ($actual -eq $desired) { Write-Host "Already patched: $($item.Relative)"; continue }
  if ($actual -ne $item.ExpectedSha256) {
    throw "File differs from uploaded snapshot: $($item.Relative). No files changed. Merge manually to preserve local edits."
  }
  $ToCopy += @{ Relative = $item.Relative; Target = $target; Replacement = $replacement }
}
$TestSource = Join-Path $PatchRoot 'payload/tests/rbac-auth-regression.test.mjs'
$TestTarget = Join-Path $ProjectRoot 'tests/rbac-auth-regression.test.mjs'
if (Test-Path -LiteralPath $TestTarget -PathType Leaf) {
  if ((Get-FileHash $TestTarget -Algorithm SHA256).Hash -ne (Get-FileHash $TestSource -Algorithm SHA256).Hash) {
    throw 'Existing test file differs; no files changed. Rename it or merge manually.'
  }
}
if ($ToCopy.Count -gt 0) {
  $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
  $backup = Join-Path (Join-Path $PatchRoot 'backups') $timestamp
  foreach ($item in $ToCopy) {
    $backupPath = Join-Path $backup ($item.Relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
    New-Item -Path (Split-Path $backupPath -Parent) -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath $item.Target -Destination $backupPath
  }
  foreach ($item in $ToCopy) {
    Copy-Item -LiteralPath $item.Replacement -Destination $item.Target -Force
    Write-Host "Patched: $($item.Relative)"
  }
  Write-Host "Backup saved: $backup"
}
if (!(Test-Path -LiteralPath $TestTarget)) {
  New-Item -Path (Split-Path -Parent $TestTarget) -ItemType Directory -Force | Out-Null
  Copy-Item -LiteralPath $TestSource -Destination $TestTarget
  Write-Host 'Installed regression test'
}
Write-Host 'Auth/RBAC patch complete. Run: node --test tests/rbac-auth-regression.test.mjs'
