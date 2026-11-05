# Exact location capture and local navigation replay

Reviewed: 2026-11-05, starting from `dev` revision `60986f0` plus the
[field-replay changes](../changes/2026-11-05-navigation-replay-and-astar-continuity.md).

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
