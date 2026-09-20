[CmdletBinding()]
param(
    [string]$ProjectRoot = (Join-Path $PSScriptRoot "..")
)

$ErrorActionPreference = "Stop"
$resolvedRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$projectPath = Join-Path $resolvedRoot "RescuAR.MAUI/RescuAR.MAUI.csproj"
$manifestPath = Join-Path $resolvedRoot "RescuAR.MAUI/Platforms/Android/AndroidManifest.xml"
$buildReporterPath = Join-Path $resolvedRoot "RescuAR.MAUI/Platforms/Android/Services/AndroidBuildManifestReporter.cs"
$workflowPath = Join-Path $resolvedRoot ".github/workflows/arcore-native-bridge.yml"
$matrixPath = Join-Path $resolvedRoot "ARCORE_DEPENDENCY_COMPATIBILITY_MATRIX.md"
$androidNamespace = "http://schemas.android.com/apk/res/android"

function Assert-Equal {
    param(
        [string]$Name,
        [AllowNull()][string]$Actual,
        [string]$Expected
    )

    if ($Actual -cne $Expected) {
        throw "$Name must be '$Expected', but was '$Actual'."
    }
}

function Get-ProjectValue {
    param(
        [xml]$Project,
        [string]$PropertyName
    )

    $node = $Project.SelectSingleNode("/Project/PropertyGroup/$PropertyName")
    if ($null -eq $node) {
        throw "Project property '$PropertyName' is missing."
    }

    return $node.InnerText.Trim()
}

function Get-AndroidElement {
    param(
        [xml]$Manifest,
        [string]$ElementName,
        [string]$AndroidName
    )

    $matchingNodes = @(
        $Manifest.SelectNodes("/manifest/$ElementName") |
            Where-Object {
                $_.GetAttribute("name", $androidNamespace) -ceq $AndroidName
            }
    )

    if ($matchingNodes.Count -ne 1) {
        throw "Expected exactly one $ElementName declaration for '$AndroidName'; found $($matchingNodes.Count)."
    }

    return $matchingNodes[0]
}

