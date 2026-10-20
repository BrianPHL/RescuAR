$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "../RescuAR-FieldLog.ps1"
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path -LiteralPath $scriptPath).Path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }

# Import helper definitions without running the script. Device-facing helpers
# are exercised only with the ADB stubs below, never a connected phone.
foreach ($name in @('Get-RotationThresholdKB', 'Get-FieldLogMarkerPriority', 'Test-FieldLogMarkerLine',
    'Get-FieldLogMarkerCommand', 'Confirm-FieldLogMarker', 'Test-NativeLogcatRotationSupport', 'Get-FieldCaptureQuality',
    'Test-RemoteLogcatIdentity', 'Stop-RemoteCapture')) {
    $definition = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) { throw "Missing function: $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}
$passed = 0
function Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAIL: $Description" }
    $script:passed++
    Write-Host "PASS: $Description"
}
Check ((Get-RotationThresholdKB 2032 'Max file size             1024000              1024000              bytes') -eq 984) `
    'rotation stays below a one-thousand-KiB inherited file-size limit'
Check ((Get-RotationThresholdKB 2032 'Max file size             unlimited             unlimited             bytes') -eq 2032) `
    'unlimited file-size limit retains the requested rotation size'
Check ((Get-RotationThresholdKB 496 'Max file size             1024000              1024000              bytes') -eq 496) `
    'a smaller requested segment size is preserved'
$smallLimitRejected = $false
try { Get-RotationThresholdKB 2032 'Max file size             1024                 1024                 bytes' | Out-Null }
catch { $smallLimitRejected = $true }
Check $smallLimitRejected 'an unusable file-size limit fails before field capture starts'

foreach ($priorityCase in @(
    @{ Capture = 'V'; Marker = 'i' },
    @{ Capture = 'D'; Marker = 'i' },
    @{ Capture = 'I'; Marker = 'i' },
    @{ Capture = 'W'; Marker = 'w' },
    @{ Capture = 'E'; Marker = 'e' },
    @{ Capture = 'F'; Marker = 'f' }
)) {
    Check ((Get-FieldLogMarkerPriority $priorityCase.Capture) -ceq $priorityCase.Marker) `
        "$($priorityCase.Capture) capture markers meet Android's INFO floor and the capture minimum"
}

