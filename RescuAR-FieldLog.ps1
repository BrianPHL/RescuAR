param(
    [Parameter(Mandatory = $true, ParameterSetName = "Start")]
    [switch]$Start,

    [Parameter(Mandatory = $true, ParameterSetName = "Collect")]
    [switch]$Collect,

    [Parameter(Mandatory = $true, ParameterSetName = "Status")]
    [switch]$Status,

    [string]$OutputDirectory = ".\Logs",

    # Hard upper target for each collected Logcat file on the phone.
    # Native rotation uses a small internal safety margin below this value.
    [ValidateRange(1, 64)]
    [int]$RotateMB = 2,

    # Number of completed rotated files Logcat keeps, in addition to the
    # currently-active file. At 2 MB this default retains roughly 2 GB.
    # This is intentionally large, but not absurdly large: Android's native
    # rotation walks the configured rotation slots every time a file rotates.
    [ValidateRange(8, 4096)]
    [int]$RotateCount = 1024,

    [ValidateSet("V", "D", "I", "W", "E", "F")]
    [string]$MinimumPriority = "D",

    [Parameter(Mandatory = $true, ParameterSetName = "Start")]
    [ValidateSet("VisualStudio", "ADB", "PlayInternal", "Sideload", "Other")]
    [string]$InstallMethod,

    [switch]$DeleteAfterPull
)

$ErrorActionPreference = "Stop"

# The ADB shell user can write here, and capture continues after the USB cable
# is unplugged because Logcat itself runs on the Android device.
$RemoteRoot = "/data/local/tmp/RescuARFieldLogs"
$RemoteSessionFile = "$RemoteRoot/current.session"
$RemoteLogcatPidFile = "$RemoteRoot/logcat.pid"
$RemoteStatusFile = "$RemoteRoot/logger.status"
$RemoteLauncherLog = "$RemoteRoot/launcher.log"
$RemoteWrapper = "$RemoteRoot/rescuar-fieldlog-wrapper.sh"
$PackageName = "com.rescuar.app"

function Get-DeviceSerial {
    if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
        throw "ADB was not found in PATH. Install/update Android SDK Platform-Tools and ensure adb.exe is in PATH."
    }

    $devices = @(
        & adb devices |
        Select-Object -Skip 1 |
        Where-Object { $_ -match "\S+\s+device$" }
    )

    if ($devices.Count -eq 0) {
        & adb devices
        throw "No authorized Android device was detected. Connect the phone, enable USB debugging, and accept the authorization prompt."
    }

    if ($devices.Count -gt 1) {
        & adb devices
        throw "More than one Android device/emulator is connected. Disconnect the others first."
    }

    return (($devices[0] -split "\s+")[0])
}

function Invoke-AdbShellText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial,

        [Parameter(Mandatory = $true)]
        [string]$Command
    )

    $output = @(& adb -s $Serial shell $Command 2>$null)

    if ($null -eq $output -or $output.Count -eq 0) {
        return ""
    }

    return (($output -join "`n").Trim())
}

function Get-AndroidProperty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial,

        [Parameter(Mandatory = $true)]
        [string]$Property
    )

    return Invoke-AdbShellText -Serial $Serial -Command "getprop $Property"
}

function Get-InstalledPackageEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial
    )

    $pathOutput = Invoke-AdbShellText `
        -Serial $Serial `
        -Command "pm path $PackageName"

    $packagePaths = @(
        $pathOutput -split "`n" |
        ForEach-Object { $_.Trim() } |
        ForEach-Object {
            if ($_ -match '^package:(.+\.apk)$') {
                $Matches[1]
            }
        }
    )

    $artifactHashes = @()
    foreach ($packagePath in $packagePaths) {
        $hashLine = Invoke-AdbShellText `
            -Serial $Serial `
            -Command "sha256sum '$packagePath' 2>/dev/null"

        $hash = if ($hashLine -match '^([0-9a-fA-F]{64})\s') {
            $Matches[1].ToLowerInvariant()
        }
        else {
            "unavailable"
        }

        $artifactHashes += "$packagePath|$hash"
    }

    $packageDump = Invoke-AdbShellText `
        -Serial $Serial `
        -Command "dumpsys package $PackageName"

    $versionName = if ($packageDump -match '(?m)^\s*versionName=([^\r\n]+)') {
        $Matches[1].Trim()
    }
    else {
        "unavailable"
    }

    $versionCode = if ($packageDump -match '(?m)^\s*versionCode=(\d+)') {
        $Matches[1]
    }
    else {
        "unavailable"
    }

    $installerOutput = Invoke-AdbShellText `
        -Serial $Serial `
        -Command "cmd package list packages -i $PackageName"

    $installer = if ($installerOutput -match 'installer=([^\s]+)') {
        $Matches[1]
    }
    else {
        "unknown"
    }

    return [PSCustomObject]@{
        PackageName = $PackageName
        VersionName = $versionName
        VersionCode = $versionCode
        Installer = $installer
        PackagePaths = $packagePaths
        ArtifactHashes = $artifactHashes
    }
}