foreach ($requiredPath in @($projectPath, $manifestPath, $buildReporterPath, $workflowPath, $matrixPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required AR compatibility input is missing: $requiredPath"
    }
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$buildReporter = Get-Content -LiteralPath $buildReporterPath -Raw
$workflow = Get-Content -LiteralPath $workflowPath -Raw
$matrix = Get-Content -LiteralPath $matrixPath -Raw

Assert-Equal "TargetFramework" (Get-ProjectValue $project "TargetFramework") "net9.0-android"
Assert-Equal "SupportedOSPlatformVersion" (Get-ProjectValue $project "SupportedOSPlatformVersion") "26.0"
Assert-Equal "RuntimeIdentifier" (Get-ProjectValue $project "RuntimeIdentifier") "android-arm64"
Assert-Equal "MauiVersion" (Get-ProjectValue $project "MauiVersion") "9.0.120"
Assert-Equal "EvergineVersion" (Get-ProjectValue $project "EvergineVersion") "2025.10.21.3204"
Assert-Equal "EvergineLibBulletcVersion" (Get-ProjectValue $project "EvergineLibBulletcVersion") "2025.8.29.27"
Assert-Equal "ArCoreBindingVersion" (Get-ProjectValue $project "ArCoreBindingVersion") "1.47.1"
Assert-Equal "NativeBridgeNdkVersion" (Get-ProjectValue $project "NativeBridgeNdkVersion") "28.2.13676358"
Assert-Equal "NativeBridgeCMakeVersion" (Get-ProjectValue $project "NativeBridgeCMakeVersion") "3.22.1"
Assert-Equal "CompatibilityProfile" (Get-ProjectValue $project "RescuArCompatibilityProfile") "net9-maui9.0.120-evergine2025.10-arcore1.47.1"
Assert-Equal "CorrectiveBatch" (Get-ProjectValue $project "RescuArCorrectiveBatch") "ARCore-10"

$arCoreMetadata = @(
    $manifest.SelectNodes("/manifest/application/meta-data") |
        Where-Object {
            $_.GetAttribute("name", $androidNamespace) -ceq "com.google.ar.core"
        }
)
if ($arCoreMetadata.Count -ne 1) {
    throw "Expected exactly one com.google.ar.core application metadata declaration."
}
Assert-Equal "ARCore application policy" ($arCoreMetadata[0].GetAttribute("value", $androidNamespace)) "required"

$camera = Get-AndroidElement $manifest "uses-feature" "android.hardware.camera"
$arCamera = Get-AndroidElement $manifest "uses-feature" "android.hardware.camera.ar"
$vulkanVersion = Get-AndroidElement $manifest "uses-feature" "android.hardware.vulkan.version"
$vulkanLevel = Get-AndroidElement $manifest "uses-feature" "android.hardware.vulkan.level"

Assert-Equal "Camera feature policy" ($camera.GetAttribute("required", $androidNamespace)) "true"
Assert-Equal "AR camera feature policy" ($arCamera.GetAttribute("required", $androidNamespace)) "true"
Assert-Equal "Vulkan version feature policy" ($vulkanVersion.GetAttribute("required", $androidNamespace)) "true"
Assert-Equal "Vulkan version" ($vulkanVersion.GetAttribute("version", $androidNamespace)) "0x00400003"
Assert-Equal "Vulkan level feature policy" ($vulkanLevel.GetAttribute("required", $androidNamespace)) "true"
Assert-Equal "Vulkan hardware level" ($vulkanLevel.GetAttribute("version", $androidNamespace)) "0"

$expectedPackageExpressions = [ordered]@{
    "Microsoft.Maui.Controls" = '$(MauiVersion)'
    "Microsoft.Maui.Controls.Compatibility" = '$(MauiVersion)'
    "Evergine.Android" = '$(EvergineVersion)'
    "Evergine.OpenAL" = '$(EvergineVersion)'
    "Evergine.Targets" = '$(EvergineVersion)'
    "Evergine.Targets.Maui" = '$(EvergineVersion)'
    "Evergine.Vulkan" = '$(EvergineVersion)'
    "Evergine.LibBulletc.Natives" = '$(EvergineLibBulletcVersion)'
    "Vapolia.Google.ARCore" = '$(ArCoreBindingVersion)'
}

foreach ($packageName in $expectedPackageExpressions.Keys) {
    $packageNode = @(
        $project.SelectNodes("/Project//PackageReference") |
            Where-Object { $_.Include -ceq $packageName }
    )

    if ($packageNode.Count -ne 1) {
        throw "Expected exactly one PackageReference for '$packageName'; found $($packageNode.Count)."
    }

    Assert-Equal "$packageName version expression" ([string]$packageNode[0].Version) $expectedPackageExpressions[$packageName]
}

$expectedBuildMetadata = [ordered]@{
    "RescuAR.ArSupportPolicy" = "required"
    "RescuAR.RequiredAbi" = '$(NativeBridgeAbi)'
    "RescuAR.AndroidMinApi" = '$(NativeBridgeAndroidApi)'
    "RescuAR.VulkanRequirement" = "vulkan-1.0-level-0"
    "RescuAR.CompatibilityProfile" = '$(RescuArCompatibilityProfile)'
    "RescuAR.MauiVersion" = '$(MauiVersion)'
    "RescuAR.EvergineVersion" = '$(EvergineVersion)'
    "RescuAR.ArCoreBindingVersion" = '$(ArCoreBindingVersion)'
    "RescuAR.NdkVersion" = '$(NativeBridgeNdkVersion)'
    "RescuAR.CMakeVersion" = '$(NativeBridgeCMakeVersion)'
    "RescuAR.AutomatedSafetyProfile" = "ARCORE_SAFETY_TESTS_V1"
}

foreach ($metadataName in $expectedBuildMetadata.Keys) {
    $metadataNode = @(
        $project.SelectNodes("/Project//AssemblyMetadata") |
            Where-Object { $_.Include -ceq $metadataName }
    )

    if ($metadataNode.Count -ne 1) {
        throw "Expected exactly one AssemblyMetadata item for '$metadataName'; found $($metadataNode.Count)."
    }

    Assert-Equal "$metadataName value" ([string]$metadataNode[0].Value) $expectedBuildMetadata[$metadataName]

    if (-not $buildReporter.Contains("`"$metadataName`"")) {
        throw "Build manifest reporter does not read '$metadataName'."
    }
}

foreach ($reportedField in @(
    "arSupportPolicy=",
    "requiredAbi=",
    "androidMinApi=",
    "vulkanRequirement=",
    "compatibilityProfile=",
    "maui=",
    "evergine=",
    "arCoreBinding=",
    "ndk=",
    "cmake="
    "automatedSafetyProfile="
)) {
    if (-not $buildReporter.Contains($reportedField)) {
        throw "Build manifest reporter does not emit '$reportedField'."
    }
}

foreach ($workflowToken in @(
    "dotnet-version: 9.0.x",
    "ndk;28.2.13676358",
    "cmake;3.22.1",
    "-f net9.0-android",
    "Verify-ARCoreReleaseReadiness.ps1"
)) {
    if (-not $workflow.Contains($workflowToken)) {
        throw "AR compatibility workflow does not record required token '$workflowToken'."
    }
}

foreach ($matrixToken in @(
    "net9.0-android",
    "9.0.120",
    "2025.10.21.3204",
    "2025.8.29.27",
    "1.47.1",
    "28.2.13676358",
    "3.22.1",
    "android-arm64",
    "AR Required",
    "Finding 38"
)) {
    if (-not $matrix.Contains($matrixToken)) {
        throw "Compatibility matrix does not record required token '$matrixToken'."
    }
}

Write-Host "ARCore manifest and compatibility declarations are internally consistent." -ForegroundColor Green
