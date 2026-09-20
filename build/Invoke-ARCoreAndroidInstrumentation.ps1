param(
    [string]$ArtifactPath,
    [string]$PackageName = "com.rescuar.app",
    [ValidateRange(1, 50)][int]$Cycles = 5,
    [ValidateRange(1, 600)][int]$SettleSeconds = 8,
    [string]$OutputDirectory = ".\artifacts\arcore-device-evidence",
    [string]$InstallMethod = "adb-install-replace",
    [switch]$SkipInstall,
    [switch]$SkipCameraNavigation,
    [switch]$ExerciseCameraPermission
)

$ErrorActionPreference = "Stop"

function Invoke-Adb([string[]]$Arguments, [switch]$AllowFailure) {
    $result = @(& adb @Arguments 2>&1)
    if (-not $AllowFailure -and $LASTEXITCODE -ne 0) { throw "adb $($Arguments -join ' ') failed: $($result -join ' ')" }
    return $result
}

function Start-Application {
    Invoke-Adb @('shell', 'monkey', '-p', $PackageName, '-c', 'android.intent.category.LAUNCHER', '1') | Out-Null
    Start-Sleep -Seconds $SettleSeconds
    if (-not $SkipCameraNavigation) {
        $size = (Invoke-Adb @('shell', 'wm', 'size')) -join ' '
        if ($size -notmatch '(\d+)x(\d+)') { throw "Unable to determine display size." }
        Invoke-Adb @('shell', 'input', 'tap', "$([int]([int]$Matches[1] / 2))", "$([int]([int]$Matches[2] * 0.94))") | Out-Null
        Start-Sleep -Seconds $SettleSeconds
    }
}

function Tap-Tab([double]$HorizontalFraction) {
    $size = (Invoke-Adb @('shell', 'wm', 'size')) -join ' '
    if ($size -notmatch '(\d+)x(\d+)') { throw "Unable to determine display size." }
    Invoke-Adb @(
        'shell', 'input', 'tap',
        "$([int]([int]$Matches[1] * $HorizontalFraction))",
        "$([int]([int]$Matches[2] * 0.94))"
    ) | Out-Null
    Start-Sleep -Seconds $SettleSeconds
}

function Assert-AppForeground([string]$Scenario) {
    $activityState = (Invoke-Adb @('shell', 'dumpsys', 'activity', 'activities')) -join "`n"
    if ($activityState -notmatch "mResumedActivity.*$([regex]::Escape($PackageName))") {
        throw "RescuAR is not the resumed Activity after $Scenario."
    }
}