function Export-InstalledArtifactManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial,

        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$StartEvidence
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $collectEvidence = Get-InstalledPackageEvidence -Serial $Serial
    $artifacts = @()

    for ($index = 0; $index -lt $collectEvidence.PackagePaths.Count; $index++) {
        $remotePath = $collectEvidence.PackagePaths[$index]
        $temporaryApk = Join-Path $DestinationDirectory "__installed_$index.apk"

        try {
            & adb -s $Serial pull $remotePath $temporaryApk | Out-Null
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temporaryApk)) {
                throw "Could not pull installed package split '$remotePath'."
            }

            $apkSha256 = (Get-FileHash -LiteralPath $temporaryApk -Algorithm SHA256).Hash.ToLowerInvariant()
            $archive = [System.IO.Compression.ZipFile]::OpenRead($temporaryApk)
            try {
                $nativeEntries = @(
                    $archive.Entries |
                    Where-Object { $_.FullName -eq 'lib/arm64-v8a/libnative_bridge.so' }
                )

                $nativeSha256 = "absent"
                if ($nativeEntries.Count -eq 1) {
                    $entryStream = $nativeEntries[0].Open()
                    try {
                        $sha256 = [System.Security.Cryptography.SHA256]::Create()
                        try {
                            $nativeHash = $sha256.ComputeHash($entryStream)
                            $nativeSha256 = ([System.BitConverter]::ToString($nativeHash) -replace '-', '').ToLowerInvariant()
                        }
                        finally {
                            $sha256.Dispose()
                        }
                    }
                    finally {
                        $entryStream.Dispose()
                    }
                }

                $artifacts += [PSCustomObject]@{
                    RemotePath = $remotePath
                    ApkSha256 = $apkSha256
                    NativeBridgeEntryCount = $nativeEntries.Count
                    NativeBridgeSha256 = $nativeSha256
                }
            }
            finally {
                $archive.Dispose()
            }
        }
        finally {
            Remove-Item -LiteralPath $temporaryApk -Force -ErrorAction SilentlyContinue
        }
    }

    $manifest = [ordered]@{
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        PackageName = $collectEvidence.PackageName
        VersionName = $collectEvidence.VersionName
        VersionCode = $collectEvidence.VersionCode
        Installer = $collectEvidence.Installer
        StartArtifactHashes = @($StartEvidence)
        CollectionArtifactHashes = @($collectEvidence.ArtifactHashes)
        InstalledArtifacts = $artifacts
        NativeBridgePresent = @($artifacts | Where-Object { $_.NativeBridgeEntryCount -eq 1 }).Count -eq 1
    }

    $manifestPath = Join-Path $DestinationDirectory "InstalledArtifactManifest.json"
    $manifest |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $manifestPath -Encoding UTF8

    if (-not $manifest.NativeBridgePresent) {
        throw "Installed package verification did not find exactly one ARM64 libnative_bridge.so entry. The phone-side logs were preserved."
    }

    return $manifestPath
}

function Read-RemoteFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return Invoke-AdbShellText `
        -Serial $Serial `
        -Command "cat '$Path' 2>/dev/null"
}

function Test-RemotePidAlive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial,

        [string]$PidText
    )

    if ($PidText -notmatch '^\d+$') {
        return $false
    }

    $result = Invoke-AdbShellText `
        -Serial $Serial `
        -Command "kill -0 $PidText 2>/dev/null && echo alive"

    return ($result -eq "alive")
}

function Wait-ForRemoteFileContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [int]$TimeoutSeconds = 8
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        $value = Read-RemoteFile -Serial $Serial -Path $Path
        if ($value) {
            return $value
        }

        Start-Sleep -Milliseconds 250
    }
    while ((Get-Date) -lt $deadline)

    return ""
}

