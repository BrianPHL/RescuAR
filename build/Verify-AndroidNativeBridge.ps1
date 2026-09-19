param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath,

    [string]$ExpectedAbi = "arm64-v8a",

    [string]$ProvenanceOutput,

    [switch]$RequireStripped,

    [string]$ReadElfPath,

    [string]$NdkVersion = "28.2.13676358",

    [string]$CMakeVersion = "3.22.1",

    [string]$AndroidApi = "26"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hash = $sha256.ComputeHash($stream)
            return ([System.BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-Range {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes,

        [Parameter(Mandatory = $true)]
        [uint64]$Offset,

        [Parameter(Mandatory = $true)]
        [uint64]$Length,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ($Offset -gt [uint64]$Bytes.LongLength -or
        $Length -gt ([uint64]$Bytes.LongLength - $Offset)) {
        throw "$Label extends beyond the native library bounds."
    }
}

$resolvedArtifact = (Resolve-Path -LiteralPath $ArtifactPath).Path
$extension = [System.IO.Path]::GetExtension($resolvedArtifact).ToLowerInvariant()

if ([string]::IsNullOrWhiteSpace($ReadElfPath)) {
    $androidSdkRoot = $env:ANDROID_SDK_ROOT
    if ([string]::IsNullOrWhiteSpace($androidSdkRoot)) {
        $androidSdkRoot = $env:ANDROID_HOME
    }

    if ([string]::IsNullOrWhiteSpace($androidSdkRoot)) {
        throw "ReadElfPath was not supplied and ANDROID_SDK_ROOT/ANDROID_HOME is unavailable."
    }

    $ReadElfPath = Join-Path `
        $androidSdkRoot `
        "ndk/$NdkVersion/toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-readelf.exe"
}

$resolvedReadElf = (Resolve-Path -LiteralPath $ReadElfPath).Path

if ($extension -notin @('.apk', '.aab')) {
    throw "Expected an APK or AAB artifact, received '$extension'."
}

$nativeEntryName = if ($extension -eq '.apk') {
    "lib/$ExpectedAbi/libnative_bridge.so"
}
else {
    "base/lib/$ExpectedAbi/libnative_bridge.so"
}

$temporaryNative = [System.IO.Path]::GetTempFileName()
$archive = $null

try {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArtifact)
    $matchingEntries = @(
        $archive.Entries |
        Where-Object { $_.FullName -eq $nativeEntryName }
    )

    if ($matchingEntries.Count -ne 1) {
        throw "Expected exactly one '$nativeEntryName' entry; found $($matchingEntries.Count)."
    }

    $entryStream = $matchingEntries[0].Open()
    $outputStream = [System.IO.File]::Create($temporaryNative)
    try {
        $entryStream.CopyTo($outputStream)
    }
    finally {
        $outputStream.Dispose()
        $entryStream.Dispose()
    }
}
finally {
    if ($null -ne $archive) {
        $archive.Dispose()
    }
}

try {
    $bytes = [System.IO.File]::ReadAllBytes($temporaryNative)

    if ($bytes.LongLength -lt 64) {
        throw "The packaged native bridge is too small to be an ELF64 library."
    }

    if ($bytes[0] -ne 0x7f -or
        $bytes[1] -ne 0x45 -or
        $bytes[2] -ne 0x4c -or
        $bytes[3] -ne 0x46) {
        throw "The packaged native bridge does not have an ELF header."
    }

    if ($bytes[4] -ne 2 -or $bytes[5] -ne 1) {
        throw "The packaged native bridge must be 64-bit little-endian ELF."
    }

    $machine = [System.BitConverter]::ToUInt16($bytes, 18)
    if ($machine -ne 183) {
        throw "The packaged native bridge is not AArch64; ELF machine=$machine."
    }

    $programHeaderOffset = [System.BitConverter]::ToUInt64($bytes, 32)
    $programHeaderEntrySize = [System.BitConverter]::ToUInt16($bytes, 54)
    $programHeaderCount = [System.BitConverter]::ToUInt16($bytes, 56)

    if ($programHeaderEntrySize -lt 56 -or $programHeaderCount -eq 0) {
        throw "The packaged native bridge has an invalid ELF program-header table."
    }

    $loadSegmentCount = 0
    $minimumLoadAlignment = [uint64]::MaxValue

    for ($index = 0; $index -lt $programHeaderCount; $index++) {
        $headerOffset = $programHeaderOffset + ([uint64]$index * $programHeaderEntrySize)
        Assert-Range -Bytes $bytes -Offset $headerOffset -Length $programHeaderEntrySize -Label "ELF program header $index"

        $headerIndex = [int]$headerOffset
        $programType = [System.BitConverter]::ToUInt32($bytes, $headerIndex)
        if ($programType -ne 1) {
            continue
        }

        $loadSegmentCount++
        $segmentOffset = [System.BitConverter]::ToUInt64($bytes, $headerIndex + 8)
        $virtualAddress = [System.BitConverter]::ToUInt64($bytes, $headerIndex + 16)
        $alignment = [System.BitConverter]::ToUInt64($bytes, $headerIndex + 48)

        if ($alignment -eq 0) {
            throw "ELF load segment $index has zero alignment."
        }

        if (($segmentOffset % $alignment) -ne ($virtualAddress % $alignment)) {
            throw "ELF load segment $index violates offset/address alignment."
        }

        if ($alignment -lt $minimumLoadAlignment) {
            $minimumLoadAlignment = $alignment
        }
    }

    if ($loadSegmentCount -eq 0) {
        throw "The packaged native bridge has no ELF load segments."
    }

    if ($minimumLoadAlignment -lt 16384) {
        throw "The packaged native bridge has load alignment below 16 KiB: $minimumLoadAlignment bytes."
    }

    $symbolOutput = @(
        & $resolvedReadElf --dyn-syms --wide $temporaryNative 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw "llvm-readelf failed while reading dynamic symbols: $($symbolOutput -join ' ')"
    }

    $symbolText = $symbolOutput -join "`n"
    $requiredExports = @(
        'rescuar_from_java_hardware_buffer',
        'rescuar_release_hardware_buffer'
    )

    foreach ($export in $requiredExports) {
        $escapedExport = [regex]::Escape($export)
        $exportPattern = "(?m)^\s*\d+:\s+[0-9a-fA-F]+\s+\d+\s+\S+\s+GLOBAL\s+DEFAULT\s+\S+\s+$escapedExport(?:@@?\S+)?\s*$"
        if ($symbolText -notmatch $exportPattern) {
            throw "The packaged native bridge is missing required export '$export'."
        }
    }

    $dynamicOutput = @(
        & $resolvedReadElf --dynamic --wide $temporaryNative 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw "llvm-readelf failed while reading dependencies: $($dynamicOutput -join ' ')"
    }

    $observedSharedObjects = @(
        $dynamicOutput |
        ForEach-Object {
            if ($_ -match '\(NEEDED\).*Shared library: \[([^\]]+)\]') {
                $Matches[1]
            }
        } |
        Sort-Object -Unique
    )

    $soname = @(
        $dynamicOutput |
        ForEach-Object {
            if ($_ -match '\(SONAME\).*Library soname: \[([^\]]+)\]') {
                $Matches[1]
            }
        }
    )

    if ($soname.Count -ne 1 -or $soname[0] -ne 'libnative_bridge.so') {
        throw "The packaged native bridge SONAME is missing or invalid: '$($soname -join ', ')'."
    }

    $requiredDependencies = @(
        'libandroid.so',
        'liblog.so',
        'libc.so'
    )

    foreach ($dependency in $requiredDependencies) {
        if ($dependency -notin $observedSharedObjects) {
            throw "The packaged native bridge is missing required dependency '$dependency'."
        }
    }

    $allowedSharedObjects = @(
        'libnative_bridge.so',
        'libandroid.so',
        'liblog.so',
        'libm.so',
        'libdl.so',
        'libc.so'
    )

    $unexpectedSharedObjects = @(
        $observedSharedObjects |
        Where-Object { $_ -notin $allowedSharedObjects }
    )

    if ($unexpectedSharedObjects.Count -gt 0) {
        throw "The packaged native bridge declares unexpected shared objects: $($unexpectedSharedObjects -join ', ')."
    }

    $sectionOutput = @(
        & $resolvedReadElf --sections --wide $temporaryNative 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw "llvm-readelf failed while reading sections: $($sectionOutput -join ' ')"
    }

    $sectionText = $sectionOutput -join "`n"
    $debugSectionsPresent =
        $sectionText -match '(?m)\.debug_(?:info|line)\b'

    if ($RequireStripped -and $debugSectionsPresent) {
        throw "The packaged Release native bridge still contains debug sections."
    }

    $artifactSha256 = Get-Sha256 -Path $resolvedArtifact
    $nativeSha256 = Get-Sha256 -Path $temporaryNative

    if ([string]::IsNullOrWhiteSpace($ProvenanceOutput)) {
        $ProvenanceOutput = "$resolvedArtifact.native-provenance.json"
    }

    $sourceRevision = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) {
        'unavailable'
    }
    else {
        $env:GITHUB_SHA
    }

    $provenance = [ordered]@{
        schemaVersion = 1
        verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        artifactFile = [System.IO.Path]::GetFileName($resolvedArtifact)
        artifactSha256 = $artifactSha256
        nativeEntry = $nativeEntryName
        nativeSha256 = $nativeSha256
        abi = $ExpectedAbi
        elfClass = 'ELF64'
        elfMachine = 'AArch64'
        minimumLoadAlignmentBytes = $minimumLoadAlignment
        requiredExports = $requiredExports
        requiredDependencies = $requiredDependencies
        observedSharedObjects = $observedSharedObjects
        debugSectionsPresent = $debugSectionsPresent
        strippingPolicy = if ($RequireStripped) { 'required-and-verified' } else { 'reported' }
        ndkVersion = $NdkVersion
        cmakeVersion = $CMakeVersion
        androidApi = $AndroidApi
        readElfTool = [System.IO.Path]::GetFileName($resolvedReadElf)
        sourceRevision = $sourceRevision
    }

    $provenanceDirectory = Split-Path -Parent $ProvenanceOutput
    if (-not [string]::IsNullOrWhiteSpace($provenanceDirectory)) {
        New-Item -ItemType Directory -Path $provenanceDirectory -Force | Out-Null
    }

    $provenance |
        ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath $ProvenanceOutput -Encoding UTF8

    Write-Host "Native bridge artifact verification PASSED."
    Write-Host "Artifact SHA-256: $artifactSha256"
    Write-Host "Native SHA-256:   $nativeSha256"
    Write-Host "Native entry:     $nativeEntryName"
    Write-Host "Provenance:       $ProvenanceOutput"
}
finally {
    Remove-Item -LiteralPath $temporaryNative -Force -ErrorAction SilentlyContinue
}
