# Exact location capture and local navigation replay

Reviewed: 2026-11-05, starting from `dev` revision `60986f0` plus the
[field-replay changes](../changes/2026-11-05-navigation-replay-and-astar-continuity.md).

## Developer-commanded AI replay

When a developer asks an AI agent to replay or locally reproduce GPS/navigation
behavior, the agent must carry out this workflow and report the results. Giving
the developer commands alone does not complete that request. For example:

```text
Replay Logs/YOUR_SESSION.zip locally. Investigate GPS jumps, the saved-MLD-to-A*
handoff and arrival decisions, and report what reproduces with links to the results.
```

This is an authorized focused host policy check using existing tools and
dependencies. It does not require another capture consent prompt. It does not
start the Android app, a browser, a routing service or a new field recording.
For a documentation-only request, update these instructions without running replay.

1. **Identify the evidence.** Inspect repository status and record the current
   branch/revision and any relevant working edits used by the runner. Use the
   developer's specified ZIP, directory or JSONL. If none is named, inspect
   available local captures; use an unambiguous match, or ask for the input when
   selection is ambiguous or nothing usable exists. Honor an explicit request for
   the latest capture. Do not silently substitute the synthetic fixture.
2. **Prepare a separate run.** Preserve the original capture. Extract ZIPs into a
   new ignored directory under `artifacts/navigation-replay/`, with a unique run
   name. Keep real coordinates, logs and generated reports in ignored locations.
   Identify the captured APK/source/profile from available manifests separately
   from the current runner revision; report unavailable identity as unknown.
