# PREP R Option B Exposure API — Railway Deployment Package

This folder is intentionally deployable as a standalone Railway service. `Dockerfile` is at the folder root so Railway can auto-detect it.

## Service
- Health: `GET /health`
- Definitions: `GET /v1/exposure/definitions`
- Full scenario: `POST /v1/exposure/scenario`
- Population: `GET /v1/exposure/population`
- Facilities: `GET /v1/exposure/facilities`

## Required Railway variable

`PREP_ALLOWED_ORIGINS=https://YOUR-RESCUAR-ADMIN-DOMAIN`

Use the exact Admin origin, without a trailing slash. Multiple origins are supported as a comma-separated list.

Do not set `PORT`; Railway supplies it and `run_api.R` reads it automatically.

## Option B mappings
- PAGASA Alert -> 5-year / Low+
- PAGASA Alarm -> 25-year / Medium+
- PAGASA Critical -> 100-year / High

The API remains a deterministic reference hazard-exposure service, not a calibrated live inundation predictor.
