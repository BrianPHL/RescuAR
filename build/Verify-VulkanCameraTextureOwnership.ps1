[CmdletBinding()]
param(
    [string]$ProjectRoot = (Join-Path $PSScriptRoot ".."),
    [string]$AssemblyPath,
    [string]$PackageRoot = (Join-Path $env:USERPROFILE ".nuget/packages")
)

$ErrorActionPreference = "Stop"
$resolvedRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
if (!$AssemblyPath) {
    $AssemblyPath = Join-Path $resolvedRoot "RescuAR.MAUI/obj/Release/net9.0-android/android-arm64/RescuAR.MAUI.dll"
}
[xml]$project = Get-Content -LiteralPath (Join-Path $resolvedRoot "RescuAR.MAUI/RescuAR.MAUI.csproj")
$engineVersion = $project.SelectSingleNode("/Project/PropertyGroup/EvergineVersion").InnerText
$mauiVersion = $project.SelectSingleNode("/Project/PropertyGroup/MauiVersion").InnerText
$cecilPath = Join-Path $PackageRoot "microsoft.maui.controls.build.tasks/$mauiVersion/buildTransitive/netstandard2.0/Mono.Cecil.dll"
$enginePath = Join-Path $PackageRoot "evergine.vulkan/$engineVersion/lib/net8.0/Evergine.Vulkan.dll"
Add-Type -Path $cecilPath

function Assert-Contract([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    Write-Output "PASS: $Message"
}

# Inspect the real compiled app and pinned engine. No Vulkan functions or GPU
# driver are invoked, and no replacement engine types are used.
$app = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $AssemblyPath).Path)
$engine = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $enginePath).Path)
try {
    $target = $app.MainModule.GetType("RescuAR.MAUI.Platforms.Android.Vulkan.ARCore.VulkanRgbaTarget")
    $create = @($target.Methods | Where-Object Name -eq "Create")[0]
    $wrapCalls = @($create.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq "FromVulkanImage" -and
        $_.Operand.DeclaringType.FullName -eq "Evergine.Vulkan.VKTexture"
    })
    Assert-Contract ($wrapCalls.Count -eq 1 -and $wrapCalls[0].Previous.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldc_I4_0) `
        "compiled camera wrapper explicitly declines native-image ownership"
    $owningCalls = @($create.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq "GetTextureFromNativePointer"
    })
    Assert-Contract ($owningCalls.Count -eq 0) "camera creation does not use the engine factory's owning wrapper"

    $texture = $engine.MainModule.GetType("Evergine.Vulkan.VKTexture")
    $wrap = @($texture.Methods | Where-Object Name -eq "FromVulkanImage")[0]
    $ownershipWrites = @($wrap.Body.Instructions | Where-Object {
        $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Stfld -and $_.Operand.Name -eq "ownResources"
    })
    Assert-Contract ($ownershipWrites.Count -eq 1 -and $ownershipWrites[0].Previous.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldarg_3) `
        "pinned engine stores the caller's ownership argument"
    $destroy = @($texture.Methods | Where-Object Name -eq "Destroy")[0]
    $guard = @($destroy.Body.Instructions | Where-Object {
        $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldfld -and $_.Operand.Name -eq "ownResources"
    })[0]
    $nativeFrees = @($destroy.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @("vkDestroyImage", "vkFreeMemory")
    })
    $lastFreeOffset = ($nativeFrees | Measure-Object Offset -Maximum).Maximum
    Assert-Contract ($nativeFrees.Count -gt 0 -and $null -ne $guard -and
        $guard.Next.OpCode.Code -in @([Mono.Cecil.Cil.Code]::Brfalse, [Mono.Cecil.Cil.Code]::Brfalse_S) -and
        $guard.Next.Operand.Offset -gt $lastFreeOffset) `
        "pinned engine skips image and memory destruction for a non-owning wrapper"

    foreach ($method in @($create, @($target.Methods | Where-Object Name -eq "Dispose")[0])) {
        foreach ($name in @("vkDestroyImage", "vkFreeMemory")) {
            $calls = @($method.Body.Instructions | Where-Object {
                $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name
            })
            Assert-Contract ($calls.Count -eq 1) "$($method.Name) keeps one app-owned $name call"
        }
    }
    Write-Output "8 compiled camera-image ownership checks passed. Device/driver behavior still requires field validation."
}
finally {
    $app.Dispose()
    $engine.Dispose()
}
