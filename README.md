# RescuAR

RescuAR combines an Android evacuation-navigation app, Evergine AR rendering,
offline A* and online OSRM/MLD routing, and a React administration dashboard.
The dashboard uses Supabase, a separate PAGASA telemetry worker, and an R API
for deterministic flood-hazard exposure estimates.

Documentation aligned with source on 2026-11-03, starting from `dev` at
`d83d797`. Repository behavior does not establish a deployed service's state,
installed APK behavior, carrier delivery, or physical AR accuracy.

AI agents and new contributors should begin with [AGENTS.md](AGENTS.md) and the
[task-based documentation index](docs/README.md). Gemini/Antigravity and other
host entry formats are described in the [compatibility guide](docs/contributing/ai-agents.md).

## Project layout

| Path | Purpose |
| --- | --- |
| `RescuAR/` | Shared .NET 8 Evergine rendering and navigation; embedded routing data and MLD Docker build |
| `RescuAR.MAUI/` | .NET 9 Android application and ARM64 native camera bridge |
| `RescuAR.Windows/`, `RescuAR.Editor/`, `Content/` | Desktop host, editor extensions, scenes and assets |
| `RescuAR.Admin/` | React dashboard, Supabase functions/SQL, telemetry worker and PREP API |
| `tests/`, `build/` | Focused host checks and Android package/evidence tools |

## Android and desktop development

Use Windows, the .NET 9 SDK with the `maui-android` workload, Android SDK and
Java 17 for Android builds. Shared code and the console checks target .NET 8;
running those checks also requires its runtime. Keep the pinned versions in the
[compatibility matrix](ARCORE_DEPENDENCY_COMPATIBILITY_MATRIX.md).

From the repository root:

```powershell
dotnet restore RescuAR.MAUI.sln
dotnet build RescuAR.MAUI/RescuAR.MAUI.csproj -c Release -f net9.0-android -r android-arm64 -p:RescuArDiagnosticBuild=false
```

Ordinary builds package the checked-in ARM64 native bridge. Rebuilding it from
source requires the pinned NDK/CMake tools and `BuildNativeBridgeOnBuild=true`.
The [Android CI workflow](.github/workflows/arcore-native-bridge.yml) records
the restore, native rebuild, publish and package-verification sequence.

`Release` alone does not disable diagnostics: `Directory.Build.props` enables
them for local builds by default. Set `RescuArDiagnosticBuild=false` explicitly
for a production-profile package. CI with `ContinuousIntegrationBuild=true`
defaults to diagnostics off. Use the
[current field procedure](docs/field-testing/README.md) for diagnostic candidates.

The Windows desktop host is a separate target:

```powershell
dotnet build RescuAR.Windows.sln
dotnet run --project RescuAR.Windows/RescuAR.Windows.csproj
```

## Administration services

Use Node.js 22.12 or newer. Follow the
[admin setup guide](RescuAR.Admin/README.md) for the frontend, worker and PREP
connection. The [database guide](RescuAR.Admin/database/README.md) identifies
missing bootstrap prerequisites; this checkout cannot recreate the complete
Supabase database from an empty project.

See the [source-based status register](docs/status.md) for the existing Admin
build blocker and other implementation/acceptance gaps. Setup instructions do
not imply a passing frontend build or hosted acceptance.

The [SIM SMS gateway](RescuAR.Admin/SMS_GATEWAY_SETUP.md) is a separate Android
project under `RescuAR.Admin/android-sms-gateway/`. It is not the MAUI app.

## Verification

Choose checks relevant to the changed code. For the latest GPS/AR recovery work:

```powershell
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --field-cadence
./build/Verify-ARCoreReleaseReadiness.ps1
```

Authentication/directory checks live in `tests/RescuAR.AuthenticationChecks/`:

```powershell
dotnet run --project tests/RescuAR.AuthenticationChecks/RescuAR.AuthenticationChecks.csproj -- --evacuation-directory
```

Build and host-check results do not establish device behavior. Package checks,
device logs, field measurements and deployed-service checks are distinct evidence.
See the [documentation index](docs/README.md) for current guides, historical
investigations and unresolved implementation gaps.
