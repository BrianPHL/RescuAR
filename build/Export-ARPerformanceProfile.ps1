[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$LogPaths,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$TrialManifestPath
)

# Parse existing diagnostic logs. Never run an app, read device state, or copy
# navigation coordinates. Summaries describe observed samples and boundaries.
$ErrorActionPreference = 'Stop'
$metrics = @('firstCameraFrame', 'firstRouteOverlay', 'presentInterval', 'cameraGap',
    'frameWork', 'sceneDrawPresent', 'nativeImport', 'conversionObserved')
$thermalOrder = @('None', 'Light', 'Moderate', 'Severe', 'Critical', 'Emergency', 'Shutdown')
$pattern = '^(?:\d{4}-)?\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+\s+(?<pid>\d+)\s+\d+\s+[VDIWEF]\s+RescuAR-ARProfile\s*:\s+AR_PROFILE_V1(?:\s+(?<payload>.*))?$'

function Test-Integer($Value, [long]$Minimum = 0) {
    return ($Value -is [int] -or $Value -is [long]) -and $Value -ge $Minimum
}

function Get-TimingSummary([long[]]$Samples) {
    if ($Samples.Count -eq 0) { return $null }
    [Array]::Sort($Samples)
    return [ordered]@{
        samples = $Samples.Count
        minMs = $Samples[0] / 1000.0
        p50Ms = $Samples[[int][Math]::Ceiling($Samples.Count * 0.50) - 1] / 1000.0
        p95Ms = $Samples[[int][Math]::Ceiling($Samples.Count * 0.95) - 1] / 1000.0
        p99Ms = $Samples[[int][Math]::Ceiling($Samples.Count * 0.99) - 1] / 1000.0
        maxMs = $Samples[-1] / 1000.0
    }
}

function Get-SystemSummary([object[]]$Observations) {
    if ($Observations.Count -eq 0) { return $null }
    $first = $Observations[0]
    $last = $Observations[-1]
    $seconds = ($last.monotonicMs - $first.monotonicMs) / 1000.0
    $usableCounters = $seconds -gt 0 -and $last.managedAllocatedBytes -ge $first.managedAllocatedBytes -and
        $last.gen0 -ge $first.gen0 -and $last.gen1 -ge $first.gen1 -and $last.gen2 -ge $first.gen2
    $thermals = [ordered]@{}
    foreach ($severity in @('Unknown') + $thermalOrder) {
        $observed = @($Observations | Where-Object thermal -ceq $severity).Count
        if ($observed -gt 0) { $thermals[$severity] = $observed }
    }
    $temperatures = @($Observations | Where-Object { $null -ne $_.batteryCelsius })
    return [ordered]@{
        samples = $Observations.Count
        firstMonotonicMs = $first.monotonicMs
        lastMonotonicMs = $last.monotonicMs
        managedFirstBytes = $first.managedBytes
        managedLastBytes = $last.managedBytes
        managedPeakBytes = ($Observations | Measure-Object managedBytes -Maximum).Maximum
        managedChangeBytes = if ($seconds -gt 0) { $last.managedBytes - $first.managedBytes }
        managedAllocationBytesPerSecond = if ($usableCounters) {
            [Math]::Round(($last.managedAllocatedBytes - $first.managedAllocatedBytes) / $seconds, 1)
        }
        gcCollections = if ($usableCounters) {
            @($last.gen0 - $first.gen0; $last.gen1 - $first.gen1; $last.gen2 - $first.gen2)
        }
        thermalSamplesBySeverity = $thermals
        highestThermalSeverity = @($thermalOrder | Where-Object { $thermals.Contains($_) } | Select-Object -Last 1)[0]
        peakBatteryCelsius = if ($temperatures.Count -gt 0) {
            ($temperatures | Measure-Object batteryCelsius -Maximum).Maximum
        }
        powerSaverSamples = @($Observations | Where-Object powerSaver).Count
    }
}