$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ("rescuar-fieldlog-checks-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $testDirectory | Out-Null
try {
    $session = '20261012_093644'
    $first = Join-Path $testDirectory 'logcat-001.txt'
    $last = Join-Path $testDirectory 'logcat-002.txt'
    [IO.File]::WriteAllText($first, @"
2026-10-12 09:36:52.044 100 100 D RescuAR-FieldLog: RESCUAR_FIELDLOG_VERIFY_$session
2026-10-12 09:37:26.909 200 200 I SurfaceFlinger: system event
"@)
    [IO.File]::WriteAllText($last, @"
2026-10-12 09:54:10.000 300 300 W RescuAR-AStar: endpoint unmatched
2026-10-12 09:54:11.000 300 300 I RescuAR-SafeZone: ARRIVAL CANDIDATE
2026-10-12 09:55:00.000 100 100 D RescuAR-FieldLog: RESCUAR_FIELDLOG_COMPLETE_$session
"@)
    $incomplete = Get-FieldCaptureQuality @($first) $session $false
    Check (-not $incomplete.CaptureComplete -and -not $incomplete.HasNavigationEvidence -and
        $incomplete.LastLogTime -eq '2026-10-12 09:37:26.909') `
        'the supplied early-stop pattern is incomplete, despite a successful startup marker'
    $complete = Get-FieldCaptureQuality @($first, $last) $session $true
    Check ($complete.CaptureComplete -and $complete.HasNavigationEvidence -and
        $complete.NavigationEvents -eq 2 -and $complete.ApplicationEvents -eq 2) `
        'complete rotated capture contains actual app navigation events'
    $wrongSession = Get-FieldCaptureQuality @($first, $last) '20261011_100000' $true
    Check (-not $wrongSession.CaptureComplete) 'markers from a different capture cannot establish completeness'
    $deadLogger = Get-FieldCaptureQuality @($first, $last) $session $false
    Check (-not $deadLogger.CaptureComplete) 'a dead logger cannot report a complete field session'
    [IO.File]::WriteAllText($first, "2026-10-12 09:36:52.044 100 100 I adbd: shell log RESCUAR_FIELDLOG_VERIFY_$session RESCUAR_FIELDLOG_COMPLETE_$session")
    $echoedCommands = Get-FieldCaptureQuality @($first) $session $true
    Check (-not $echoedCommands.StartMarkerFound -and -not $echoedCommands.EndMarkerFound) `
        'ADB command echoes cannot substitute for captured completion markers'
    $markerPriority = (Get-FieldLogMarkerPriority 'D').ToUpperInvariant()
    [IO.File]::WriteAllText($first, @"
2026-10-12 09:36:52.044 100 100 $markerPriority RescuAR-FieldLog: RESCUAR_FIELDLOG_VERIFY_$session
2026-10-12 09:55:00.000 100 100 $markerPriority RescuAR-FieldLog: RESCUAR_FIELDLOG_COMPLETE_$session
"@)
    $infoMarkers = Get-FieldCaptureQuality @($first) $session $true
    Check ($infoMarkers.CaptureComplete -and $infoMarkers.ApplicationEvents -eq 0 -and
        -not $infoMarkers.HasNavigationEvidence) `
        'INFO markers establish a complete Debug capture without counting as navigation evidence'
    $failedCompletion = Get-FieldCaptureQuality @($first) $session $true $false
    Check ($failedCompletion.StartMarkerFound -and $failedCompletion.EndMarkerFound -and
        -not $failedCompletion.CompletionVerified -and -not $failedCompletion.CaptureComplete) `
        'captured marker text cannot override a failed completion verification'

    $marker = "RESCUAR_FIELDLOG_VERIFY_$session"
    $realMarkerLine = "2026-10-12 09:36:52.044 100 100 I RescuAR-FieldLog: $marker"
    Check (-not (Test-FieldLogMarkerLine "$realMarkerLine`_wrong_session" $marker)) `
        'a marker prefix cannot verify another session'
    Check (-not (Test-FieldLogMarkerLine "2026-10-12 09:36:52.044 100 100 I adbd: echoed I RescuAR-FieldLog: $marker" $marker)) `
        'a command echo containing a marker-like tag cannot pass startup verification'

    # Execute the generated POSIX-shell payload with a fake log command. This
    # checks quoting, message ordering and the actual burst without Android.
    $bashPath = if (Get-Command bash -ErrorAction SilentlyContinue) {
        (Get-Command bash).Source
    } elseif (Test-Path -LiteralPath 'C:\Program Files\Git\bin\bash.exe') {
        'C:\Program Files\Git\bin\bash.exe'
    } else { $null }
    if ($bashPath) {
        $shellPath = Join-Path $testDirectory 'marker-check.sh'
        # The helper generates ASCII only, so this byte limit matches Toybox's
        # strlen check. The previous unlimited stub missed the real failure.
        $fakeLog = @'
log() {
    if [ "${#5}" -ge 1024 ]; then
        printf 'log: log cut at 1024 bytes\n' >&2
        return 1
    fi
    printf '%s\t%s\t%s\n' "$2" "$4" "$5"
}
'@
        $command = Get-FieldLogMarkerCommand -Marker $marker -MinimumPriority 'D'
        [IO.File]::WriteAllText($shellPath, "$fakeLog`n$command`n", [Text.UTF8Encoding]::new($false))
        $records = @(& $bashPath --noprofile --norc --posix ($shellPath -replace '\\', '/'))
        Check ($LASTEXITCODE -eq 0 -and $records.Count -eq 65 -and
            ($records[0] -split "`t")[2] -ceq $marker) `
            'the shell emits the marker before 64 flush records within the Android message limit'
        Check (@($records | Select-Object -Skip 1 | Sort-Object -Unique).Count -eq 64) `
            'flush records are distinct so identical-message suppression cannot discard the burst'
        $messageSizes = @($records | ForEach-Object {
            [Text.Encoding]::UTF8.GetByteCount(($_ -split "`t", 3)[2])
        })
        Check (($messageSizes | Measure-Object -Maximum).Maximum -lt 1024 -and
            ($messageSizes | Measure-Object -Sum).Sum -gt 32768) `
            'every complete message is below 1024 bytes while the burst still exceeds 32 KiB'

        $memory = [IO.MemoryStream]::new()
        $writer = [IO.StreamWriter]::new($memory, [Text.UTF8Encoding]::new($false), 16384, $true)
        try {
            $firstRecord = ($records[0] -split "`t", 3)
            $writer.WriteLine("2026-10-12 09:36:52.044 100 100 $($firstRecord[0].ToUpperInvariant()) $($firstRecord[1]): $($firstRecord[2])")
            Check ($memory.Length -eq 0) 'a quiet marker remains buffered even though the logger is alive'
            foreach ($record in ($records | Select-Object -Skip 1)) {
                $fields = $record -split "`t", 3
                $writer.WriteLine("2026-10-12 09:36:52.045 100 100 $($fields[0].ToUpperInvariant()) $($fields[1]): $($fields[2])")
            }
            $persisted = [Text.Encoding]::UTF8.GetString($memory.ToArray())
            Check ($persisted.Contains($marker)) 'the burst persists the marker without closing or explicitly flushing the logger'

            $completionMarker = "RESCUAR_FIELDLOG_COMPLETE_$session"
            $completionCommand = Get-FieldLogMarkerCommand $completionMarker 'W'
            [IO.File]::WriteAllText($shellPath, "$fakeLog`n$completionCommand`n", [Text.UTF8Encoding]::new($false))
            $completionRecords = @(& $bashPath --noprofile --norc --posix ($shellPath -replace '\\', '/'))
            Check ($LASTEXITCODE -eq 0 -and $completionRecords.Count -eq 65 -and
                $completionRecords[0] -ceq "w`tRescuAR-FieldLog`t$completionMarker") `
                'completion respects the Warning capture priority and stays within Android message limits'
            $lastAppEvent = '2026-10-12 09:55:00.000 300 300 W RescuAR-RouteProgress: last app event'
            $writer.WriteLine($lastAppEvent)
            foreach ($record in $completionRecords) {
                $fields = $record -split "`t", 3
                $writer.WriteLine("2026-10-12 09:55:00.001 100 100 $($fields[0].ToUpperInvariant()) $($fields[1]): $($fields[2])")
            }
            $persisted = [Text.Encoding]::UTF8.GetString($memory.ToArray())
            Check ($persisted.Contains($lastAppEvent) -and $persisted.Contains($completionMarker)) `
                'completion persists preceding app events and its marker before logger shutdown'
        }
        finally { $writer.Dispose(); $memory.Dispose() }

        # Reproduce the supplied failure using the old padding length.
        $oversizedCommand = $command.Replace(('x' * 512), ('x' * 1024))
        [IO.File]::WriteAllText($shellPath, "$fakeLog`n$oversizedCommand`n", [Text.UTF8Encoding]::new($false))
        try {
            $ErrorActionPreference = 'Continue'
            $oversizedOutput = @(& $bashPath --noprofile --norc --posix ($shellPath -replace '\\', '/') 2>&1 |
                ForEach-Object { $_.ToString() })
            $oversizedExit = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = 'Stop' }
        Check ($oversizedExit -eq 1 -and ($oversizedOutput -join "`n").Contains('log cut at 1024 bytes') -and
            $oversizedOutput -ccontains "i`tRescuAR-FieldLog`t$marker") `
            'old oversized padding reproduces exit 1 after the marker was already emitted'

        [IO.File]::WriteAllText($shellPath, "log() { return 7; }`n$command`n", [Text.UTF8Encoding]::new($false))
        & $bashPath --noprofile --norc --posix ($shellPath -replace '\\', '/') | Out-Null
        Check ($LASTEXITCODE -eq 7) 'the shell preserves a failed marker emission exit code'
    }
    else { Write-Host 'SKIP: POSIX-shell payload and buffering model checks (Bash unavailable).' }

    # Isolate polling/error handling from real ADB and real device state.
    $script:emissionExit = 0
    $script:reads = 0
    $script:persistAfter = 3
    $script:loggerAlive = $true
    $script:probeHasMarker = $true
    $RemoteRoot = '/data/local/tmp/RescuARFieldLogs'
    $RemoteSessionFile = "$RemoteRoot/current.session"
    $RemoteLogcatPidFile = "$RemoteRoot/logcat.pid"
    $RemoteStatusFile = "$RemoteRoot/logger.status"
    $script:testSession = $session
    $script:processArguments = "logcat`n-b`nall`n-f`n$RemoteRoot/$session/logcat"
    $script:stopExit = 0
    $script:ignoreSignals = $false
    $script:deviceCommands = @()
    function adb { $global:LASTEXITCODE = 0 }
    function Invoke-AdbShellResult {
        param([string]$Serial, [string]$Command)
        $script:lastDeviceCommand = $Command
        $script:deviceCommands += $Command
        if ($Command -match '^kill -') {
            if ($script:stopExit -eq 0 -and -not $script:ignoreSignals) { $script:loggerAlive = $false }
            return [PSCustomObject]@{ ExitCode = $script:stopExit; Output = 'mocked signal output' }
        }
        [PSCustomObject]@{ ExitCode = $script:emissionExit; Output = 'mocked device output' }
    }
    function Invoke-AdbShellText {
        param([string]$Serial, [string]$Command)
        if ($Command -match '/proc/\d+/cmdline') { return $script:processArguments }
        $script:reads++
        if ($Command -match "'(RESCUAR_FIELDLOG_PROBE_[0-9a-f]+)'") {
            if ($script:probeHasMarker) { return "2026-10-12 09:36:52.044 100 100 I RescuAR-FieldLog: $($Matches[1])" }
            return ''
        }
        if ($script:reads -ge $script:persistAfter) { return $realMarkerLine }
        return ''
    }
    function Test-RemotePidAlive { param([string]$Serial, [string]$PidText); return $script:loggerAlive }
    function Read-RemoteFile {
        param([string]$Serial, [string]$Path)
        if ($Path -eq $RemoteSessionFile) { return $script:testSession }
        if ($Path -eq $RemoteLogcatPidFile) { return '123' }
        return ''
    }
    function Get-FieldLogDiagnostics { param($Serial, $BaseLogPath, $Marker, $UidFilter, $PidText); return 'mocked diagnostics' }

    Check (Confirm-FieldLogMarker -Serial 'fake' -BaseLogPath '/fake/logcat' -Marker $marker -MinimumPriority 'D' -PidText '123') `
        'startup polls until a delayed file write contains the exact marker'
    $script:reads = 0
    $script:persistAfter = 0
    Check ((Confirm-FieldLogMarker 'fake' '/fake/logcat' $marker 'D' '123') -and $script:reads -eq 1) `
        'an active-file marker avoids scanning the retained rotated logs'
    $script:reads = 0
    $script:emissionExit = 7
    $emissionRejected = $false
    try { Confirm-FieldLogMarker 'fake' '/fake/logcat' $marker 'D' '123' | Out-Null }
    catch { $emissionRejected = $_.Exception.Message -match 'exit 7' }
    Check ($emissionRejected -and $script:reads -eq 0) 'failed emission reports its exit code without waiting for a nonexistent marker'
    $script:emissionExit = 0
    $script:persistAfter = [int]::MaxValue
    Check (-not (Confirm-FieldLogMarker 'fake' '/fake/logcat' $marker 'D' '123' 0)) `
        'a live process with no persisted marker cannot pass verification'
    $script:loggerAlive = $false
    $script:persistAfter = 0
    Check (-not (Confirm-FieldLogMarker 'fake' '/fake/logcat' $marker 'D' '123' 0)) `
        'a persisted marker cannot verify a dead capture process'

    Test-NativeLogcatRotationSupport -Serial 'fake' -UidFilter '10123,2000' -MinimumPriority 'D'
    Check ($script:lastDeviceCommand -match "--uid '10123,2000'" -and
        $script:lastDeviceCommand -match 'log -p i' -and $script:lastDeviceCommand -match 'logcat -d') `
        'preflight verifies marker delivery using the actual UID filter and a normally flushed dump'
    $script:probeHasMarker = $false
    $emptyProbeRejected = $false
    try { Test-NativeLogcatRotationSupport -Serial 'fake' -UidFilter '10123,2000' }
    catch { $emptyProbeRejected = $_.Exception.Message -match 'did not capture its marker' }
    Check $emptyProbeRejected 'creating an empty probe file cannot establish rotation/filter support'

    Check (Test-RemoteLogcatIdentity 'fake' '123') 'shutdown recognizes the registered native Logcat command and file'
    $script:processArguments = "/system/bin/sh`n-f`n$RemoteRoot/$session/logcat"
    Check (-not (Test-RemoteLogcatIdentity 'fake' '123')) 'a reused PID belonging to another program is rejected'
    $script:processArguments = "logcat`n-f`n$RemoteRoot/20261011_100000/logcat"
    Check (-not (Test-RemoteLogcatIdentity 'fake' '123')) 'a different capture session cannot inherit the registered PID ownership'
    $script:loggerAlive = $true
    $script:deviceCommands = @()
    $unownedStopRejected = $false
    try { Stop-RemoteCapture 'fake' 0 }
    catch { $unownedStopRejected = $_.Exception.Message -match 'not the registered' }
    Check ($unownedStopRejected -and $script:deviceCommands.Count -eq 0) `
        'shutdown sends no signal or state change to an unrelated process'

    $script:processArguments = "logcat`n-b`nall`n-f`n$RemoteRoot/$session/logcat"
    Stop-RemoteCapture 'fake' 0
    Check (-not $script:loggerAlive -and $script:lastDeviceCommand.Contains("echo 'STOPPED'")) `
        'STOPPED is written only after the registered logger has exited'
    $script:loggerAlive = $true
    $script:stopExit = 1
    $script:deviceCommands = @()
    $failedSignalRejected = $false
    try { Stop-RemoteCapture 'fake' 0 }
    catch { $failedSignalRejected = $_.Exception.Message -match 'Could not stop' }
    Check ($failedSignalRejected -and $script:deviceCommands.Count -eq 1 -and
        -not $script:lastDeviceCommand.Contains('STOPPED')) `
        'a failed termination signal preserves logger state and prevents collection'
    $script:stopExit = 0
    $script:ignoreSignals = $true
    $script:deviceCommands = @()
    $persistentProcessRejected = $false
    try { Stop-RemoteCapture 'fake' 0 }
    catch { $persistentProcessRejected = $_.Exception.Message -match 'remains alive' }
    Check ($persistentProcessRejected -and $script:deviceCommands.Count -eq 2 -and
        -not $script:lastDeviceCommand.Contains('STOPPED')) `
        'an unsuccessful forced stop cannot report STOPPED or pull changing files'
}
finally {
    # Delete only the exact files created by this test, then the empty folder.
    Remove-Item -LiteralPath (Join-Path $testDirectory 'logcat-001.txt') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $testDirectory 'logcat-002.txt') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $testDirectory 'marker-check.sh') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $testDirectory -Force
}
Write-Host "$passed field-log regression checks passed."
