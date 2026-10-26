# Applies the RBAC UI layout patch to an existing RescuAR.Admin project.
# Does not alter RBAC SQL, authentication, sessions, telemetry, or production advisories.
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $PSScriptRoot 'files'
$existingHashes = @{
    'src/components/Sidebar.jsx' = '6A870B52E6781A6C47D28E130FC0C5F72DC128584C1CAC652704C1B1EF33854C',
    'src/components/RbacAdmins.jsx' = '62B6B6816EEBDA38E15085A41BCE2100C746BA51680698711E67943A58F0CC36',
    'src/components/RbacTesting.jsx' = '263F3BD2332B0E406861E7CF0347380429D6634B24BDF588D8F1B96E2640FBC0'
}
$patchedHashes = @{
    'src/components/Sidebar.jsx' = 'C222073EC4F57F3F6155D9168AE92DB2AF8E70BB97427E2654DCC9E1D327C248',
    'src/components/RbacAdmins.jsx' = '0EC62C6232C56BB7946AAFB06C5962D64191861ECB696CCBA542060B3653CBD8',
    'src/components/RbacTesting.jsx' = '7B2AC4483826EC2AEA5F99A1F09617C90507A843F72F6CF6D2A1791CD8EA19E9',
    'src/components/SidebarRbac.css' = '3DCB1550ED511F7A59A6065639503522D6DCBB0F7006AB39B576E55D595C0233',
    'src/components/RbacPages.css' = '896D660EF484EAD890991E147394C71E9F55C2E0F0C9464F9145BB86CA6C617A',
    'tests/rbac-ui-regression.test.mjs' = '6CE0F9924990F780F9F035BEAE102B4543FDCC5B110000604EF50CFE7623CEB3'
}
$paths = @(
    'src/components/Sidebar.jsx',
    'src/components/RbacAdmins.jsx',
    'src/components/RbacTesting.jsx',
    'src/components/SidebarRbac.css',
    'src/components/RbacPages.css',
    'tests/rbac-ui-regression.test.mjs'
)

if (!(Test-Path (Join-Path $project 'src/App.jsx'))) {
    throw "RescuAR.Admin root not found. Extract the ZIP into RescuAR.Admin and run .\\RescuAR_RBAC_UI_PATCH\\Apply-RBAC-UI.ps1"
}

Write-Host "RescuAR RBAC UI patch - target: $project"
$changes = @()
foreach ($relative in $paths) {
    $source = Join-Path $payload $relative
    $target = Join-Path $project $relative
    if (!(Test-Path $source)) { throw "Missing payload: $relative" }
    $actualSourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ($actualSourceHash -ne $patchedHashes[$relative]) { throw "Patch file integrity check failed: $relative" }
    if (Test-Path $target) {
        $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($actual -eq $patchedHashes[$relative]) {
            Write-Host "Already patched: $relative"
            continue
        }
        if ($existingHashes.ContainsKey($relative) -and $actual -ne $existingHashes[$relative] -and !$Force) {
            throw "STOP: $relative differs from the reviewed uploaded archive. No files were modified. Review local changes, or rerun with -Force to back up and replace this file."
        }
        if (!$existingHashes.ContainsKey($relative) -and !$Force) {
            throw "STOP: $relative already exists and differs from the patch. No files were modified. Rerun with -Force only after reviewing local changes."
        }
    }
    $changes += $relative
}
if ($changes.Count -eq 0) {
    Write-Host 'All RBAC UI files are already patched.'
    exit 0
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = Join-Path $PSScriptRoot "backups/$stamp"
foreach ($relative in $changes) {
    $source = Join-Path $payload $relative
    $target = Join-Path $project $relative
    if (Test-Path $target) {
        $saved = Join-Path $backup $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $saved) | Out-Null
        Copy-Item -LiteralPath $target -Destination $saved -Force
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    Write-Host "Updated: $relative"
}
Write-Host "Patch applied. Backups (if previous versions existed): $backup"
Write-Host 'Run: node --test .\\tests\\rbac-ui-regression.test.mjs'
Write-Host 'Then: npm run build'