function Stop-RemoteCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial
    )

    $pidText = Read-RemoteFile -Serial $Serial -Path $RemoteLogcatPidFile

    if ($pidText -match '^\d+$') {
        if (Test-RemotePidAlive -Serial $Serial -PidText $pidText) {
            Write-Host "Stopping on-device Logcat (PID $pidText)..."
            & adb -s $Serial shell "kill -TERM $pidText 2>/dev/null" | Out-Null

            $deadline = (Get-Date).AddSeconds(4)
            do {
                Start-Sleep -Milliseconds 250
                $alive = Test-RemotePidAlive -Serial $Serial -PidText $pidText
            }
            while ($alive -and (Get-Date) -lt $deadline)

            if ($alive) {
                Write-Warning "Logcat did not stop after SIGTERM; forcing it to stop."
                & adb -s $Serial shell "kill -KILL $pidText 2>/dev/null" | Out-Null
                Start-Sleep -Milliseconds 500
            }
        }
    }

    & adb -s $Serial shell "rm -f '$RemoteLogcatPidFile'" | Out-Null
    & adb -s $Serial shell "echo 'STOPPED' > '$RemoteStatusFile'" | Out-Null
}

function Get-CaptureStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial
    )

    $session = Read-RemoteFile -Serial $Serial -Path $RemoteSessionFile
    $pidText = Read-RemoteFile -Serial $Serial -Path $RemoteLogcatPidFile
    $statusText = Read-RemoteFile -Serial $Serial -Path $RemoteStatusFile

    if (-not $session) {
        return [PSCustomObject]@{
            Session = ""
            LogcatPid = $pidText
            LogcatAlive = $false
            StatusText = $statusText
            Bytes = 0L
            Files = 0
        }
    }

    $sessionDir = "$RemoteRoot/$session"

    $kbText = Invoke-AdbShellText `
        -Serial $Serial `
        -Command "du -sk '$sessionDir' 2>/dev/null | awk '{print `$1}'"

    # Native Logcat files are named 'logcat' and 'logcat.NNNN'.
    $fileCountText = Invoke-AdbShellText `
        -Serial $Serial `
        -Command "find '$sessionDir' -maxdepth 1 -type f -name 'logcat*' 2>/dev/null | wc -l"

    $bytes = 0L
    if ($kbText -match '^\d+$') {
        $bytes = [int64]$kbText * 1024L
    }

    $files = 0
    if ($fileCountText -match '^\d+$') {
        $files = [int]$fileCountText
    }

    return [PSCustomObject]@{
        Session = $session
        LogcatPid = $pidText
        LogcatAlive = (Test-RemotePidAlive -Serial $Serial -PidText $pidText)
        StatusText = $statusText
        Bytes = $bytes
        Files = $files
    }
}

function Test-NativeLogcatRotationSupport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial
    )

    $probe = "$RemoteRoot/.rotation-probe"

    & adb -s $Serial shell "rm -f '$probe' '$probe'.*" | Out-Null

    # A real invocation is more reliable than parsing vendor-specific --help
    # text. -d makes it exit after dumping the current main buffer.
    & adb -s $Serial shell "logcat -d -b main -v threadtime -v year -f '$probe' -r 64 -n 2 '*:I'" | Out-Null
    $probeExit = $LASTEXITCODE

    $probeExists = Invoke-AdbShellText `
        -Serial $Serial `
        -Command "test -f '$probe' && echo yes"

    & adb -s $Serial shell "rm -f '$probe' '$probe'.*" | Out-Null

    if ($probeExit -ne 0 -or $probeExists -ne "yes") {
        throw "This device's Logcat does not appear to support native -f/-r/-n file rotation from the ADB shell."
    }
}

$serial = Get-DeviceSerial

if ($Status) {
    Write-Host ""
    Write-Host "=== RescuAR Field Log: STATUS ==="
    Write-Host "Device: $serial"

    $capture = Get-CaptureStatus -Serial $serial

    if (-not $capture.Session) {
        Write-Host "No active field-log session is registered on the phone."
        exit 0
    }

    $mb = [Math]::Round([double]$capture.Bytes / 1MB, 2)

    Write-Host "Session: $($capture.Session)"
    Write-Host "Logcat PID: $($capture.LogcatPid)  Alive: $($capture.LogcatAlive)"
    Write-Host "Logger status: $($capture.StatusText)"
    Write-Host "Log files: $($capture.Files)"
    Write-Host "Captured size: $mb MB"

    if (-not $capture.LogcatAlive) {
        Write-Warning "The on-device Logcat process is not running. Existing files can still be collected with -Collect."
        exit 2
    }

    exit 0
}

