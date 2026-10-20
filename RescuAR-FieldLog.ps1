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

    # Normal operation requires only -Start. When left as Auto, the script
    # records the best install-method classification available from Android's
    # package-installer metadata.
    [ValidateSet("Auto", "VisualStudio", "ADB", "PlayInternal", "Sideload", "Other")]
    [string]$InstallMethod = "Auto",

    # Include only RescuAR and ADB-shell markers to avoid vendor system-log
    # floods. All-process capture remains available by omitting this switch.
    [switch]$AppOnly,

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

function Invoke-AdbShellResult {
    param([string]$Serial, [string]$Command)

    # Windows PowerShell treats native stderr as error records. Capture those
    # records without losing the device exit code or the actual error message.
    $ErrorActionPreference = "Continue"
    $output = @(& adb -s $Serial shell $Command 2>&1 | ForEach-Object { $_.ToString() })
    return [PSCustomObject]@{
        ExitCode = $LASTEXITCODE
        Output = (($output -join "`n").Trim())
    }
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

    # Do not use `cmd package list packages -i` here. On Samsung devices with
    # Secure Folder or another secondary profile, that command can scan an
    # inaccessible user and abort with SecurityException. The package dump is
    # already scoped to RescuAR and normally contains the same installer data.
    $installer = "unknown"
    if ($packageDump -match '(?m)^\s*installerPackageName[=:]\s*([^\s\r\n]+)') {
        $installerCandidate = $Matches[1].Trim()
        if ($installerCandidate -and $installerCandidate -notin @("null", "none")) {
            $installer = $installerCandidate
        }
    }

    return [PSCustomObject]@{
        PackageName = $PackageName
        UserId = $(if ($packageDump -match '(?m)^\s*(?:userId|appId)=(\d+)') { $Matches[1] } else { "" })
        VersionName = $versionName
        VersionCode = $versionCode
        Installer = $installer
        PackagePaths = $packagePaths
        ArtifactHashes = $artifactHashes
    }
}