$runs = @{}
$malformedRecords = 0
$duplicateRecords = 0
$conflictingRecords = 0
foreach ($path in $LogPaths) {
    foreach ($line in [IO.File]::ReadLines((Resolve-Path -LiteralPath $path).Path)) {
        $match = [regex]::Match($line, $pattern)
        if (-not $match.Success) { continue }
        $processId = $match.Groups['pid'].Value
        $payload = $match.Groups['payload'].Value
        try {
            if ($payload.Length -eq 0 -or $payload.Length -gt 3800) { throw 'invalid profile record length' }
            $event = ConvertFrom-Json -InputObject $payload -AsHashtable -Depth 5
            if ($event -isnot [Collections.IDictionary] -or -not (Test-Integer $event.session 1) -or
                -not (Test-Integer $event.graphics 1) -or -not (Test-Integer $event.run 1) -or
                -not (Test-Integer $event.monotonicMs) -or -not (Test-Integer $event.sequence 1) -or
                $event.capture -cnotmatch '^[0-9a-f]{32}$') { throw 'invalid profile identity' }
            if ($event.kind -ceq 'timings') {
                if ($event.metric -cnotin $metrics -or $event.samplesUs -isnot [Array] -or
                    $event.samplesUs.Count -lt 1 -or $event.samplesUs.Count -gt 120) { throw 'invalid timing batch' }
                foreach ($sample in $event.samplesUs) {
                    if (-not (Test-Integer $sample)) { throw 'invalid timing sample' }
                }
            }
            elseif ($event.kind -ceq 'system') {
                foreach ($counter in @('managedBytes', 'managedAllocatedBytes', 'gen0', 'gen1', 'gen2')) {
                    if (-not (Test-Integer $event[$counter])) { throw 'invalid system counter' }
                }
                if (($event.thermal -cnotin $thermalOrder -and $event.thermal -cne 'Unknown') -or
                    $event.powerSaver -isnot [bool] -or ($null -ne $event.batteryCelsius -and
                        ($event.batteryCelsius -isnot [ValueType] -or $event.batteryCelsius -is [bool] -or
                            -not [double]::IsFinite([double]$event.batteryCelsius)))) { throw 'invalid thermal observation' }
            }
            elseif ($event.kind -cnotin @('start', 'end')) { throw 'unknown profile record kind' }
        }
        catch { $malformedRecords++; continue }

        $runKey = "$($event.capture)/$processId/$($event.session)/$($event.graphics)/$($event.run)"
        if (-not $runs.ContainsKey($runKey)) {
            $runs[$runKey] = @{ ProcessId = [int]$processId; Capture = $event.capture; Session = $event.session;
                Graphics = $event.graphics; Run = $event.run; Records = @{} }
        }
        $recordKey = $event.sequence
        $records = $runs[$runKey].Records
        if ($records.ContainsKey($recordKey)) {
            if ($records[$recordKey].Payload -ceq $payload) { $duplicateRecords++ }
            elseif (-not $records[$recordKey].Conflicting) {
                $records[$recordKey].Conflicting = $true
                $conflictingRecords++
            }
            continue
        }
        $records[$recordKey] = @{ Payload = $payload; Event = $event; Conflicting = $false }
    }
}

