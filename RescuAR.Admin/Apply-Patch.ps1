[CmdletBinding()]
param([switch]$DryRun)
$ErrorActionPreference = 'Stop'
$PatchFolder = $PSScriptRoot
$ProjectRoot = (Resolve-Path (Join-Path $PatchFolder '..')).Path
$PackageJson = Join-Path $ProjectRoot 'package.json'
if (-not (Test-Path -LiteralPath $PackageJson)) { throw 'Place the extracted RescuAR_Admin_Provisioning_PATCH folder inside RescuAR.Admin and run again.' }

$Checks = Get-Content -LiteralPath (Join-Path $PatchFolder 'base-checksums.json') -Raw | ConvertFrom-Json
$Modified = @('src/components/RbacAdmins.jsx', 'src/components/RbacPages.css', 'src/AuthWrapper.jsx')
$New = @(
  'src/components/FirstLoginPasswordChange.jsx',
  'src/components/FirstLoginPasswordChange.css',
  'database/migrations/005_rbac_admin_provisioning.sql',
  'supabase/functions/rbac-admin-provision/index.ts',
  'tests/rbac-admin-provisioning.test.mjs'
)
$All = @($Modified + $New)

# Validate all paths BEFORE touching any project file.
foreach ($Relative in $All) {
  $RelativeNative = $Relative.Replace('/', [string][IO.Path]::DirectorySeparatorChar)
  $Source = Join-Path $PatchFolder $RelativeNative
  $Target = Join-Path $ProjectRoot $RelativeNative
  if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Patch is incomplete: $Relative" }
  if ($Modified -contains $Relative) {
    if (-not (Test-Path -LiteralPath $Target -PathType Leaf)) { throw "Expected existing file missing: $Relative" }
    $Expected = [string]$Checks.$Relative
    $Actual = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Actual -ne $Expected) { throw "Existing file differs from the October 26 uploaded baseline: $Relative`nExpected SHA256: $Expected`nActual SHA256: $Actual`nSTOP and merge the patch manually; nothing was overwritten." }
  } elseif (Test-Path -LiteralPath $Target) {
    throw "New patch file already exists: $Relative. Review before applying; nothing was overwritten."
  }
}

if ($DryRun) {
  Write-Host "DRY RUN PASS: $($Modified.Count) existing files match original SHA256, and $($New.Count) new files do not exist." -ForegroundColor Green
  exit 0
}

$BackupDir = Join-Path $ProjectRoot ('rbac_admin_provision_backups_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
foreach ($Relative in $Modified) {
  $Native = $Relative.Replace('/', [string][IO.Path]::DirectorySeparatorChar)
  $Target = Join-Path $BackupDir $Native
  New-Item -ItemType Directory -Path (Split-Path $Target -Parent) -Force | Out-Null
  Copy-Item -LiteralPath (Join-Path $ProjectRoot $Native) -Destination $Target -Force
}
foreach ($Relative in $All) {
  $Native = $Relative.Replace('/', [string][IO.Path]::DirectorySeparatorChar)
  $Target = Join-Path $ProjectRoot $Native
  New-Item -ItemType Directory -Path (Split-Path $Target -Parent) -Force | Out-Null
  Copy-Item -LiteralPath (Join-Path $PatchFolder $Native) -Destination $Target -Force
  Write-Host "Patched: $Relative"
}
Write-Host "Patch installed. Existing files backed up in: $BackupDir" -ForegroundColor Green
Write-Host 'NEXT: Apply SQL migration 005 in Supabase, configure secret origin allowlist, and deploy the Edge Function. See README_PATCH.md.' -ForegroundColor Yellow
