# React + Vite

This template provides a minimal setup to get React working in Vite with HMR and some Oxlint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the Oxlint configuration

If you are developing a production application, we recommend using TypeScript with type-aware lint rules enabled. Check out the [TS template](https://github.com/vitejs/vite/tree/main/packages/create-vite/template-react-ts) for information on how to integrate TypeScript and Oxlint's TypeScript related rules in your project.

## External inundation model client

Set `VITE_PREP_API_URL` to the **full prediction endpoint** of the external PREP
service, then restart or rebuild Admin. The client sends a JSON POST containing
`riverLevel` (metres), `mode` (`live` or `simulation`), `inputSource`, and
`observedAt` (the verified observation timestamp, or `null` for simulation).
The service must accept this frontend contract or provide an adapter for it.

The JSON response supplies `alarm` (`code`, `level`, `label`), `inundation`
(`has_triggered_rule`, `affected_barangays`, `triggered_thresholds`,
`evidence_status`), optional `exposure.total_population_affected`, and `model`
metadata. Results and evidence labels come from the service. No prediction
formula is implemented in Admin. Missing configuration, request failures, and
invalid responses retain the existing unavailable state. This restores the
missing frontend module; compatibility with the deployed R/Plumber service
still requires checking its endpoint and payload contract.

Run the transport checks with `node --test tests/prepInundationApi.test.mjs`.
