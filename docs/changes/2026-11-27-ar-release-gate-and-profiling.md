# AR release gate and profiling

Recorded on November 27, 2026 (Asia/Manila), starting from `dev` at `3f96b36`.

## Changes and purpose

- Run focused AR regression checks through the existing Evergine host build with
  the real project descriptor. Gate the Android package workflow on the same
  checks, including changes confined to the regression tests. CI selects the
  .NET 9 SDK, installs the .NET 8 runtime and checks the vendor's pinned
  Evergine Core archive hash before extraction.
- Verify the exact produced APK and record its packaged source revision, build
  profile, package hash and native-library hash. CI packages production; field
  profiling uses a separately identified diagnostic candidate.
- Record startup, camera gaps and stage timing in diagnostic builds. Separate
  native image import, conversion completion and scene draw timing. Summarize
  memory and thermal observations with field evidence and flag missing samples.
- The [Android field procedure](../field-testing/README.md) describes package
  verification, host checks and comparable performance trials. Import reuse and
  additional conversion targets remain decisions for captured device data.

## Verification

- The normal Release Evergine host build and all six focused suites passed
  (`--ar-audit`, `--alignment-recovery`, `--camera-lifecycle`, `--field-cadence`,
  `--field-replay` and `--offline-handoff`). The eight navigation replay and ten
  performance profile export checks passed.
- The static ARCore release-readiness check and all 11 compiled Vulkan
  ownership checks passed. The workflow's YAML, PowerShell, test-only triggers
  and check order passed local inspection. Package guard probes rejected
  mismatched and duplicate packaged source revisions.
- The documentation catalog was regenerated. Its verifier reported ten broken
  links in the existing tracked hotline backup.
- Three archived November 5 log chunks contained no Phase 3 profiling records.
  That confirms the evidence gap for those logs. Hosted CI and candidate device
  performance remain unverified.

## Local Android candidates

Both ARM64 Release packages were published from app source
`c42e7ee56a7a3ad8ff30bcea5107ba2966f9440b` and passed the exact package verifier.
Compilation checks confirmed that the profiler is present only in the diagnostic
build. Both builds reported the existing nullable warning in
`SafetyCircleService.cs:274` and no errors.

| APK under `artifacts/ar-phase3-c42e7ee/` | SHA-256 |
| --- | --- |
| `production/com.rescuar.app-Signed.apk` | `1904e64fae48730ec185532b13c80281b53f579017afd91fc3e3986adc131d60` |
| `diagnostic/com.rescuar.app-Signed.apk` | `b7793a7da0ed0db5a3bc6e565e3822f90e84731b339ee3bffc95ab6a57297abe` |

Each package directory contains `evidence/arcore-package-test-report.json`.
The local builds used the checked-in ARM64 native bridge with SHA-256
`c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`.
The hosted workflow is configured to rebuild the bridge from the pinned source
and native toolchain.

## Follow-up

Profile the exact diagnostic APK on the Honor X9B and Galaxy A54. Compare the
same routes and Depth modes, retain matching logs, and record the observed timing,
memory trend and thermal state before choosing rendering optimizations.