if ($Start) {
    Write-Host ""
    Write-Host "=== RescuAR Field Log: START ==="
    Write-Host "Device: $serial"

    & adb -s $serial shell "mkdir -p '$RemoteRoot'" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the on-device logger directory."
    }

    # Stop only the process from a previous RescuAR logger session. Old session
    # directories are intentionally left untouched until explicitly collected/
    # deleted.
    Stop-RemoteCapture -Serial $serial
    & adb -s $serial shell "rm -f '$RemoteSessionFile' '$RemoteLauncherLog'" | Out-Null

    $nohupPath = Invoke-AdbShellText -Serial $serial -Command "command -v nohup"
    if (-not $nohupPath) {
        throw "This Android build does not expose 'nohup' to the ADB shell, so detached capture cannot be guaranteed after USB disconnection."
    }

    Write-Host "Checking native Logcat file rotation support..."
    Test-NativeLogcatRotationSupport -Serial $serial

    $session = Get-Date -Format "yyyyMMdd_HHmmss"
    $remoteSessionDir = "$RemoteRoot/$session"
    $remoteMeta = "$remoteSessionDir/session.txt"
    $remoteBaseLog = "$remoteSessionDir/logcat"

    # Logcat rotates only after it finishes writing the record that crosses the
    # threshold. Keep a safety margin below the requested hard ceiling so a
    # completed line cannot make a segment exceed RotateMB MiB. Android Logcat
    # records are bounded, and 16 KiB provides ample headroom.
    $segmentCeilingKB = [int64]$RotateMB * 1024L
    $rotationSafetyKB = 16L
    $rotateKB = $segmentCeilingKB - $rotationSafetyKB
    if ($rotateKB -lt 1) {
        throw "Calculated Logcat rotation size is invalid."
    }

    $effectiveSegmentMB = [Math]::Round([double]$rotateKB / 1024.0, 3)
    $approxRetentionMB = [Math]::Floor(([double]$rotateKB * ([int64]$RotateCount + 1L)) / 1024.0)

    & adb -s $serial shell "mkdir -p '$remoteSessionDir'" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create '$remoteSessionDir'."
    }

    Write-Host "Clearing all Android Logcat buffers..."
    & adb -s $serial shell "logcat -b all -c" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to clear all Logcat buffers."
    }

    # Confirm the clear command completed before the detached capture starts.
    # The capture itself also uses -T 1, so even if a vendor buffer immediately
    # receives new system records, Logcat begins at the newest available record
    # instead of replaying historical buffer contents.
    Start-Sleep -Milliseconds 250

    $model = Get-AndroidProperty -Serial $serial -Property "ro.product.model"
    $androidVersion = Get-AndroidProperty -Serial $serial -Property "ro.build.version.release"
    $sdkVersion = Get-AndroidProperty -Serial $serial -Property "ro.build.version.sdk"
    $supportedAbis = Get-AndroidProperty -Serial $serial -Property "ro.product.cpu.abilist"
    $installedPackage = Get-InstalledPackageEvidence -Serial $serial

    if ($installedPackage.PackagePaths.Count -eq 0) {
        throw "$PackageName is not installed; field evidence cannot be tied to an application artifact."
    }

    & adb -s $serial shell "echo 'Session=$session' > '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'Device=$model' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'Android=$androidVersion' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'API=$sdkVersion' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'PackageName=$($installedPackage.PackageName)' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'PackageVersionName=$($installedPackage.VersionName)' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'PackageVersionCode=$($installedPackage.VersionCode)' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'PackageInstaller=$($installedPackage.Installer)' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'InstallMethod=$InstallMethod' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'SupportedAbis=$supportedAbis' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'InstalledArtifactHashes=$($installedPackage.ArtifactHashes -join ';')' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'MinimumPriority=$MinimumPriority' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'SegmentCeilingMB=$RotateMB' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'RotationThresholdKB=$rotateKB' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'RotationSafetyKB=$rotationSafetyKB' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'SegmentMB=$RotateMB' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'RotateCount=$RotateCount' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'ApproxRetentionMB=$approxRetentionMB' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'CaptureMode=Native_Logcat_File_Rotation' >> '$remoteMeta'" | Out-Null

    # The wrapper immediately execs Logcat. Because exec replaces the shell,
    # the PID written here remains the actual long-lived Logcat PID. This avoids
    # the detached pipeline/PID ambiguity that affected the previous logger.
    $wrapperContent = @'
