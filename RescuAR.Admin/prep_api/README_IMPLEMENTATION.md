# PREP exposure API

Source reviewed on 2026-11-03. This R/Plumber service computes deterministic
population/facility exposure to 5-, 25- and 100-year reference hazard polygons.
It is not a calibrated live inundation predictor. Service deployment and live
frontend acceptance have not been revalidated by this documentation update.

## Service configuration

Deploy `RescuAR.Admin/prep_api/` as a separate service with its own Docker build
context; the included `Dockerfile` installs the API packages. On Railway set:

```dotenv
PREP_ALLOWED_ORIGINS=https://YOUR_ADMIN_DOMAIN
```

Use the actual frontend origin. Multiple origins are comma-separated. Railway
supplies `PORT`; `run_api.R` uses it, defaulting to 8000 locally. On the frontend,
set `VITE_PREP_EXPOSURE_API_URL` to this service's base URL and rebuild Vite.
These variables belong to different services.

For local Docker use, from the repository root:

```powershell
docker build -t rescuar-prep RescuAR.Admin/prep_api
docker run --rm -p 8000:8000 -e PREP_ALLOWED_ORIGINS=http://localhost:5173 rescuar-prep
```

Set frontend `VITE_PREP_EXPOSURE_API_URL=http://localhost:8000`. Local execution
without Docker requires the packages loaded by `plumber.R` and its sourced R
files, and `Rscript run_api.R` from `prep_api/`. The `install_packages.R` named in
the runner's missing-package error is not included; use the Dockerfile as the
available API dependency recipe.

## Implemented endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/health` | Process/model metadata; not a frontend integration test |
| GET | `/v1/exposure/definitions` | Definitions, limitations and `automatic_rule_config` metadata |
| POST | `/v1/exposure/scenario` | Full scenario from `return_period` and `minimum_hazard` |
| GET | `/v1/exposure/population` | Barangay estimates for the selected scenario |
| GET | `/v1/exposure/facilities` | Facilities intersecting the selected hazard polygons |
| GET | `/v1/hazard/exposure` | Backward-compatible population alias |
| POST | `/v1/inundation/scenario` | Deprecated scenario alias, not a live flood predictor |

## Rule configuration and incomplete frontend connection

`R/config.R` loads `config/exposure_scenario_rules.json`; missing/invalid files
fall back to provisional defaults. Rule metadata is included in definitions and
scenario model metadata. The frontend's dedicated rules loader instead requests
`GET /v1/exposure/rules`, which is **not implemented in `plumber.R`**. With this
checked-in API, that request fails, the page records a configuration error and
retains its provisional mapping. Metadata inclusion does not complete that loader.

The provisional mappings are:

| PAGASA state | Reference return period | Minimum hazard counted |
| --- | --- | --- |
| Alert | 5 years | Low |
| Alarm | 25 years | Medium |
| Critical | 100 years | High |

These are project scenario rules, not proven hydraulic equivalences or universal
warning thresholds. The [validation workflow](validation/VALIDATION_WORKFLOW.md)
separates research, gated file promotion and the unfinished API/frontend connection.
A promoted file or successful health response does not establish calibrated
automatic mapping in the dashboard.
