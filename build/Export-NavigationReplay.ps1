[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$LogPaths,
    [Parameter(Mandatory)][string]$OutputDirectory
)

# Reads existing logs only. Output contains exact diagnostic locations and must
# remain with the private field bundle, never in checked-in replay fixtures.
$ErrorActionPreference = 'Stop'
$records = @{}
$dropCount = 0
$malformed = 0
$duplicates = 0
$maximumRecordBytes = 256 * 1024
$pattern = '^(?:\d{4}-)?\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+\s+\d+\s+\d+\s+[VDIWEF]\s+RescuAR-Replay\s*:\s+NAV_REPLAY_V1 ([a-f0-9]{32}) (\d{1,18}) (\d{1,3})/(\d{1,3}) ([a-f0-9]{64}) ([A-Za-z0-9+/=]+)\s*$'
$dropPattern = '^.*\s+[VDIWEF]\s+RescuAR-Replay\s*:\s+NAV_REPLAY_DROP [a-f0-9]{32} \d+ \w+\s*$'
$captureOrder = @{}
foreach ($path in $LogPaths) {
    foreach ($line in [IO.File]::ReadLines((Resolve-Path -LiteralPath $path).Path)) {
        if ($line -match $dropPattern) { $dropCount++; continue }
        if ($line -notmatch $pattern) {
            if ($line -match '^.*\s+[VDIWEF]\s+RescuAR-Replay\s*:\s+NAV_REPLAY_V1 ') { $malformed++ }
            continue
        }
        $capture = $Matches[1]
        if (-not $captureOrder.ContainsKey($capture)) { $captureOrder[$capture] = $captureOrder.Count }
        $sequence = [long]$Matches[2]
        $part = [int]$Matches[3]
        $parts = [int]$Matches[4]
        $digest = $Matches[5]
        $chunk = $Matches[6]
        if ($sequence -lt 1 -or $parts -lt 1 -or $parts -gt 128 -or
            $part -lt 1 -or $part -gt $parts -or $chunk.Length -gt 3000) { $malformed++; continue }
        $key = "$capture/$sequence"
        if (-not $records.ContainsKey($key)) {
            $records[$key] = @{ Capture = $capture; Sequence = $sequence; Parts = $parts;
                Digest = $digest; Chunks = @{}; Invalid = $false }
        }
        $record = $records[$key]
        if ($record.Parts -ne $parts -or $record.Digest -cne $digest) { $record.Invalid = $true; continue }
        if ($record.Chunks.ContainsKey($part)) {
            if ($record.Chunks[$part] -cne $chunk) { $record.Invalid = $true }
            else { $duplicates++ }
        }
        $record.Chunks[$part] = $chunk
    }
}
$decoded = [Collections.Generic.List[object]]::new()
$incomplete = 0
$invalid = 0
foreach ($record in $records.Values) {
    if ($record.Invalid) { $invalid++; continue }
    if ($record.Chunks.Count -ne $record.Parts) { $incomplete++; continue }
    try {
        $encoded = -join (1..$record.Parts | ForEach-Object { $record.Chunks[$_] })
        $bytes = [Convert]::FromBase64String($encoded)
        if ($bytes.Length -gt $maximumRecordBytes) { throw 'oversized record' }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }
        if ($actual -cne $record.Digest) { throw 'checksum mismatch' }
        $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $envelope = $json | ConvertFrom-Json
        if ($envelope.schema -cne 'rescuar.navigation-replay' -or $envelope.version -ne 1 -or
            $envelope.captureId -cne $record.Capture -or $envelope.sequence -ne $record.Sequence -or
            -not $envelope.kind) { throw 'invalid envelope' }
        $decoded.Add([PSCustomObject]@{ Capture = $record.Capture; CaptureOrder = $captureOrder[$record.Capture]; Sequence = $record.Sequence;
            Kind = $envelope.kind; Json = $json })
    }
    catch { $invalid++ }
}
$ordered = @($decoded | Sort-Object CaptureOrder, Sequence)
$captures = @($ordered | Group-Object Capture | ForEach-Object {
    $expected = 1L
    $missing = 0L
    foreach ($entry in $_.Group) {
        $missing += $entry.Sequence - $expected
        $expected = $entry.Sequence + 1
    }
    [PSCustomObject]@{ captureId = $_.Name; records = $_.Count; lastSequence = $expected - 1;
        missingSequences = $missing; hasStart = @($_.Group | Where-Object Kind -eq 'capture_start').Count -gt 0 }
})
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$root = (Resolve-Path -LiteralPath $OutputDirectory).Path
[IO.File]::WriteAllLines((Join-Path $root 'navigation-replay.jsonl'), [string[]]@($ordered.Json), [Text.UTF8Encoding]::new($false))
$complete = $ordered.Count -gt 0 -and $incomplete -eq 0 -and $invalid -eq 0 -and $malformed -eq 0 -and $dropCount -eq 0
foreach ($capture in $captures) { if (-not $capture.hasStart -or $capture.missingSequences -ne 0) { $complete = $false } }
$quality = [ordered]@{ schema = 'rescuar.navigation-replay'; version = 1; records = $ordered.Count;
    captures = $captures; incompleteRecords = $incomplete; invalidRecords = $invalid;
    malformedChunks = $malformed; identicalDuplicateChunks = $duplicates; explicitlyDroppedRecords = $dropCount;
    completeThroughLastRecord = $complete; containsExactLocations = $ordered.Count -gt 0;
    boundary = 'Logcat capture boundaries remain in CaptureQuality.json/trial.json; this checks replay record integrity only.' }
$quality | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'navigation-replay-quality.json') -Encoding UTF8
Write-Output ("Replay export: records={0}; incomplete={1}; invalid={2}; dropped={3}; completeThroughLastRecord={4}" -f
    $ordered.Count, $incomplete, $invalid, $dropCount, $complete)