3. **Check capture integrity.** Follow [Collect and extract](#collect-and-extract).
   When raw logs exist, run the exporter into the new run directory. Otherwise
   use the supplied JSONL and available quality metadata, noting which transport
   checks cannot be repeated. Inspect missing/corrupt chunks, drops, sequence gaps
   and recording boundaries. Missing exact events in an old/redacted bundle cannot
   be reconstructed as real GPS. Partial replay may still provide evidence, but
   its findings must identify the affected gaps.
4. **Execute the replay.** Use the [local command](#replay-locally-before-a-new-walk)
   with the actual input and a report path in the new run directory. Preserve the
   command, exit code and console output there. Use existing restored dependencies;
   do not install dependencies, run an Android build or broaden into other tests
   solely to replay. If required tooling is unavailable, report the exact blocker
   and the smallest next action instead of claiming a successful reproduction.
5. **Inspect the results.** Read the generated report, not just the exit code.
   Investigate the requested behavior using event sequences/times, provider and
   accuracy changes, geographic versus tracked displacement, registration holds,
   A* replacement/progress and arrival decisions. Distinguish captured inputs,
   recomputed policy results, recorded results and any differences. A clean input
   report or zero progress differences alone does not prove a visual fault fixed.
6. **Deliver the evidence.** State whether the requested behavior reproduced,
   did not reproduce, or remains inconclusive, with supporting sequences/times and
   relevant measurements. Report capture quality, exact versus synthetic data,
   source/build identity, input issues and replay limits. Link the local report,
   quality metadata and command/output record. Keep raw private coordinates out
   of committed change records. A replay request alone does not authorize code
   fixes, device testing, commits or pushes.

Use the relative reconstruction below only when that example is requested or
explicitly identified as the input. Label it synthetic in every result. Local
policy reproduction cannot certify surveyed GPS truth, rendered street placement
or device performance; retain the [acceptance boundary](README.md#acceptance-boundary).

## Capture behavior

At the owner's request, diagnostic builds include exact location and replay
records automatically in the ordinary field logs. There is no consent dialog,
launch flag or additional recording switch. `RescuArDiagnosticBuild=false`
continues to redact location-bearing fields and removes replay call sites.
Use the [field procedure](README.md) to identify the exact APK/build profile.

The structured stream includes:

- Raw GPS/network/fused callbacks, original coordinates and provider accuracy,
  speed/course/course accuracy, provider UTC and native acquisition/receipt time,
  rejection reasons, source start/stop and selected observations.
- Complete accepted route geometry in bounded batches, provider/data provenance,
  destination, route/trip identity and A* replacement/rebase decisions.
- Route startup, observed navigation and registration stage inputs/results,
  scoped policy resets and registration transfers, paired tracked
  motion and frame ownership, pre-fix progress, fusion decisions, accepted PDR
  advances, arrival observations and explicit user arrival choices.
- Build identity, capture ID, monotonically increasing event sequence and
  checkpoint/drop records. Unavailable numeric values remain named JSON strings
  such as `"NaN"`; they are not converted into invented measurements.

Coordinates use round-trip numeric precision, not the previous four-decimal grid.
They are reported positions, not surveyed truth: a provider can report a small
accuracy radius while its coordinate is biased. Original provider UTC and the
monotonic acquisition-derived observation time are separate fields.

These exports contain exact private field data. Keep real captures under ignored
`Logs/` or `artifacts/`; do not commit them as test fixtures. The existing
30-day maximum field-evidence retention policy remains in source.

## Collect and extract

From the repository root, use the existing field logger with PowerShell 7 and an
authorized device. Starting/collecting a field recording remains a tester action:

```powershell
./RescuAR-FieldLog.ps1 -Start -AppOnly
# Perform the intended trip, then reconnect the phone.
./RescuAR-FieldLog.ps1 -Collect
```

Collection now adds `navigation-replay.jsonl` and
`navigation-replay-quality.json` alongside the existing log segments and APK
manifest. `build/Capture-NavigationFieldEvidence.ps1` also includes these records
when reading an existing Logcat buffer; it does not start a recording.

To extract an already collected directory without a phone:

```powershell
$logPaths = (Get-ChildItem -LiteralPath 'Logs/YOUR_SESSION' -Filter 'logcat-*.txt').FullName
./build/Export-NavigationReplay.ps1 -LogPaths $logPaths -OutputDirectory artifacts/local-replay
```

The transport tag is `RescuAR-Replay`. Records are split into bounded ASCII
chunks with capture/sequence identity, part counts and SHA-256. Extraction checks
missing/conflicting/truncated chunks, malformed envelopes, explicit drops and
sequence gaps. Check the quality file before interpreting a replay.
`completeThroughLastRecord` certifies the observed record sequence only; use
`CaptureQuality.json` and the manifest for recording boundaries and build identity.
An empty export from an older build is not a successful exact-data capture.

## Replay locally before a new walk

From the repository root, with the existing .NET dependencies restored:

```powershell
$projectFile = (Resolve-Path RescuAR.weproj).Path
dotnet run --no-restore -c Release --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj "-p:EvergineProjectFilePath=$projectFile" -- --replay artifacts/local-replay/navigation-replay.jsonl --output artifacts/local-replay/report.json
```

The runner executes the production matching, fusion, geographic-consistency,
registration, A* replacement and arrival policies against recorded stage inputs.
Startup and scoped reset records preserve policy initialization and recovery order.
The report exposes provider accuracy, geographic versus tracked displacement,
computed decisions/progress, recorded results, differences and input issues.
It uses captured evaluation times instead of the current clock to age fixes.
No routing API, Android session or UI is started.

This is policy-stage replay. Recorded readiness, accepted PDR advances,
pre-observation progress and explicit user choices are inputs; it does not
reconstruct accelerometer samples, ARCore tracking, camera pixels, GPU work or
every UI decision. Differences can indicate changed policy, missing context or
recording loss; they are not automatically regressions or proof of physical error.
Return code 2 means missing/invalid policy input or a sequence gap; 1 means the
input could not be read. A clean report still requires field acceptance.

## Existing November 5 evidence

The supplied November 5 bundle predates exact capture and redacted its absolute
origin. Its real latitude/longitude cannot be recovered. The checked-in
[relative reconstruction](../../tests/RescuAR.RegressionChecks/Fixtures/November5-relative-reconstruction.jsonl)
uses session-relative metre values around synthetic WGS84 `(0,0)`, explicitly
marked `exactCoordinates=false` and `syntheticRelativeCoordinates=true`.
It reproduces the 25.302 m registration disagreement, preserved A* handoff state
and remapped PDR advance; it is not the original complete GPS timeline.

```powershell
$projectFile = (Resolve-Path RescuAR.weproj).Path
dotnet run --no-restore -c Release --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj "-p:EvergineProjectFilePath=$projectFile" -- --replay tests/RescuAR.RegressionChecks/Fixtures/November5-relative-reconstruction.jsonl --output artifacts/relative-replay/report.json
```

## Focused checks and next acceptance

```powershell
dotnet run --no-restore -c Release --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj "-p:EvergineProjectFilePath=$projectFile" -- --field-replay
./tests/NavigationReplay.ExportChecks.ps1
```

After local review, repeat the saved-MLD-to-A* trip beyond five minutes and a nearby
destination trip. Expect the same-corridor replacement to preserve uncertainty
and remap progress without a fresh single-fix registration. An untrusted
registration still holds AR. Qualified A* previews can bridge to eight seconds;
frame, position, hazard and ownership checks remain in force. GPS vicinity stays
informational and guidance continues. Only an eligible explicit arrival choice
ends guidance. Collect a matching recording, exact replay and APK identity, then
report actual results before behavior-dependent tuning.
