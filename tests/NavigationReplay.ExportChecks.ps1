$ErrorActionPreference = 'Stop'
$exporter = Join-Path $PSScriptRoot '../build/Export-NavigationReplay.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('rescuar-replay-export-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$capture = '0123456789abcdef0123456789abcdef'
$passed = 0
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw "FAIL: $message" }
    $script:passed++
}
function Lines([long]$sequence, [string]$kind, [object]$data) {
    $record = [ordered]@{ schema = 'rescuar.navigation-replay'; version = 1; captureId = $capture;
        sequence = $sequence; kind = $kind; utc = '2026-11-05T09:00:00.1234567+00:00'; monotonicMs = 123456; data = $data }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($record | ConvertTo-Json -Depth 8 -Compress))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $digest = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    $base64 = [Convert]::ToBase64String($bytes)
    $parts = [int][Math]::Ceiling($base64.Length / 100.0)
    for ($i = 0; $i -lt $parts; $i++) {
        '2026-11-05 17:00:00.000 100 101 I RescuAR-Replay: NAV_REPLAY_V1 {0} {1} {2}/{3} {4} {5}' -f
            $capture, $sequence, ($i + 1), $parts, $digest,
            $base64.Substring($i * 100, [Math]::Min(100, $base64.Length - $i * 100))
    }
}
function Export([string]$name, [string[]]$lines) {
    $dir = Join-Path $testRoot $name
    New-Item -ItemType Directory -Path $dir | Out-Null
    $log = Join-Path $dir 'logcat-001.txt'
    [IO.File]::WriteAllLines($log, $lines, [Text.UTF8Encoding]::new($false))
    & $exporter -LogPaths @($log) -OutputDirectory $dir | Out-Null
    return (Get-Content -LiteralPath (Join-Path $dir 'navigation-replay-quality.json') -Raw | ConvertFrom-Json)
}
try {
    $start = @(Lines 1 'capture_start' @{ exactCoordinates = $true })
    $event = @(Lines 2 'location.raw' @{ latitude = 14.123456789012345; longitude = 121.98765432109876;
        acquisitionElapsedRealtimeNanoseconds = 1234567890123456L; label = '試験' })
    $reversed = @($event)
    [array]::Reverse($reversed)
    $valid = Export 'valid' @($start + $reversed + @($event[0]))
    Check ($valid.records -eq 2 -and $valid.completeThroughLastRecord -and $valid.identicalDuplicateChunks -eq 1) `
        'out-of-order and identical repeated chunks reconstruct two complete records'
    $jsonl = @(Get-Content -LiteralPath (Join-Path $testRoot 'valid/navigation-replay.jsonl'))
    $restored = $jsonl[1] | ConvertFrom-Json
    Check ($restored.data.latitude -eq 14.123456789012345 -and $restored.data.label -ceq '試験' -and
        $restored.data.acquisitionElapsedRealtimeNanoseconds -eq 1234567890123456L) `
        'export preserves exact coordinate numbers, Unicode and integer sensor clocks'
    $missing = Export 'missing' @($start + $event[0..($event.Count - 2)])
    Check ($missing.records -eq 1 -and $missing.incompleteRecords -eq 1 -and -not $missing.completeThroughLastRecord) `
        'a missing chunk cannot silently become a replayable observation'
    $damaged = @($event)
    $damaged[0] = $damaged[0] -replace ' ([A-Za-z0-9+/=]+)$', ' AAAA'
    $corrupt = Export 'corrupt' @($start + $damaged)
    Check ($corrupt.invalidRecords -eq 1 -and -not $corrupt.completeThroughLastRecord) `
        'a payload checksum mismatch is rejected'
    $jump = Export 'sequence-gap' @($start + @(Lines 3 'location.raw' @{ latitude = 1; longitude = 2 }))
    Check ($jump.captures[0].missingSequences -eq 1 -and -not $jump.completeThroughLastRecord) `
        'whole lost records are detected through envelope sequence gaps'
    $drop = Export 'explicit-drop' @($start + @("2026-11-05 17:00:00.000 100 101 W RescuAR-Replay: NAV_REPLAY_DROP $capture 2 ArgumentException"))
    Check ($drop.explicitlyDroppedRecords -eq 1 -and -not $drop.completeThroughLastRecord) `
        'explicit serialization drops remain visible in replay quality'
    $fake = $start | ForEach-Object { $_ -replace 'I RescuAR-Replay:', 'I adbd: echoed I RescuAR-Replay:' }
    $echo = Export 'echo' @($fake)
    Check ($echo.records -eq 0 -and -not $echo.completeThroughLastRecord) `
        'ADB echoes cannot impersonate actual replay-tag records'
    $partial = Export 'no-start' @($event)
    Check (-not $partial.captures[0].hasStart -and -not $partial.completeThroughLastRecord) `
        'a capture starting after process initialization is identified as partial'
    Write-Output "Navigation replay export checks passed: $passed"
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTestRoot.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTestRoot).StartsWith('rescuar-replay-export-checks-')) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