$profiles = @($runs.Values | Sort-Object { $_.Capture }, { $_.ProcessId }, { $_.Session }, { $_.Graphics }, { $_.Run } |
    ForEach-Object {
        $run = $_
        $events = @($run.Records.Values | Where-Object { -not $_.Conflicting } | ForEach-Object Event |
            Sort-Object { $_.monotonicMs }, { $_.sequence })
        $timings = [ordered]@{}
        foreach ($metric in $metrics) {
            $samples = @($events | Where-Object { $_.kind -ceq 'timings' -and $_.metric -ceq $metric } |
                ForEach-Object { $_.samplesUs })
            $timings[$metric] = Get-TimingSummary $samples
            if ($metric -ceq 'cameraGap' -and $null -ne $timings[$metric]) {
                $timings[$metric].over100Ms = @($samples | Where-Object { $_ -gt 100000 }).Count
                $timings[$metric].over250Ms = @($samples | Where-Object { $_ -gt 250000 }).Count
            }
        }
        $system = Get-SystemSummary @($events | Where-Object { $_.kind -ceq 'system' })
        $start = @($events | Where-Object { $_.kind -ceq 'start' })
        $end = @($events | Where-Object { $_.kind -ceq 'end' })
        $lastSequence = ($events | Measure-Object sequence -Maximum).Maximum
        $missingSequences = if ($events.Count -gt 0) { $lastSequence - $events.Count } else { $null }
        [ordered]@{
            processId = $run.ProcessId
            capture = $run.Capture
            session = $run.Session
            graphics = $run.Graphics
            run = $run.Run
            records = $events.Count
            startMonotonicMs = if ($start.Count -eq 1) { $start[0].monotonicMs }
            endMonotonicMs = if ($end.Count -eq 1) { $end[0].monotonicMs }
            missingSequences = $missingSequences
            completeThroughEnd = $start.Count -eq 1 -and $start[0].sequence -eq 1 -and
                $end.Count -eq 1 -and $end[0].sequence -eq $lastSequence -and
                $end[0].monotonicMs -ge $start[0].monotonicMs -and $missingSequences -eq 0
            timings = $timings
            system = $system
            missingMeasurements = @($metrics | Where-Object { $null -eq $timings[$_] }) +
                @(if ($null -eq $system) { 'managedMemory'; 'thermal' }
                  else {
                      if ($null -eq $system.managedChangeBytes) { 'managedMemoryGrowth' }
                      if ($null -eq $system.highestThermalSeverity) { 'thermal' }
                  })
        }
    })

$identity = if ($TrialManifestPath) { Get-Content -LiteralPath $TrialManifestPath -Raw | ConvertFrom-Json -AsHashtable }
$snapshot = if ($identity.postTrialSnapshot -is [Collections.IDictionary]) {
    [ordered]@{
        totalPssKb = if (Test-Integer $identity.postTrialSnapshot.totalPssKb) { $identity.postTrialSnapshot.totalPssKb }
        totalRssKb = if (Test-Integer $identity.postTrialSnapshot.totalRssKb) { $identity.postTrialSnapshot.totalRssKb }
        androidThermalStatus = if ((Test-Integer $identity.postTrialSnapshot.androidThermalStatus) -and
            $identity.postTrialSnapshot.androidThermalStatus -le 6) { $identity.postTrialSnapshot.androidThermalStatus }
    }
}
$recordCount = ($profiles | ForEach-Object { $_.records } | Measure-Object -Sum).Sum
$report = [ordered]@{
    schema = 'rescuar.ar-performance-profile'
    version = 1
    trial = $identity.trial
    candidate = [ordered]@{
        installedApkSha256 = if ($identity.installedApkSha256 -match '^[a-f0-9]{64}$') { $identity.installedApkSha256 }
        device = $identity.model
        androidApi = $identity.androidApi
        arCoreVersion = $identity.arCoreVersion
    }
    postTrialSnapshot = $snapshot
    coverage = [ordered]@{
        records = [int]$recordCount
        duplicates = $duplicateRecords
        conflicting = $conflictingRecords
        malformed = $malformedRecords
        hasProfileEvidence = $recordCount -gt 0
        missingIdentity = $identity.installedApkSha256 -notmatch '^[a-f0-9]{64}$'
    }
    runs = $profiles
    methodology = @(
        'Nearest-rank percentiles over observed samples only. Timing values use milliseconds.'
        'First-frame and first-route overlay timings start at ARCore producer start. The route metric is a renderer visibility proxy.'
        'A camera gap is the interval between distinct images drawn. A presentation interval is between successful scene draws.'
        'Gap distributions require another frame to close the interval. A stall through exit may have no measured gap.'
        'Scene draw/present and conversion-observed timings are app-observed, without compositor display or GPU timestamp guarantees.'
        'Managed heap samples exclude native memory. The optional device snapshot is post-trial and gives no process memory trend.'
        'Absent boundaries, sequence gaps and malformed records limit comparisons. The optional package identity describes the app installed at collection.'
    )
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputPath = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path 'ar-performance-profile.json'
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Output "AR performance profile: runs=$($profiles.Count); records=$([int]$recordCount); malformed=$malformedRecords; conflicts=$conflictingRecords. $outputPath"
