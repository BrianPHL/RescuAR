param(
    [Parameter(Mandatory = $true)][string]$ArtifactPath,
    [string]$ExpectedAbi = "arm64-v8a",
    [string]$ExpectedPackage = "com.rescuar.app",
    [ValidateSet("true", "false")][string]$ExpectedDiagnosticBuild = "false",
    [string]$OutputDirectory,
    [string]$ReadElfPath,
    [string]$ApkAnalyzerPath,
    [string]$BundleToolPath,
    [string]$ExpectedSourceRevision
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-BuildProfileValue([string]$Text, [string]$Key) {
    $entries = [regex]::Matches($Text, "(?m)^$([regex]::Escape($Key))=([^\r\n]*)\r?$")
    if ($entries.Count -ne 1 -or [string]::IsNullOrWhiteSpace($entries[0].Groups[1].Value)) {
        throw "Expected exactly one packaged '$Key' value."
    }
    return $entries[0].Groups[1].Value
}

function Find-ApkAnalyzer {
    if (-not [string]::IsNullOrWhiteSpace($ApkAnalyzerPath)) {
        return (Resolve-Path -LiteralPath $ApkAnalyzerPath).Path
    }
    $sdkRoot = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { $env:ANDROID_HOME }
    if ([string]::IsNullOrWhiteSpace($sdkRoot)) {
        throw "ApkAnalyzerPath and ANDROID_SDK_ROOT/ANDROID_HOME are unavailable."
    }
    $candidate = Get-ChildItem -LiteralPath $sdkRoot -Filter "apkanalyzer*" -File -Recurse |
        Where-Object { $_.Extension -in @('.bat', '.exe', '') } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($null -eq $candidate) { throw "apkanalyzer was not found below '$sdkRoot'." }
    return $candidate.FullName
}

function Assert-ManifestElement([string]$Text, [string]$Element, [string]$AndroidName, [string]$AdditionalPattern) {
    $name = [regex]::Escape($AndroidName)
    $pattern = "<$Element(?=[^>]*android:name=[`"']$name[`"'])(?=[^>]*$AdditionalPattern)[^>]*>"
    if ($Text -notmatch $pattern) {
        throw "Merged manifest is missing required $Element '$AndroidName'."
    }
}

$artifact = (Resolve-Path -LiteralPath $ArtifactPath).Path
$extension = [IO.Path]::GetExtension($artifact).ToLowerInvariant()
if ($extension -notin @('.apk', '.aab')) { throw "ArtifactPath must identify an APK or AAB." }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path ([IO.Path]::GetDirectoryName($artifact)) "arcore-package-evidence"
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$provenance = Join-Path $output "native-bridge-provenance.json"
$manifestFile = Join-Path $output "merged-android-manifest.xml"
$reportFile = Join-Path $output "arcore-package-test-report.json"

$archive = [IO.Compression.ZipFile]::OpenRead($artifact)
try {
    $nativeEntries = @($archive.Entries | Where-Object { $_.FullName -match '^(?:base/)?lib/[^/]+/[^/]+\.so$' } | ForEach-Object FullName | Sort-Object)
    $observedAbis = @($nativeEntries | ForEach-Object { if ($_ -match '^(?:base/)?lib/([^/]+)/') { $Matches[1] } } | Sort-Object -Unique)
    $profileEntryName = if ($extension -eq '.apk') { 'assets/rescuar-build-profile.txt' } else { 'base/assets/rescuar-build-profile.txt' }
    $profileEntries = @($archive.Entries | Where-Object { $_.FullName -ceq $profileEntryName })
    if ($profileEntries.Count -ne 1) { throw "Expected exactly one '$profileEntryName' build-profile asset; found $($profileEntries.Count)." }
    $profileReader = [IO.StreamReader]::new($profileEntries[0].Open())
    try { $buildProfile = $profileReader.ReadToEnd() -replace "^\uFEFF", "" }
    finally { $profileReader.Dispose() }
}
finally { $archive.Dispose() }
if ($nativeEntries.Count -eq 0) { throw "The artifact contains no native libraries." }
if ($observedAbis.Count -ne 1 -or $observedAbis[0] -ne $ExpectedAbi) {
    throw "Expected only ABI '$ExpectedAbi'; observed '$($observedAbis -join ', ')'."
}
$diagnosticBuild = Get-BuildProfileValue $buildProfile 'diagnosticBuild'
if ($diagnosticBuild -ne $ExpectedDiagnosticBuild) {
    throw "Packaged diagnostic-build profile does not match expected value '$ExpectedDiagnosticBuild'."
}
$sourceRevision = Get-BuildProfileValue $buildProfile 'sourceRevision'
if ($ExpectedSourceRevision -and $sourceRevision -cne $ExpectedSourceRevision) {
    throw "Packaged source revision '$sourceRevision' does not match '$ExpectedSourceRevision'."
}
$configuration = Get-BuildProfileValue $buildProfile 'configuration'
if ($configuration -ne 'Release') { throw "Expected a Release package, found '$configuration'." }
if ((Get-BuildProfileValue $buildProfile 'diagnosticRouteOverride') -ne 'false') { throw "Road diagnostics must retain navigation confidence checks." }
if ((Get-BuildProfileValue $buildProfile 'correctiveBatch') -cne 'ARCore-14') { throw "Packaged corrective-batch profile is incorrect." }
$validationProfile = Get-BuildProfileValue $buildProfile 'validationProfile'
if ($validationProfile -cne 'ARCORE_MANUAL_FIELD_VALIDATION_V1') { throw "Packaged validation profile is incorrect." }
$monoGcBridge = Get-BuildProfileValue $buildProfile 'monoGcBridge'
if ($monoGcBridge -cne 'new') { throw "Packaged Mono GC bridge profile is incorrect." }

