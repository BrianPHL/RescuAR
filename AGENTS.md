# Repository Guidelines

## Project Structure & Module Organization

- `RescuAR/`: shared .NET 8 Evergine code, AR rendering, and navigation. Routing datasets and OSRM profiles live under `Navigation/Data/`.
- `RescuAR.MAUI/`: Android app organized into `Views/`, `ViewModels/`, `Services/`, `Models/`, `Platforms/`, and `Resources/`.
- `RescuAR.Windows/` and `RescuAR.Editor/`: desktop host and editor extensions; `Content/` contains Evergine scenes and assets.
- `RescuAR.Admin/src/`: React dashboard; `public/` holds static assets and `scraper/` runs telemetry ingestion.
- `tests/RescuAR.RegressionChecks/`: regression runner; `build/`: Android validation scripts.

## Build, Test, and Development Commands

Run .NET commands from the repository root. Android requires .NET 9, the `maui-android` workload, Android SDK, and Java 17. Admin requires Node.js 22.12+.

- `dotnet build RescuAR.Windows.sln`: restore and build desktop projects.
- `dotnet run --project RescuAR.Windows/RescuAR.Windows.csproj`: launch the desktop host.
- `dotnet build RescuAR.MAUI.sln`: build the Android app.
- In `RescuAR.Admin/`, use `npm ci` to install, `npm run dev` to serve locally, `npm run build` to bundle, and `npm run lint` to run Oxlint.
- In `RescuAR.Admin/scraper/`, run `npm ci`, then `npm start` to launch the separate worker.

## Coding Style & Naming Conventions

Prefer four-space C# indentation and two-space JavaScript/JSX indentation; preserve surrounding formatting. Use PascalCase for C# types/methods and React components, camelCase for locals/functions, and `I`-prefixed interfaces. Keep page/view-model pairs named `MapPage`/`MapViewModel`. Respect nullable annotations and consume routing through `IRoutingService`. React rules are configured in `RescuAR.Admin/.oxlintrc.json`.

## Testing Guidelines

Run `dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj`. This console harness uses `Check(condition, "behavior description")`; add descriptive assertions for routing, AR, or policy changes. No coverage threshold or admin test script is configured; run admin lint/build and manually verify changed screens. For Android changes, run `./build/Verify-ARCoreReleaseReadiness.ps1` and follow the package/device checks in `.github/workflows/arcore-native-bridge.yml` and `ARCORE_BATCH_14_FIELD_VALIDATION.md`.

## Commit & Pull Request Guidelines

History mixes plain summaries and scoped Conventional Commits. Prefer `fix(routing): clarify evacuation access` or `feat(navigation): add regional data`. Keep commits focused. PRs should explain behavior changes, link relevant issues, report validation commands/results, and include screenshots for UI changes or device evidence for AR changes.

## Security & Configuration Tips

Keep credentials in ignored environment files. Use `VITE_SUPABASE_URL`/`VITE_SUPABASE_ANON_KEY` for the dashboard; reserve `SUPABASE_SERVICE_ROLE_KEY` for the worker. Preserve dependency pins documented in `ARCORE_DEPENDENCY_COMPATIBILITY_MATRIX.md`; update compatibility evidence when changing them.