function Resolve-InstallMethod {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RequestedMethod,

        [string]$Installer
    )

    if ($RequestedMethod -ne "Auto") {
        return $RequestedMethod
    }

    switch -Regex ($Installer) {
        '^com\.android\.vending$' {
            return "PlayInternalOrStore"
        }
        '^com\.android\.shell$' {
            # Android records both command-line ADB installs and Visual Studio
            # deployments as shell-driven installs, so they cannot be
            # distinguished reliably without asking the operator.
            return "ADBOrVisualStudio"
        }
        '(?i)(packageinstaller|permissioncontroller)' {
            return "Sideload"
        }
        default {
            return "Unknown"
        }
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

    # PowerShell's current location can differ from the process working
    # directory used by .NET APIs. Resolve once so ZipFile.OpenRead receives an
    # unambiguous absolute path.
    $resolvedDestinationDirectory = (Resolve-Path -LiteralPath $DestinationDirectory).Path

    $collectEvidence = Get-InstalledPackageEvidence -Serial $Serial
    $artifacts = @()

    for ($index = 0; $index -lt $collectEvidence.PackagePaths.Count; $index++) {
        $remotePath = $collectEvidence.PackagePaths[$index]
        $temporaryApk = Join-Path $resolvedDestinationDirectory "__installed_$index.apk"

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
                $buildProfile = @{}
                $profileEntry = $archive.GetEntry('assets/rescuar-build-profile.txt')
                if ($null -ne $profileEntry) {
                    $profileReader = [IO.StreamReader]::new($profileEntry.Open())
                    try {
                        foreach ($profileLine in ($profileReader.ReadToEnd() -split "`r?`n")) {
                            if ($profileLine -match '^([^=]+)=(.*)$') {
                                $buildProfile[$Matches[1].Trim()] = $Matches[2].Trim()
                            }
                        }
                    }
                    finally { $profileReader.Dispose() }
                }
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
                    BuildProfile = $buildProfile
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

function Get-RotationThresholdKB {
    param([int64]$RequestedKB, [string]$LimitsText)
    if ($LimitsText -match '(?m)^Max file size\s+(\d+)\s+') {
        $limitKB = [int64][Math]::Floor([double]$Matches[1] / 1024.0)
        $RequestedKB = [Math]::Min($RequestedKB, $limitKB - 16L)
    }
    if ($RequestedKB -lt 1) {
        throw "The inherited file-size limit leaves no safe Logcat rotation space."
    }
    return $RequestedKB
}

function Get-FieldLogMarkerPriority {
    param([ValidateSet("V", "D", "I", "W", "E", "F")][string]$MinimumPriority)

    # Android can discard V/D messages in liblog before Logcat receives them.
    # Markers must meet both that default INFO floor and the capture filter.
    if ($MinimumPriority -in @("V", "D")) { return "i" }
    return $MinimumPriority.ToLowerInvariant()
}

function Test-FieldLogMarkerLine {
    param([string]$Line, [string]$Marker)

    # Require a real threadtime record, not an ADB command echo or a marker
    # from another session whose name happens to begin with the same text.
    $pattern = '^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+\s+\d+\s+\d+\s+[VDIWEF]\s+RescuAR-FieldLog\s*:\s*' +
        [regex]::Escape($Marker) + '\s*$'
    return ($Line -cmatch $pattern)
}

function Get-FieldLogMarkerCommand {
    param(
        [ValidatePattern('^RESCUAR_FIELDLOG_(VERIFY|COMPLETE)_\d{8}_\d{6}$')][string]$Marker,
        [string]$MinimumPriority
    )

    $priority = Get-FieldLogMarkerPriority -MinimumPriority $MinimumPriority
    # Toybox log truncates messages >=1024 bytes and returns exit 1, including
    # when it still writes the truncated text. Leave room for the prefix/index.
    $padding = 'x' * 512
    # Native Logcat -f uses buffered stdio. Sleeping or running sync does not
    # flush that userspace buffer. Emit the marker FIRST, then >32 KiB of
    # distinct same-UID/priority records so quiet AppOnly captures persist it.
    # This also persists preceding app events before collection stops Logcat.
    return (@"
log -p $priority -t RescuAR-FieldLog '$Marker' || exit `$?
rescuar_flush_index=0
while [ "`$rescuar_flush_index" -lt 64 ]; do
    log -p $priority -t RescuAR-FieldLog "RESCUAR_FIELDLOG_FLUSH_`${rescuar_flush_index}_$padding" || exit `$?
    rescuar_flush_index=`$((rescuar_flush_index + 1))
done
"@ -replace "`r`n", "`n")
}

function Confirm-FieldLogMarker {
    param(
        [string]$Serial, [string]$BaseLogPath, [string]$Marker,
        [string]$MinimumPriority, [string]$PidText, [int]$TimeoutSeconds = 8
    )

    $command = Get-FieldLogMarkerCommand -Marker $Marker -MinimumPriority $MinimumPriority
    $emission = Invoke-AdbShellResult -Serial $Serial -Command $command
    if ($emission.ExitCode -ne 0) {
        throw "Marker/flush write failed (ADB/device exit $($emission.ExitCode)): $($emission.Output)"
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        # Check the active file first; a freshly written marker usually lives
        # there. Avoid scanning up to 2 GiB of rotated logs on every poll.
        foreach ($searchPaths in @("'$BaseLogPath'", "'$BaseLogPath'.*")) {
            $lines = Invoke-AdbShellText -Serial $Serial `
                -Command "grep -h -F '$Marker' $searchPaths 2>/dev/null"
            foreach ($line in ($lines -split "`n")) {
                if (Test-FieldLogMarkerLine -Line $line -Marker $Marker) {
                    return (Test-RemotePidAlive -Serial $Serial -PidText $PidText)
                }
            }
        }
        if (-not (Test-RemotePidAlive -Serial $Serial -PidText $PidText)) { return $false }
        if ((Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Get-FieldLogDiagnostics {
    param([string]$Serial, [string]$BaseLogPath, [string]$Marker, [string]$UidFilter, [string]$PidText)

    $uidArgument = if ($UidFilter) { "--uid '$UidFilter'" } else { "" }
    $processCommand = if ($PidText -match '^\d+$') {
        "cat '/proc/$PidText/cmdline' 2>/dev/null | tr '\000' ' '; echo"
    } else { "echo 'Detached logger not started (preflight)'" }
    $command = @"
echo 'Shell identity:'
id
echo 'Logger command line:'
$processCommand
echo 'Native log file sizes:'
ls -ln '$BaseLogPath' '$BaseLogPath'.* 2>/dev/null
echo 'Marker persisted in native log files:'
grep -h -F '$Marker' '$BaseLogPath' '$BaseLogPath'.* 2>/dev/null
echo 'Marker in main buffer without UID filter:'
logcat -d -b main -v threadtime -v year 'RescuAR-FieldLog:V' '*:S' | grep -F '$Marker'
echo 'Marker in main buffer with capture UID filter:'
logcat -d -b main -v threadtime -v year $uidArgument 'RescuAR-FieldLog:V' '*:S' | grep -F '$Marker'
echo 'Android write-side log priorities:'
getprop log.tag.RescuAR-FieldLog
getprop persist.log.tag.RescuAR-FieldLog
getprop log.tag
getprop persist.log.tag
"@
    $result = Invoke-AdbShellResult -Serial $Serial -Command ($command -replace "`r`n", "`n")
    return "Capture UID filter: $UidFilter`n$($result.Output)"
}

function Get-FieldCaptureQuality {
    param([string[]]$Paths, [string]$Session, [bool]$LoggerAliveBeforeStop, [bool]$CompletionVerified = $true)
    $startMarker = "RESCUAR_FIELDLOG_VERIFY_$Session"
    $endMarker = "RESCUAR_FIELDLOG_COMPLETE_$Session"
    $startFound = $false
    $endFound = $false
    $appEvents = 0L
    $navigationEvents = 0L
    $firstTime = $null
    $lastTime = $null
    foreach ($path in $Paths) {
        foreach ($line in [IO.File]::ReadLines($path)) {
            if ($line -match '\s[VDIWEF]\s+RescuAR-FieldLog\s*:') {
                if (Test-FieldLogMarkerLine -Line $line -Marker $startMarker) { $startFound = $true }
                if (Test-FieldLogMarkerLine -Line $line -Marker $endMarker) { $endFound = $true }
            }
            if ($line -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)') {
                $timestamp = $Matches[1]
                if ($null -eq $firstTime -or $timestamp -lt $firstTime) { $firstTime = $timestamp }
                if ($null -eq $lastTime -or $timestamp -gt $lastTime) { $lastTime = $timestamp }
            }
            if ($line -match '\s[VDIWEF]\s+RescuAR-(?!FieldLog\b)[^:]+:') { $appEvents++ }
            if ($line -match '\s[VDIWEF]\s+RescuAR-(?:AStar|HybridRouting|MLD|Routing|RouteProgress|SafeZone|GPS)\s*:') {
                $navigationEvents++
            }
        }
    }
    return [PSCustomObject]@{
        Session = $Session
        LoggerAliveBeforeStop = $LoggerAliveBeforeStop
        CompletionVerified = $CompletionVerified
        StartMarkerFound = $startFound
        EndMarkerFound = $endFound
        FirstLogTime = $firstTime
        LastLogTime = $lastTime
        ApplicationEvents = $appEvents
        NavigationEvents = $navigationEvents
        CaptureComplete = ($LoggerAliveBeforeStop -and $CompletionVerified -and $startFound -and $endFound)
        HasNavigationEvidence = ($navigationEvents -gt 0)
    }
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

function Test-RemoteLogcatIdentity {
    param([string]$Serial, [string]$PidText)

    if ($PidText -notmatch '^\d+$') { return $false }
    $session = Read-RemoteFile -Serial $Serial -Path $RemoteSessionFile
    if ($session -notmatch '^\d{8}_\d{6}$') { return $false }
    $commandLine = Invoke-AdbShellText -Serial $Serial `
        -Command "cat '/proc/$PidText/cmdline' 2>/dev/null | tr '\000' '\n'"
    $arguments = @($commandLine -split "`n")
    if ($arguments[0] -notmatch '^(?:.*/)?logcat$') { return $false }
    for ($index = 1; $index -lt $arguments.Count - 1; $index++) {
        if ($arguments[$index] -eq '-f' -and
            $arguments[$index + 1] -ceq "$RemoteRoot/$session/logcat") { return $true }
    }
    return $false
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
        [string]$Serial,
        [int]$TimeoutSeconds = 4
    )

    $pidText = Read-RemoteFile -Serial $Serial -Path $RemoteLogcatPidFile

    if ($pidText -match '^\d+$') {
        if (Test-RemotePidAlive -Serial $Serial -PidText $pidText) {
            # A PID can be reused after a reboot or process exit. Never signal
            # an unrelated process just because the old PID file still exists.
            if (-not (Test-RemoteLogcatIdentity -Serial $Serial -PidText $pidText)) {
                throw "PID $pidText is alive but is not the registered RescuAR Logcat capture; it was not stopped."
            }
            Write-Host "Stopping on-device Logcat (PID $pidText)..."
            $stopResult = Invoke-AdbShellResult -Serial $Serial -Command "kill -TERM $pidText"
            if ($stopResult.ExitCode -ne 0 -and (Test-RemotePidAlive -Serial $Serial -PidText $pidText)) {
                throw "Could not stop Logcat PID $pidText (exit $($stopResult.ExitCode)): $($stopResult.Output)"
            }

            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            do {
                Start-Sleep -Milliseconds 250
                $alive = Test-RemotePidAlive -Serial $Serial -PidText $pidText
            }
            while ($alive -and (Get-Date) -lt $deadline)

            if ($alive) {
                Write-Warning "Logcat did not stop after SIGTERM; forcing it to stop."
                # Recheck ownership before the second signal as well.
                if (-not (Test-RemoteLogcatIdentity -Serial $Serial -PidText $pidText)) {
                    throw "Logcat PID $pidText changed identity during shutdown; no further signal was sent."
                }
                $stopResult = Invoke-AdbShellResult -Serial $Serial -Command "kill -KILL $pidText"
                Start-Sleep -Milliseconds 500
                if (Test-RemotePidAlive -Serial $Serial -PidText $pidText) {
                    throw "Logcat PID $pidText remains alive after shutdown (exit $($stopResult.ExitCode)): $($stopResult.Output)"
                }
            }
        }
    }

    $stateResult = Invoke-AdbShellResult -Serial $Serial `
        -Command "rm -f '$RemoteLogcatPidFile' && echo 'STOPPED' > '$RemoteStatusFile'"
    if ($stateResult.ExitCode -ne 0) {
        throw "Capture stopped, but logger state could not be updated: $($stateResult.Output)"
    }
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
    if ($session -notmatch '^\d{8}_\d{6}$') {
        throw "Unexpected on-device capture session name; capture paths were not used."
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
        [string]$Serial,
        [string]$UidFilter = "",
        [string]$MinimumPriority = "D"
    )

    $probe = "$RemoteRoot/.rotation-probe"

    & adb -s $Serial shell "rm -f '$probe' '$probe'.*" | Out-Null

    # Check actual marker delivery, not just an empty file being created.
    # Unlike the long-lived capture, -d exits normally and flushes stdio.
    $marker = "RESCUAR_FIELDLOG_PROBE_$([guid]::NewGuid().ToString('N'))"
    $priority = Get-FieldLogMarkerPriority -MinimumPriority $MinimumPriority
    $uidArgument = if ($UidFilter) { "--uid '$UidFilter'" } else { "" }
    $probeResult = Invoke-AdbShellResult -Serial $Serial -Command (
        "log -p $priority -t RescuAR-FieldLog '$marker' || exit `$?; " +
        "logcat -d -b main -v threadtime -v year -f '$probe' -r 64 -n 2 $uidArgument '*:$MinimumPriority'")
    try {
        $probeLines = Invoke-AdbShellText -Serial $Serial `
            -Command "grep -h -F '$marker' '$probe' '$probe'.* 2>/dev/null"
        $markerFound = @($probeLines -split "`n" | Where-Object {
            Test-FieldLogMarkerLine -Line $_ -Marker $marker
        }).Count -gt 0
        if ($probeResult.ExitCode -ne 0 -or -not $markerFound) {
            $diagnostics = Get-FieldLogDiagnostics -Serial $Serial -BaseLogPath $probe `
                -Marker $marker -UidFilter $UidFilter -PidText "self"
            throw "Native Logcat rotation/filter probe did not capture its marker (exit $($probeResult.ExitCode)). $($probeResult.Output)`n$diagnostics"
        }
    }
    finally { & adb -s $Serial shell "rm -f '$probe' '$probe'.*" | Out-Null }
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
    $setsidPath = Invoke-AdbShellText -Serial $serial -Command "command -v setsid"
    $detachCommand = if ($setsidPath) { "nohup setsid sh" } else { "nohup sh" }
    if (-not $setsidPath) {
        Write-Warning "setsid is unavailable; capture uses nohup only. Check logger status after reconnecting USB."
    }

    $installedPackage = Get-InstalledPackageEvidence -Serial $serial
    if ($installedPackage.PackagePaths.Count -eq 0) {
        throw "$PackageName is not installed; field evidence cannot be tied to an application artifact."
    }
    $uidFilter = ""
    if ($AppOnly) {
        if ($installedPackage.UserId -notmatch '^\d+$') {
            throw "The RescuAR UID is unavailable; retry without -AppOnly."
        }
        $androidUser = Invoke-AdbShellText -Serial $serial -Command "am get-current-user"
        $shellUid = Invoke-AdbShellText -Serial $serial -Command "id -u"
        if ($androidUser -notmatch '^\d+$' -or $shellUid -notmatch '^\d+$') {
            throw "The active Android user/shell UID is unavailable; retry without -AppOnly."
        }
        $appUid = [int64]$installedPackage.UserId
        if ($appUid -lt 100000) { $appUid += [int64]$androidUser * 100000L }
        $uidFilter = "$appUid,$shellUid"
    }
    Write-Host "Checking native Logcat file rotation/filter support..."
    Test-NativeLogcatRotationSupport -Serial $serial -UidFilter $uidFilter -MinimumPriority $MinimumPriority

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
    $loggerLimits = Invoke-AdbShellText -Serial $serial -Command "cat /proc/self/limits"
    $rotateKB = Get-RotationThresholdKB -RequestedKB $rotateKB -LimitsText $loggerLimits
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

    $resolvedInstallMethod = Resolve-InstallMethod `
        -RequestedMethod $InstallMethod `
        -Installer $installedPackage.Installer

    & adb -s $serial shell "echo 'Session=$session' > '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'Device=$model' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'Android=$androidVersion' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'API=$sdkVersion' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'PackageName=$($installedPackage.PackageName)' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'PackageVersionName=$($installedPackage.VersionName)' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'PackageVersionCode=$($installedPackage.VersionCode)' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'PackageInstaller=$($installedPackage.Installer)' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'InstallMethod=$resolvedInstallMethod' >> '$remoteMeta'" | Out-Null
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
    & adb -s $serial shell "echo 'UidFilter=$uidFilter' >> '$remoteMeta'" | Out-Null
    & adb -s $serial shell "echo 'DetachMethod=$detachCommand' >> '$remoteMeta'" | Out-Null

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
UID_FILTER="$7"

BASE="$SESSION_DIR/logcat"

echo "$$" > "$PID_FILE"
echo "STARTING" > "$STATUS_FILE"
cat "/proc/$$/limits" > "$SESSION_DIR/logger-limits.txt"
set --
if [ -n "$UID_FILTER" ]; then
    set -- --uid "$UID_FILTER"
fi

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
    "$@" \
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
    Write-Host "Marker priority: $(Get-FieldLogMarkerPriority $MinimumPriority) (buffered-file verification)"
    Write-Host "Capture UID filter: $(if ($uidFilter) { $uidFilter } else { 'all' })"
    Write-Host "Install method: $resolvedInstallMethod"

    $launchCommand =
        "$detachCommand '$RemoteWrapper' " +
        "'$remoteSessionDir' " +
        "'$RemoteLogcatPidFile' " +
        "'$RemoteStatusFile' " +
        "'$rotateKB' " +
        "'$RotateCount' " +
        "'$MinimumPriority' " +
        "'$uidFilter' " +
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

    # Verify the actual file after flushing a quiet native Logcat capture.
    $marker = "RESCUAR_FIELDLOG_VERIFY_$session"
    try {
        $markerCaptured = Confirm-FieldLogMarker -Serial $serial -BaseLogPath $remoteBaseLog `
            -Marker $marker -MinimumPriority $MinimumPriority -PidText $pidText
        if (-not $markerCaptured) {
            throw "The startup marker did not reach the native log files after the flush burst."
        }
        $capture = Get-CaptureStatus -Serial $serial
        if (-not $capture.LogcatAlive) { throw "Logcat exited after writing the startup marker." }
    }
    catch {
        $failureMessage = $_.Exception.Message
        $launcherError = Read-RemoteFile -Serial $serial -Path $RemoteLauncherLog
        $statusText = Read-RemoteFile -Serial $serial -Path $RemoteStatusFile
        $diagnostics = Get-FieldLogDiagnostics -Serial $serial -BaseLogPath $remoteBaseLog `
            -Marker $marker -UidFilter $uidFilter -PidText $pidText
        $failureDetails = "Session: $session`n$failureMessage`nStatus before stop: $statusText`nLauncher output: $launcherError`n$diagnostics"
        try {
            New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
            $diagnosticPath = Join-Path $OutputDirectory "RescuAR_FieldLog_Failure_$session.txt"
            $failureDetails | Set-Content -LiteralPath $diagnosticPath -Encoding UTF8
            Write-Host "Failure diagnostics saved to: $((Resolve-Path -LiteralPath $diagnosticPath).Path)"
        }
        catch { Write-Warning "Could not save failure diagnostics locally: $($_.Exception.Message)" }
        try { Stop-RemoteCapture -Serial $serial }
        catch { throw "$failureDetails`nShutdown also failed: $($_.Exception.Message)" }
        # Keep the pointer and session files available for -Status/-Collect.
        throw $failureDetails
    }

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
    if ($session -notmatch '^\d{8}_\d{6}$') {
        throw "Unexpected on-device capture session name; collection paths were not used."
    }

    $remoteSessionDir = "$RemoteRoot/$session"
    $captureBeforeStop = Get-CaptureStatus -Serial $serial
    $preStopMB = [Math]::Round([double]$captureBeforeStop.Bytes / 1MB, 2)

    Write-Host "Session: $session"
    Write-Host "Logger status: $($captureBeforeStop.StatusText)"
    Write-Host "Logcat alive before stop: $($captureBeforeStop.LogcatAlive)"
    Write-Host "Captured before stop: $preStopMB MB in $($captureBeforeStop.Files) file(s)"

    $launcherOutput = Read-RemoteFile -Serial $serial -Path $RemoteLauncherLog
    $loggerStatus = Read-RemoteFile -Serial $serial -Path $RemoteStatusFile
    $completionMarker = "RESCUAR_FIELDLOG_COMPLETE_$session"
    $completionMeta = Read-RemoteFile -Serial $serial -Path "$remoteSessionDir/session.txt"
    $completionMinimum = if ($completionMeta -match '(?m)^MinimumPriority=([VDIWEF])') {
        $Matches[1]
    } else { "I" }
    $completionCaptured = $false
    if ($captureBeforeStop.LogcatAlive) {
        try {
            $completionCaptured = Confirm-FieldLogMarker -Serial $serial -BaseLogPath "$remoteSessionDir/logcat" `
                -Marker $completionMarker -MinimumPriority $completionMinimum -PidText $captureBeforeStop.LogcatPid
            if (-not $completionCaptured) { throw "The completion marker was not persisted before stopping Logcat." }
        }
        catch {
            Write-Warning "Completion verification failed; collecting the available evidence: $($_.Exception.Message)"
        }
    }
    Stop-RemoteCapture -Serial $serial
    & adb -s $serial shell "sync" | Out-Null
    Start-Sleep -Milliseconds 750

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
    $localSessionDir = Join-Path $resolvedOutputDirectory "RescuAR_FieldTest_$session"

    if (Test-Path -LiteralPath $localSessionDir) {
        $retryTimestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $localSessionDir = Join-Path `
            $resolvedOutputDirectory `
            "RescuAR_FieldTest_${session}_retry_$retryTimestamp"

        Write-Warning "A previous collection directory already exists. This retry will be saved separately at: $localSessionDir"
    }

    New-Item -ItemType Directory -Path $localSessionDir -Force | Out-Null
    $launcherOutput | Set-Content -LiteralPath (Join-Path $localSessionDir "launcher.log") -Encoding UTF8
    $loggerStatus | Set-Content -LiteralPath (Join-Path $localSessionDir "logger-status.txt") -Encoding UTF8

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
    $quality = Get-FieldCaptureQuality -Paths @($logFiles.FullName) -Session $session `
        -LoggerAliveBeforeStop $captureBeforeStop.LogcatAlive -CompletionVerified $completionCaptured
    $quality | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $localSessionDir "CaptureQuality.json") -Encoding UTF8
    if (-not $quality.CaptureComplete -or -not $quality.HasNavigationEvidence) {
        Write-Warning "Field evidence is incomplete: loggerAlive=$($quality.LoggerAliveBeforeStop), completionVerified=$($quality.CompletionVerified), startMarker=$($quality.StartMarkerFound), endMarker=$($quality.EndMarkerFound), navigationEvents=$($quality.NavigationEvents). Last captured event: $($quality.LastLogTime)."
    }

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
Logger Alive     : $($quality.LoggerAliveBeforeStop)
Completion Check : $($quality.CompletionVerified)
Capture Complete : $($quality.CaptureComplete)
First Log Event  : $($quality.FirstLogTime)
Last Log Event   : $($quality.LastLogTime)
App Events       : $($quality.ApplicationEvents)
Navigation Events: $($quality.NavigationEvents)
Segment Check    : $(if ($oversizedSegments.Count -eq 0) { "PASS" } else { "WARNING - $($oversizedSegments.Count) oversized segment(s)" })
Capture Location : $RemoteRoot

ADB Version
-----------
$(adb version)
"@ | Set-Content -Path $infoFile -Encoding UTF8

    Write-Host ""
    Write-Host $(if ($quality.CaptureComplete -and $quality.HasNavigationEvidence) {
        "COLLECTION VERIFIED: complete capture with navigation events."
    } else { "COLLECTION SAVED: incomplete field evidence; see CaptureQuality.json and launcher.log." })
    Write-Host "Files: $($logFiles.Count)"
    Write-Host "Log data: $totalMB MB"
    Write-Host "Saved to: $localSessionDir"

    # The session is no longer active after collection. Clear the pointer even
    # when the phone-side files are retained as a backup.
    & adb -s $serial shell "rm -f '$RemoteSessionFile' '$RemoteLogcatPidFile'" | Out-Null

    if ($DeleteAfterPull -and $quality.CaptureComplete -and $quality.HasNavigationEvidence) {
        Write-Host "Deleting the collected phone-side session..."
        & adb -s $serial shell "rm -rf '$remoteSessionDir'" | Out-Null
        & adb -s $serial shell "rm -f '$RemoteStatusFile' '$RemoteLauncherLog'" | Out-Null
    }
    else {
        Write-Host "The original phone-side session directory has been kept as a backup."
    }

    if (-not $quality.CaptureComplete -or -not $quality.HasNavigationEvidence) { exit 2 }
    exit 0
}
