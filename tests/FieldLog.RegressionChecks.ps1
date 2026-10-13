$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "../RescuAR-FieldLog.ps1"
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path -LiteralPath $scriptPath).Path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }

# Import only pure functions, never the script's device operations.
foreach ($name in @('Get-RotationThresholdKB', 'Get-FieldLogMarkerPriority', 'Get-FieldCaptureQuality')) {
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
}
finally {
    # Delete only the two exact files created by this test, then the empty folder.
    Remove-Item -LiteralPath (Join-Path $testDirectory 'logcat-001.txt') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $testDirectory 'logcat-002.txt') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $testDirectory -Force
}
Write-Host "$passed field-log regression checks passed."