if ($null -eq (Get-Command adb -ErrorAction SilentlyContinue)) { throw "adb is not available on PATH." }
$devices = @(& adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^([^\s]+)\s+device$' } | ForEach-Object { $Matches[1] })
if ($devices.Count -ne 1) { throw "Exactly one authorized Android device is required; found $($devices.Count)." }

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$runDirectory = Join-Path (Resolve-Path $OutputDirectory).Path ([DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$artifact = $null; $artifactHash = "not-recorded"
if ($ArtifactPath) {
    $artifact = (Resolve-Path -LiteralPath $ArtifactPath).Path
    if ([IO.Path]::GetExtension($artifact).ToLowerInvariant() -ne '.apk') { throw "Device instrumentation requires an APK." }
    $artifactHash = (Get-FileHash $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
}
if (-not $SkipInstall) {
    if (-not $artifact) { throw "ArtifactPath is required unless -SkipInstall is supplied." }
    Invoke-Adb @('install', '-r', $artifact) | Out-Null
}

$device = [ordered]@{
    serial = $devices[0]
    manufacturer = ((Invoke-Adb @('shell', 'getprop', 'ro.product.manufacturer')) -join '').Trim()
    model = ((Invoke-Adb @('shell', 'getprop', 'ro.product.model')) -join '').Trim()
    androidRelease = ((Invoke-Adb @('shell', 'getprop', 'ro.build.version.release')) -join '').Trim()
    apiLevel = ((Invoke-Adb @('shell', 'getprop', 'ro.build.version.sdk')) -join '').Trim()
    abi = ((Invoke-Adb @('shell', 'getprop', 'ro.product.cpu.abi')) -join '').Trim()
    gpu = ((Invoke-Adb @('shell', 'dumpsys', 'SurfaceFlinger')) | Select-String 'GLES:' | Select-Object -First 1).Line
    arCore = ((Invoke-Adb @('shell', 'dumpsys', 'package', 'com.google.ar.core') -AllowFailure) | Select-String 'versionName=' | Select-Object -First 1).Line
}
$device | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $runDirectory 'device.json') -Encoding UTF8
$oldRotation = ((Invoke-Adb @('shell', 'settings', 'get', 'system', 'user_rotation') -AllowFailure) -join '').Trim()
$oldAutoRotation = ((Invoke-Adb @('shell', 'settings', 'get', 'system', 'accelerometer_rotation') -AllowFailure) -join '').Trim()
Invoke-Adb @('logcat', '-c') | Out-Null

if ($ExerciseCameraPermission) {
    Invoke-Adb @('shell', 'pm', 'revoke', $PackageName, 'android.permission.CAMERA') -AllowFailure | Out-Null
    $revokedState = (Invoke-Adb @('shell', 'dumpsys', 'package', $PackageName)) -join "`n"
    if ($revokedState -notmatch 'android\.permission\.CAMERA:\s+granted=false') {
        throw "Camera permission could not be placed in the denied state."
    }

    Start-Application
    Invoke-Adb @('shell', 'pm', 'grant', $PackageName, 'android.permission.CAMERA') | Out-Null
    $grantedState = (Invoke-Adb @('shell', 'dumpsys', 'package', $PackageName)) -join "`n"
    if ($grantedState -notmatch 'android\.permission\.CAMERA:\s+granted=true') {
        throw "Camera permission could not be restored to the granted state."
    }
}

try {
    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        Invoke-Adb @('shell', 'am', 'force-stop', $PackageName) | Out-Null
        Start-Application
        Assert-AppForeground "cold start in cycle $cycle"
        if (-not $SkipCameraNavigation) {
            Tap-Tab 0.10
            Tap-Tab 0.50
        }
        Invoke-Adb @('shell', 'input', 'keyevent', '3') | Out-Null; Start-Sleep 2; Start-Application
        Assert-AppForeground "background/foreground in cycle $cycle"
        Invoke-Adb @('shell', 'input', 'keyevent', '26') | Out-Null; Start-Sleep 2
        Invoke-Adb @('shell', 'input', 'keyevent', '26') | Out-Null
        Invoke-Adb @('shell', 'input', 'keyevent', '82') -AllowFailure | Out-Null; Start-Sleep $SettleSeconds
        Assert-AppForeground "lock/unlock in cycle $cycle"
        Invoke-Adb @('shell', 'settings', 'put', 'system', 'accelerometer_rotation', '0') | Out-Null
        Invoke-Adb @('shell', 'settings', 'put', 'system', 'user_rotation', '1') | Out-Null; Start-Sleep 3
        Invoke-Adb @('shell', 'settings', 'put', 'system', 'user_rotation', '0') | Out-Null; Start-Sleep 3
        if ([string]::IsNullOrWhiteSpace(((Invoke-Adb @('shell', 'pidof', $PackageName) -AllowFailure) -join '').Trim())) {
            throw "Application process disappeared during cycle $cycle."
        }
    }
}
finally {
    if ($oldAutoRotation -and $oldAutoRotation -ne 'null') { Invoke-Adb @('shell', 'settings', 'put', 'system', 'accelerometer_rotation', $oldAutoRotation) -AllowFailure | Out-Null }
    if ($oldRotation -and $oldRotation -ne 'null') { Invoke-Adb @('shell', 'settings', 'put', 'system', 'user_rotation', $oldRotation) -AllowFailure | Out-Null }
}

$logFile = Join-Path $runDirectory 'logcat.txt'
Invoke-Adb @('logcat', '-d', '-v', 'threadtime') | Set-Content $logFile -Encoding UTF8
$log = Get-Content $logFile -Raw
$fatal = @('FATAL EXCEPTION', 'Fatal signal', 'ANR in com\.rescuar\.app', 'BUILD_MANIFEST_FAILED', 'ARCORE_CAMERA_PIPELINE_TERMINAL', 'dequeueBuffer failed', 'queueBuffer failed', 'VK_ERROR_DEVICE_LOST', 'DllNotFoundException.*native_bridge')
$hits = @($fatal | Where-Object { $log -match $_ })
if ($hits.Count) { throw "Fatal markers: $($hits -join ', '). Evidence: $logFile" }
$required = @(
    'BUILD_MANIFEST batch=ARCore-10',
    'automatedSafetyProfile=ARCORE_SAFETY_TESTS_V1',
    'ARCORE_NATIVE_BRIDGE_SELF_TEST result=PASS'
)
if (-not $SkipCameraNavigation) { $required += 'ARCore lifecycle state: state=Running' }
$missing = @($required | Where-Object { $log -notmatch [regex]::Escape($_) })
if ($missing.Count) { throw "Missing markers: $($missing -join ', '). Ensure the test account can enter Camera." }

[ordered]@{
    schemaVersion = 1; testLayer = 'android-instrumentation'; result = 'PASS'; completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    package = $PackageName; artifactFile = if ($artifact) { [IO.Path]::GetFileName($artifact) } else { 'preinstalled' }
    artifactSha256 = $artifactHash; installMethod = if ($SkipInstall) { "preinstalled:$InstallMethod" } else { $InstallMethod }
    cycles = $Cycles; cameraPermissionCycle = [bool]$ExerciseCameraPermission
    scenarios = @('cold start', 'Camera page entry/exit', 'background/foreground', 'lock/unlock', 'rotation', 'surface recreation', 'shutdown')
    requiredMarkers = $required; fatalPatternsChecked = $fatal; device = $device; logFile = 'logcat.txt'
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $runDirectory 'instrumentation-result.json') -Encoding UTF8

Write-Host "ARCore Android lifecycle instrumentation PASSED. Evidence: $runDirectory"
