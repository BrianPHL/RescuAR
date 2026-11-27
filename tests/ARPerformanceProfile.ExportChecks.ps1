$ErrorActionPreference = 'Stop'
$exporter = Join-Path $PSScriptRoot '../build/Export-ARPerformanceProfile.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('rescuar-ar-profile-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$captureId = '0123456789abcdef0123456789abcdef'
$apkHash = 'a' * 64
$passed = 0

function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:passed++
}

function Line([int]$Sequence, [string]$Kind, [long]$Time, [hashtable]$Values = @{}, [int]$Run = 1,
    [string]$Capture = $captureId) {
    $event = [ordered]@{ kind = $Kind; capture = $Capture; session = 3; graphics = 4;
        run = $Run; sequence = $Sequence; monotonicMs = $Time }
    foreach ($key in $Values.Keys) { $event[$key] = $Values[$key] }
    '11-27 09:00:00.123 100 101 I RescuAR-ARProfile: AR_PROFILE_V1 ' + ($event | ConvertTo-Json -Compress)
}

function Export([string]$Name, [string[]]$Lines) {
    $dir = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path $dir | Out-Null
    $log = Join-Path $dir 'logcat.txt'
    Set-Content -LiteralPath $log -Value $Lines -Encoding UTF8
    $manifest = Join-Path $dir 'trial.json'
    @{ trial = 'device-a'; model = 'test'; installedApkSha256 = $apkHash;
        postTrialSnapshot = @{ totalPssKb = 1234; totalRssKb = 2345; androidThermalStatus = 2 } } |
        ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding UTF8
    & $exporter -LogPaths $log -TrialManifestPath $manifest -OutputDirectory $dir | Out-Null
    Get-Content -LiteralPath (Join-Path $dir 'ar-performance-profile.json') -Raw | ConvertFrom-Json
}

try {
    $start = Line 1 'start' 1000
    $first = Line 2 'timings' 1137 @{ metric = 'firstCameraFrame'; samplesUs = @(137500); location = 'DO-NOT-COPY' }
    $cadence = Line 3 'timings' 1400 @{ metric = 'presentInterval'; samplesUs = @(12000, 34000, 16000, 50000, 18000) }
    $gaps = Line 4 'timings' 1400 @{ metric = 'cameraGap'; samplesUs = @(100000, 250000, 480000) }
    $nativeImport = Line 5 'timings' 1410 @{ metric = 'nativeImport'; samplesUs = @(2010, 3990) }
    $conversion = Line 6 'timings' 1420 @{ metric = 'conversionObserved'; samplesUs = @(6510, 7990) }
    $systemStart = Line 7 'system' 2000 @{ managedBytes = 1000; managedAllocatedBytes = 5000;
        gen0 = 1; gen1 = 0; gen2 = 1; thermal = 'Unknown'; batteryCelsius = $null; powerSaver = $false }
    $systemEnd = Line 8 'system' 4000 @{ managedBytes = 800; managedAllocatedBytes = 9000;
        gen0 = 3; gen1 = 1; gen2 = 1; thermal = 'Moderate'; batteryCelsius = 39.5; powerSaver = $true }
    $end = Line 9 'end' 4100
    $valid = Export 'valid' @($end, $start, $first, $cadence, $cadence, $gaps, $nativeImport, $conversion, $systemStart, $systemEnd)
    $run = $valid.runs[0]
    Check ($valid.coverage.records -eq 9 -and $valid.coverage.duplicates -eq 1 -and $run.completeThroughEnd) `
        'out-of-order logs and repeated records produce one complete run'
    Check ($run.timings.firstCameraFrame.p50Ms -eq 137.5 -and $run.timings.presentInterval.p50Ms -eq 18 -and
        $run.timings.presentInterval.p95Ms -eq 50 -and $run.timings.presentInterval.p99Ms -eq 50) `
        'first-camera latency and nearest-rank presentation percentiles preserve fractional milliseconds'
    Check ($run.timings.cameraGap.over100Ms -eq 2 -and $run.timings.cameraGap.over250Ms -eq 1) `
        'camera gaps have distinct strict reporting thresholds'
    Check ($run.timings.nativeImport.p95Ms -eq 3.99 -and $run.timings.conversionObserved.p95Ms -eq 7.99 -and
        $run.missingMeasurements -contains 'firstRouteOverlay') `
        'native import and conversion remain distinct, and a missing route measurement remains absent'
    Check ($run.system.managedChangeBytes -eq -200 -and $run.system.managedPeakBytes -eq 1000 -and
        $run.system.managedAllocationBytesPerSecond -eq 2000 -and ($run.system.gcCollections -join ',') -eq '2,1,0') `
        'runtime heap trend, allocation rate and GC counts use the observed interval'
    Check ($run.system.highestThermalSeverity -ceq 'Moderate' -and $run.system.thermalSamplesBySeverity.Unknown -eq 1 -and
        $run.system.peakBatteryCelsius -eq 39.5 -and $run.system.powerSaverSamples -eq 1) `
        'unknown thermal readings stay explicit, with measured thermal and battery samples reported separately'
    Check ($valid.candidate.installedApkSha256 -ceq $apkHash -and $valid.postTrialSnapshot.totalPssKb -eq 1234 -and
        (Get-Content -LiteralPath (Join-Path $testRoot 'valid/ar-performance-profile.json') -Raw) -notmatch 'DO-NOT-COPY') `
        'candidate identity and the post-trial snapshot survive without forwarding unrelated log fields'
    $conflict = Line 2 'timings' 1137 @{ metric = 'firstCameraFrame'; samplesUs = @(856500) }
    $badSample = Line 5 'timings' 2000 @{ metric = 'nativeImport'; samplesUs = @(-1) }
    $noise = $end -replace 'I RescuAR-ARProfile:', 'I adbd: echoed I RescuAR-ARProfile:'
    $partial = Export 'partial' @($start, $first, $conflict, $gaps, $badSample, $noise)
    Check ($partial.coverage.conflicting -eq 1 -and $partial.coverage.malformed -eq 1 -and
        $partial.runs[0].missingSequences -eq 2 -and -not $partial.runs[0].completeThroughEnd -and
        $null -eq $partial.runs[0].timings.firstCameraFrame) `
        'conflicts and invalid numeric data cannot make partial coverage appear complete'
    $otherCapture = 'a123456789abcdef0123456789abcdef'
    $separate = Export 'separate' @($start, $first, (Line 1 'start' 6000 @{} 2), (Line 1 'start' 8000 @{} 1 $otherCapture))
    Check ($separate.runs.Count -eq 3 -and $separate.runs[1].run -eq 2 -and $separate.runs[2].capture -ceq $otherCapture) `
        'a resumed run and process restart are kept separate when Android reuses a process ID'
    $empty = Export 'empty' @($noise, '11-27 09:00:00.000 100 101 I RescuAR-ARCore: fps=31')
    Check (-not $empty.coverage.hasProfileEvidence -and $empty.runs.Count -eq 0) `
        'older logs and wrapped ADB echoes report unavailable performance evidence'
    Write-Output "AR performance profile export checks passed: $passed"
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved).StartsWith('rescuar-ar-profile-checks-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