#!/system/bin/sh

SESSION_DIR="$1"
PID_FILE="$2"
STATUS_FILE="$3"
ROTATE_KB="$4"
ROTATE_COUNT="$5"
PRIORITY="$6"

BASE="$SESSION_DIR/logcat"

echo "$$" > "$PID_FILE"
echo "STARTING" > "$STATUS_FILE"

# Mark RUNNING immediately before exec. The host performs an independent PID
# check and writes a Logcat marker afterward, so a failed exec is still caught.
echo "RUNNING" > "$STATUS_FILE"

exec logcat \
    -b all \
    -v threadtime \
    -v year \
    -f "$BASE" \
    -r "$ROTATE_KB" \
    -n "$ROTATE_COUNT" \
    -T 1 \
    "*:$PRIORITY"
'@

    $tempWrapper = Join-Path ([System.IO.Path]::GetTempPath()) "rescuar-fieldlog-wrapper.sh"

    [System.IO.File]::WriteAllText(
        $tempWrapper,
        ($wrapperContent -replace "`r`n", "`n"),
        [System.Text.UTF8Encoding]::new($false))

    try {
        & adb -s $serial push $tempWrapper $RemoteWrapper | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to push the on-device logger wrapper."
        }
    }
    finally {
        Remove-Item -LiteralPath $tempWrapper -Force -ErrorAction SilentlyContinue
    }

    & adb -s $serial shell "chmod 700 '$RemoteWrapper'" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not make the on-device logger wrapper executable."
    }

    & adb -s $serial shell "rm -f '$RemoteLogcatPidFile' '$RemoteStatusFile' '$RemoteLauncherLog'" | Out-Null

    # Register the new session BEFORE startup verification. Get-CaptureStatus
    # depends on this pointer; the previous script registered it too late.
    & adb -s $serial shell "echo '$session' > '$RemoteSessionFile'" | Out-Null

    Write-Host "Starting detached on-device Logcat..."
    Write-Host "Storage: /data/local/tmp"
    Write-Host "Segment ceiling: $RotateMB MiB"
    Write-Host "Native rotation threshold: $rotateKB KiB (~$effectiveSegmentMB MiB)"
    Write-Host "Rotated files retained: $RotateCount (+ 1 active file)"
    Write-Host "Approximate maximum log retention: $approxRetentionMB MB"
    Write-Host "Minimum priority: $MinimumPriority"

    $launchCommand =
        "nohup sh '$RemoteWrapper' " +
        "'$remoteSessionDir' " +
        "'$RemoteLogcatPidFile' " +
        "'$RemoteStatusFile' " +
        "'$rotateKB' " +
        "'$RotateCount' " +
        "'$MinimumPriority' " +
        "> '$RemoteLauncherLog' 2>&1 < /dev/null &"

    & adb -s $serial shell $launchCommand | Out-Null

    $pidText = Wait-ForRemoteFileContent `
        -Serial $serial `
        -Path $RemoteLogcatPidFile `
        -TimeoutSeconds 8

    if ($pidText -notmatch '^\d+$') {
        $launcherError = Read-RemoteFile -Serial $serial -Path $RemoteLauncherLog
        $statusText = Read-RemoteFile -Serial $serial -Path $RemoteStatusFile
        & adb -s $serial shell "rm -f '$RemoteSessionFile'" | Out-Null
        throw "The on-device Logcat process did not initialize. Status='$statusText'. Launcher output: $launcherError"
    }

    Start-Sleep -Seconds 2

    if (-not (Test-RemotePidAlive -Serial $serial -PidText $pidText)) {
        $launcherError = Read-RemoteFile -Serial $serial -Path $RemoteLauncherLog
        $statusText = Read-RemoteFile -Serial $serial -Path $RemoteStatusFile
        & adb -s $serial shell "rm -f '$RemoteSessionFile'" | Out-Null
        throw "Logcat PID $pidText exited during startup. Status='$statusText'. Launcher output: $launcherError"
    }

    # Verify end-to-end capture. The marker priority follows the configured
    # minimum so W/E/F captures do not falsely fail verification.
    $priorityForLogCommand = $MinimumPriority.ToLowerInvariant()
    $marker = "RESCUAR_FIELDLOG_VERIFY_$session"
    & adb -s $serial shell "log -p $priorityForLogCommand -t RescuAR-FieldLog '$marker'" | Out-Null
    Start-Sleep -Seconds 3

    $markerCheck = Invoke-AdbShellText `
        -Serial $serial `
        -Command "grep -h -F '$marker' '$remoteBaseLog' '$remoteBaseLog'.* 2>/dev/null | tail -n 1"

    if (-not $markerCheck) {
        $launcherError = Read-RemoteFile -Serial $serial -Path $RemoteLauncherLog
        $statusText = Read-RemoteFile -Serial $serial -Path $RemoteStatusFile

        Stop-RemoteCapture -Serial $serial
        & adb -s $serial shell "rm -f '$RemoteSessionFile'" | Out-Null

        throw @"
Logcat PID verification passed, but the test marker did not reach the on-device log files.
Status: $statusText
Launcher output: $launcherError
"@
    }

    $capture = Get-CaptureStatus -Serial $serial
    $initialKB = [Math]::Round([double]$capture.Bytes / 1KB, 1)

    Write-Host ""
    Write-Host "LOGGER VERIFICATION PASSED."
    Write-Host "Logcat PID: $($capture.LogcatPid)"
    Write-Host "Session: $session"
    Write-Host "Log files present: $($capture.Files)"
    Write-Host "Initial captured size: $initialKB KB"
    Write-Host ""
    Write-Host "You may now unplug USB and perform the field test."
    Write-Host ""
    Write-Host "Optional status check before unplugging:"
    Write-Host "  .\RescuAR-FieldLog.ps1 -Status"
    Write-Host ""
    Write-Host "After reconnecting:"
    Write-Host "  .\RescuAR-FieldLog.ps1 -Collect"
    exit 0
}

if ($Collect) {
    Write-Host ""
    Write-Host "=== RescuAR Field Log: COLLECT ==="
    Write-Host "Device: $serial"

    $session = Read-RemoteFile -Serial $serial -Path $RemoteSessionFile

    if (-not $session) {
        throw "No active RescuAR field-log session was found on the phone."
    }

    $remoteSessionDir = "$RemoteRoot/$session"
    $captureBeforeStop = Get-CaptureStatus -Serial $serial
    $preStopMB = [Math]::Round([double]$captureBeforeStop.Bytes / 1MB, 2)

    Write-Host "Session: $session"
    Write-Host "Logger status: $($captureBeforeStop.StatusText)"
    Write-Host "Logcat alive before stop: $($captureBeforeStop.LogcatAlive)"
    Write-Host "Captured before stop: $preStopMB MB in $($captureBeforeStop.Files) file(s)"

    Stop-RemoteCapture -Serial $serial
    & adb -s $serial shell "sync" | Out-Null
    Start-Sleep -Milliseconds 750

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $localSessionDir = Join-Path $OutputDirectory "RescuAR_FieldTest_$session"

    if (Test-Path -LiteralPath $localSessionDir) {
        throw "The local collection directory already exists: $localSessionDir. Move/delete it first so an earlier field test cannot be mixed with this one."
    }

    New-Item -ItemType Directory -Path $localSessionDir -Force | Out-Null

    Write-Host "Pulling field logs..."
    & adb -s $serial pull "$remoteSessionDir/." "$localSessionDir"

    if ($LASTEXITCODE -ne 0) {
        Remove-Item -LiteralPath $localSessionDir -Recurse -Force -ErrorAction SilentlyContinue
        throw "ADB pull failed. The original files remain on the phone."
    }

    # Native Logcat rotation semantics:
    #   logcat.N (larger N) = older
    #   logcat.1             = newest completed rotation
    #   logcat               = current/newest file
    # Modern Android may zero-pad N depending on RotateCount.
    $allNativeLogs = @(
        Get-ChildItem -LiteralPath $localSessionDir -File |
        Where-Object { $_.Name -match '^logcat(?:\.\d+)?$' }
    )

    if ($allNativeLogs.Count -eq 0) {
        throw "Collection completed, but no native Logcat files were found. The phone-side session has been preserved."
    }

    $rotatedLogs = @(
        $allNativeLogs |
        Where-Object { $_.Name -match '^logcat\.(\d+)$' } |
        ForEach-Object {
            [PSCustomObject]@{
                File = $_
                Rotation = [int]([regex]::Match($_.Name, '^logcat\.(\d+)$').Groups[1].Value)
            }
        } |
        Sort-Object Rotation -Descending
    )

    $currentLog = @(
        $allNativeLogs |
        Where-Object { $_.Name -eq 'logcat' }
    )

    $orderedFiles = @($rotatedLogs | ForEach-Object { $_.File }) + @($currentLog)

    $digits = [Math]::Max(3, $orderedFiles.Count.ToString().Length)

    # Rename through temporary names first so no native filename can collide
    # with the requested numbered output names.
    for ($i = 0; $i -lt $orderedFiles.Count; $i++) {
        $tempName = "__rescuar_order_$($i.ToString('D6')).tmp"
        Rename-Item -LiteralPath $orderedFiles[$i].FullName -NewName $tempName
    }

    $tempOrdered = @(
        Get-ChildItem -LiteralPath $localSessionDir -File |
        Where-Object { $_.Name -match '^__rescuar_order_\d+\.tmp$' } |
        Sort-Object Name
    )

    for ($i = 0; $i -lt $tempOrdered.Count; $i++) {
        $sequenceText = ($i + 1).ToString("D$digits")
        Rename-Item `
            -LiteralPath $tempOrdered[$i].FullName `
            -NewName "logcat-$sequenceText.txt"
    }

    $logFiles = @(
        Get-ChildItem -LiteralPath $localSessionDir -File |
        Where-Object { $_.Name -match '^logcat-\d+\.txt$' } |
        Sort-Object Name
    )

    $totalBytes = ($logFiles | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $totalBytes) {
        $totalBytes = 0L
    }

    if ($totalBytes -lt 1024) {
        throw "Collected Logcat data is only $totalBytes bytes. The original phone-side session has been preserved."
    }

    $totalMB = [Math]::Round([double]$totalBytes / 1MB, 2)

    $manufacturer = Get-AndroidProperty -Serial $serial -Property "ro.product.manufacturer"
    $model = Get-AndroidProperty -Serial $serial -Property "ro.product.model"
    $androidVersion = Get-AndroidProperty -Serial $serial -Property "ro.build.version.release"
    $sdkVersion = Get-AndroidProperty -Serial $serial -Property "ro.build.version.sdk"
    $buildId = Get-AndroidProperty -Serial $serial -Property "ro.build.display.id"

    $sessionMetaPath = Join-Path $localSessionDir "session.txt"
    $capturedRotateMB = $RotateMB
    $capturedRotateKB = $null
    $capturedRotationSafetyKB = $null
    $capturedRotateCount = $RotateCount
    $capturedPriority = $MinimumPriority
    $capturedPackageName = $PackageName
    $capturedPackageVersionName = "unavailable"
    $capturedPackageVersionCode = "unavailable"
    $capturedPackageInstaller = "unknown"
    $capturedInstallMethod = "unavailable"
    $capturedSupportedAbis = "unavailable"
    $startArtifactHashes = @()

    if (Test-Path -LiteralPath $sessionMetaPath) {
        $metaLines = Get-Content -LiteralPath $sessionMetaPath -ErrorAction SilentlyContinue
        foreach ($line in $metaLines) {
            if ($line -match '^SegmentMB=(\d+)$') {
                $capturedRotateMB = [int]$Matches[1]
            }
            elseif ($line -match '^RotationThresholdKB=(\d+)$') {
                $capturedRotateKB = [int]$Matches[1]
            }
            elseif ($line -match '^RotationSafetyKB=(\d+)$') {
                $capturedRotationSafetyKB = [int]$Matches[1]
            }
            elseif ($line -match '^RotateCount=(\d+)$') {
                $capturedRotateCount = [int]$Matches[1]
            }
            elseif ($line -match '^MinimumPriority=([VDIWEF])$') {
                $capturedPriority = $Matches[1]
            }
            elseif ($line -match '^PackageName=(.+)$') {
                $capturedPackageName = $Matches[1]
            }
            elseif ($line -match '^PackageVersionName=(.+)$') {
                $capturedPackageVersionName = $Matches[1]
            }
            elseif ($line -match '^PackageVersionCode=(.+)$') {
                $capturedPackageVersionCode = $Matches[1]
            }
            elseif ($line -match '^PackageInstaller=(.+)$') {
                $capturedPackageInstaller = $Matches[1]
            }
            elseif ($line -match '^InstallMethod=(.+)$') {
                $capturedInstallMethod = $Matches[1]
            }
            elseif ($line -match '^SupportedAbis=(.+)$') {
                $capturedSupportedAbis = $Matches[1]
            }
            elseif ($line -match '^InstalledArtifactHashes=(.+)$') {
                $startArtifactHashes = @($Matches[1] -split ';')
            }
        }
    }

    Write-Host "Verifying the installed APK split set and native bridge..."
    $installedArtifactManifest = Export-InstalledArtifactManifest `
        -Serial $serial `
        -DestinationDirectory $localSessionDir `
        -StartEvidence $startArtifactHashes

    $segmentCeilingBytes = [int64]$capturedRotateMB * 1MB
    $largestSegmentBytes = ($logFiles | Measure-Object -Property Length -Maximum).Maximum
    if ($null -eq $largestSegmentBytes) {
        $largestSegmentBytes = 0L
    }

    $oversizedSegments = @(
        $logFiles | Where-Object { $_.Length -gt $segmentCeilingBytes }
    )

    if ($oversizedSegments.Count -gt 0) {
        $oversizedNames = ($oversizedSegments | ForEach-Object { $_.Name }) -join ", "
        Write-Warning "One or more collected segments exceeded the $capturedRotateMB MiB ceiling: $oversizedNames"
    }
    else {
        Write-Host "Segment-size verification passed: every log file is <= $capturedRotateMB MiB."
    }

    $infoFile = Join-Path $localSessionDir "DeviceInfo.txt"

    @"