if ($extension -eq '.apk') {
    $manifestLines = @(& (Find-ApkAnalyzer) manifest print $artifact 2>&1)
}
else {
    if (-not $BundleToolPath) { throw "BundleToolPath is required for AAB manifest inspection." }
    $manifestLines = @(& java -jar (Resolve-Path -LiteralPath $BundleToolPath).Path dump manifest --bundle=$artifact --module=base 2>&1)
}
if ($LASTEXITCODE -ne 0) { throw "Manifest extraction failed: $($manifestLines -join ' ')" }
$manifest = $manifestLines -join "`n"
$manifest | Set-Content -LiteralPath $manifestFile -Encoding UTF8

if ($manifest -notmatch "package=[`"']$([regex]::Escape($ExpectedPackage))[`"']") { throw "Package ID mismatch." }
if ($manifest -notmatch '<uses-sdk(?=[^>]*android:minSdkVersion=["'']26["''])[^>]*>') { throw "Minimum Android API declaration mismatch." }
Assert-ManifestElement $manifest "meta-data" "com.google.ar.core" "android:value=[`"']required[`"']"
Assert-ManifestElement $manifest "uses-feature" "android.hardware.camera" "android:required=[`"']true[`"']"
Assert-ManifestElement $manifest "uses-feature" "android.hardware.camera.ar" "android:required=[`"']true[`"']"
Assert-ManifestElement $manifest "uses-feature" "android.hardware.vulkan.version" "android:required=[`"']true[`"']"
Assert-ManifestElement $manifest "uses-feature" "android.hardware.vulkan.level" "android:required=[`"']true[`"']"
# Android's package inspector removes hexadecimal zero padding (0x400003).
if ($manifest -notmatch '<uses-feature(?=[^>]*android:name=["'']android\.hardware\.vulkan\.version["''])(?=[^>]*android:version=["''](?:(?i:0x0*400003)|4194307)["''])[^>]*>') { throw "Vulkan 1.0 declaration mismatch." }
if ($manifest -notmatch '<uses-feature(?=[^>]*android:name=["'']android\.hardware\.vulkan\.level["''])(?=[^>]*android:version=["''](?:0x0+|0)["''])[^>]*>') { throw "Vulkan level declaration mismatch." }

$buildProps = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) "Directory.Build.props") -Raw
if ($buildProps -notmatch '<RescuArDiagnosticBuild[^>]*>false</RescuArDiagnosticBuild>') { throw "Production-default diagnostic policy is missing." }

$verifyArgs = @{
    ArtifactPath = $artifact; ExpectedAbi = $ExpectedAbi; RequireStripped = $true
    ProvenanceOutput = $provenance; SourceRevision = $sourceRevision
}
if ($ReadElfPath) { $verifyArgs.ReadElfPath = $ReadElfPath }
& (Join-Path $PSScriptRoot "Verify-AndroidNativeBridge.ps1") @verifyArgs
$nativeEvidence = Get-Content -LiteralPath $provenance -Raw | ConvertFrom-Json

[ordered]@{
    schemaVersion = 1; testLayer = "packaging"; verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); result = "PASS"
    artifactFile = [IO.Path]::GetFileName($artifact); artifactType = $extension.TrimStart('.'); artifactSha256 = $nativeEvidence.artifactSha256
    sourceRevision = $sourceRevision; configuration = $configuration; nativeSha256 = $nativeEvidence.nativeSha256
    artifactBytes = (Get-Item -LiteralPath $artifact).Length; expectedPackage = $ExpectedPackage; expectedAbi = $ExpectedAbi
    observedAbis = $observedAbis; nativeEntries = $nativeEntries; nativeProvenance = [IO.Path]::GetFileName($provenance)
    mergedManifest = [IO.Path]::GetFileName($manifestFile); arSupportPolicy = "required"; vulkanRequirement = "vulkan-1.0-level-0"
    androidMinApi = 26; diagnosticBuild = $diagnosticBuild; buildProfileAsset = $profileEntryName
    validationProfile = $validationProfile; monoGcBridge = $monoGcBridge
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding UTF8

Write-Host "ARCore package test PASSED."
Write-Host "Report: $reportFile"
