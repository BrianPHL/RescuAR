[CmdletBinding()]
param(
    [string]$DeviceSerial,
    [string]$AdbPath = (Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/navigation-field-evidence'),
    [string]$TrialName = 'navigation-trial'
)

# Read-only collection after a tester's run. Never launches/stops the app,
# changes location/network/depth settings, or clears the device's logs.
$ErrorActionPreference = 'Stop'
$adbExecutable = (Resolve-Path -LiteralPath $AdbPath).Path
$deviceArguments = if ($DeviceSerial) { @('-s', $DeviceSerial) } else { @() }
function Read-Adb([string[]]$CommandArguments) {
    $result = & $adbExecutable @deviceArguments @CommandArguments
    if ($LASTEXITCODE -ne 0) { throw "ADB command failed: $($CommandArguments[0])" }
    return ($result -join "`n").Trim()
}
function Read-OptionalAdb([string[]]$CommandArguments) {
    try { Read-Adb $CommandArguments }
    catch { $null }
}
if ((Read-Adb @('get-state')) -ne 'device') { throw 'Connect and authorize one device, or provide -DeviceSerial.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$packageInformation = Read-Adb @('shell', 'dumpsys', 'package', 'com.rescuar.app')
$arInformation = Read-Adb @('shell', 'dumpsys', 'package', 'com.google.ar.core')
$packagePath = Read-Adb @('shell', 'pm', 'path', 'com.rescuar.app')
$apkPath = ($packagePath -split "`n" | Where-Object { $_ -match '^package:/data/app/[-_A-Za-z0-9/.+=~]+/base\.apk$' } | Select-Object -First 1)
$installedHash = 'unavailable'
if ($apkPath) {
    $apkPath = $apkPath.Substring('package:'.Length)
    try { $installedHash = ((Read-Adb @('shell', 'sha256sum', $apkPath)) -split '\s+')[0] }
    catch { $installedHash = 'unavailable: device did not permit reading the installed base APK' }
}
$memory = Read-OptionalAdb @('shell', 'dumpsys', 'meminfo', 'com.rescuar.app')
$pssKb = if ($memory -match '(?m)\bTOTAL PSS:\s*(\d+)') { [long]$Matches[1] }
    elseif ($memory -match '(?m)^\s*TOTAL\s+(\d+)\s') { [long]$Matches[1] }
$rssKb = if ($memory -match '(?m)\bTOTAL RSS:\s*(\d+)') { [long]$Matches[1] }
$thermal = Read-OptionalAdb @('shell', 'dumpsys', 'thermalservice')
$thermalStatus = if ($thermal -match '(?mi)^\s*(?:Current\s+)?Thermal\s+Status:\s*([0-6])\s*$') { [int]$Matches[1] }
$manifest = [ordered]@{
    trial = $TrialName
    collectedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    deviceUtc = Read-Adb @('shell', 'date', '-u', '+%Y-%m-%dT%H:%M:%SZ')
    model = Read-Adb @('shell', 'getprop', 'ro.product.model')
    manufacturer = Read-Adb @('shell', 'getprop', 'ro.product.manufacturer')
    androidRelease = Read-Adb @('shell', 'getprop', 'ro.build.version.release')
    androidApi = Read-Adb @('shell', 'getprop', 'ro.build.version.sdk')
    installedApkSha256 = $installedHash
    appVersion = @($packageInformation -split "`n" | Where-Object { $_ -match 'versionCode=|versionName=' } | Select-Object -First 2)
    arCoreVersion = @($arInformation -split "`n" | Where-Object { $_ -match 'versionCode=|versionName=' } | Select-Object -First 2)
    postTrialSnapshot = [ordered]@{
        totalPssKb = $pssKb
        totalRssKb = $rssKb
        androidThermalStatus = $thermalStatus
    }
    collection = 'read-only; diagnostic builds include exact replay inputs; production remains redacted'
}
$trialPath = Join-Path $outputRoot 'trial.json'
$logPath = Join-Path $outputRoot 'navigation-logcat.txt'
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $trialPath -Encoding utf8
$filters = @('RescuAR-NavigationQuality:I', 'RescuAR-GPS:I', 'RescuAR-Heading:D',
    'RescuAR-ARCore:I', 'RescuAR-ARRoute:D', 'RescuAR-RouteProgress:D',
    'RescuAR-Reroute:I', 'RescuAR-MLD:D', 'RescuAR-HybridRouting:D', 'RescuAR-Build:I', 'RescuAR-Replay:I',
    'RescuAR-ARProfile:I', '*:S')
Read-Adb (@('logcat', '-d', '-v', 'threadtime') + $filters) |
    Set-Content -LiteralPath $logPath -Encoding utf8
& (Join-Path $PSScriptRoot 'Export-NavigationReplay.ps1') -LogPaths $logPath -OutputDirectory $outputRoot
& (Join-Path $PSScriptRoot 'Export-ARPerformanceProfile.ps1') -LogPaths $logPath -TrialManifestPath $trialPath -OutputDirectory $outputRoot
Write-Output "Navigation evidence saved: $outputRoot"
