[CmdletBinding()]
param([switch]$NoRestore)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$evergineProject = Join-Path $root 'RescuAR.weproj'
$checksProject = Join-Path $root 'tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj'
$buildArgs = @('build', $checksProject, '-c', 'Release',
    "-p:EvergineProjectFilePath=$evergineProject", '-p:GenerateEvergineContent=true')
if ($NoRestore) { $buildArgs += '--no-restore' }

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "The real Evergine host regression build failed (exit $LASTEXITCODE)."
}

foreach ($suite in @('--ar-audit', '--alignment-recovery', '--camera-lifecycle',
    '--field-cadence', '--field-replay', '--offline-handoff')) {
    Write-Host "Running $suite"
    & dotnet run --project $checksProject --no-build -c Release -- $suite
    if ($LASTEXITCODE -ne 0) { throw "AR regression suite $suite failed (exit $LASTEXITCODE)." }
}