RescuAR Field Test
==================

Session          : $session
Collected        : $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
ADB Serial       : $serial
Manufacturer     : $manufacturer
Device Model     : $model
Android Version  : $androidVersion
Android API      : $sdkVersion
Build ID         : $buildId
Package Name     : $capturedPackageName
Package Version  : $capturedPackageVersionName ($capturedPackageVersionCode)
Package Installer: $capturedPackageInstaller
Install Method   : $capturedInstallMethod
Supported ABIs   : $capturedSupportedAbis
Artifact Manifest: $([System.IO.Path]::GetFileName($installedArtifactManifest))
Capture Mode     : Native Logcat file rotation
Minimum Priority : $capturedPriority
Segment Ceiling  : $capturedRotateMB MiB
Rotation Threshold: $(if ($null -ne $capturedRotateKB) { "$capturedRotateKB KiB" } else { "not recorded" })
Rotation Safety  : $(if ($null -ne $capturedRotationSafetyKB) { "$capturedRotationSafetyKB KiB" } else { "not recorded" })
Rotation Count   : $capturedRotateCount
Log Files        : $($logFiles.Count)
Log Data         : $totalMB MB
Largest Segment  : $largestSegmentBytes bytes
Segment Check    : $(if ($oversizedSegments.Count -eq 0) { "PASS" } else { "WARNING - $($oversizedSegments.Count) oversized segment(s)" })
Capture Location : $RemoteRoot

ADB Version
-----------
$(adb version)
"@ | Set-Content -Path $infoFile -Encoding UTF8

    Write-Host ""
    Write-Host "COLLECTION VERIFIED."
    Write-Host "Files: $($logFiles.Count)"
    Write-Host "Log data: $totalMB MB"
    Write-Host "Saved to: $localSessionDir"

    # The session is no longer active after collection. Clear the pointer even
    # when the phone-side files are retained as a backup.
    & adb -s $serial shell "rm -f '$RemoteSessionFile' '$RemoteLogcatPidFile'" | Out-Null

    if ($DeleteAfterPull) {
        Write-Host "Deleting the collected phone-side session..."
        & adb -s $serial shell "rm -rf '$remoteSessionDir'" | Out-Null
        & adb -s $serial shell "rm -f '$RemoteStatusFile' '$RemoteLauncherLog'" | Out-Null
    }
    else {
        Write-Host "The original phone-side session directory has been kept as a backup."
    }

    exit 0
}
