# Current Android field procedure

Aligned with source on 2026-11-04, starting at `dev` revision `cb8c760` plus the
offline-handoff changes recorded in [the change record](../changes/2026-11-04-offline-route-handoff.md).
This is the current procedure. Dated reports retain their own candidate results;
none establishes acceptance of a later build.

## Build and identify the candidate

Keep the pinned [compatibility profile](../../ARCORE_DEPENDENCY_COMPATIBILITY_MATRIX.md).
From the repository root, build a diagnostic candidate for field evidence:

```powershell
$fieldRevision = (git rev-parse HEAD).Trim()
./build/Verify-ARCoreReleaseReadiness.ps1
dotnet publish RescuAR.MAUI/RescuAR.MAUI.csproj -c Release -f net9.0-android -r android-arm64 -p:AndroidPackageFormats=apk -p:RescuArDiagnosticBuild=true "-p:RescuArSourceRevision=$fieldRevision"
```

If source has uncommitted edits, retain the diff and identify the candidate with
an explicit revision suffix; HEAD alone cannot identify modified source. Record
the exact output APK's SHA-256 and packaged source/build profile. Use that same
APK on both devices. Historical APKs under ignored `artifacts/` may exist only on
the original machine; a filename in a report is not a reproducible source release.

Run package verification against the exact candidate path:

```powershell
./build/Test-ARCorePackage.ps1 -ArtifactPath "<absolute-path-to-candidate.apk>" -ExpectedDiagnosticBuild true -OutputDirectory artifacts/package-evidence
```

Native ELF verification needs the configured NDK tools. For rebuilding the native
bridge and recording provenance, follow the
[CI sequence](../../.github/workflows/arcore-native-bridge.yml). A production-profile
package must explicitly use `RescuArDiagnosticBuild=false` and be checked with
`ExpectedDiagnosticBuild=false`. Local Release builds default to diagnostics on.

## Current policy and expected behavior

- Camera initial registration seeds from one already qualified, pose-paired
  observation. The optional two-observation policy remains in source, but Camera
  does not opt into it. Registration does not replace independent road-entry,
  heading, tracking, ground, hazard or arrival checks.
- Fresh accepted geographic observations can update map position, guidance and
  arrival while AR placement is held. A stale map marker ages visibly and cannot
  become fresh routing or arrival evidence.
- Automatic arrival still needs two distinct qualifying fresh observations,
  precision/destination ownership and mapped-barrier checks. Current policy allows
  up to 30 seconds between qualifying observations; this does not relax each
  observation's freshness requirement. Pin overlap alone is not entry evidence.
- Live tracked visual continuity is bounded by time, distance and frame/route
  ownership; it does not create geographic progress or arrival evidence.
- Network loss switches the controller label to `A-STAR · saved MLD path`,
  preserving the downloaded route and placement until a validated replacement.
  Saved geometry expires after five minutes; confirmed deviation/hazard can
  require an earlier replacement. Fresh position and normal safety checks still
  gate acceptance. Reconnection does not automatically switch this trip back.
- Missing camera background blocks scene presentation. Brief conversion gaps
  must not reveal a skybox; sustained interruption shows camera recovery and
  eventually offers 2D Map. No route change intentionally restarts ARCore.
- Diagnostic logging does not bypass Depth crash protections. SM-A546E/API 34
  and SM-A546E or SM-A156E/API 36+ retain the baseline quarantine. A controlled
  diagnostic experiment requires `rescuar.arcore.force_depth_on`; explicit
  `force_depth_off` takes precedence. Route-depth occlusion is separately opt-in.

The [November 2 cadence recovery report](2026-11-02-samsung-honor-cadence-recovery.md)
records the prior cadence change and its local candidate evidence. The earlier
[device reliability](2026-11-02-device-reliability.md) and
[route regression](2026-11-02-ar-route-regression.md) reports describe superseded
startup candidates. Their field targets remain targets, not verified results.

## Focused host checks

For GPS/AR cadence and recovery changes, use:

```powershell
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --field-cadence
dotnet run --project tests/RescuAR.RegressionChecks/RescuAR.RegressionChecks.csproj -- --offline-handoff
```

Select different focused checks for other changes. A host check or successful APK
build does not measure street alignment, device sensor cadence or crash freedom.

## Manual device matrix

On Honor X9B and Samsung Galaxy A54, record three A* and three MLD trips using
the same known route and walking direction, including a reverse journey. Retain
a matching screen recording and logs for each trial.

1. Confirm candidate identity and Precise location. Test A* offline and MLD online;
   record the actual route-provider label rather than infer it from connectivity.
2. Start with the ground visible, then walk, stop and turn the phone. Expect fresh
   measured position and geographic guidance to continue while any AR hold reason
   identifies the missing input. Check AR recovery once qualified inputs return.
3. Switch AR → 2D → AR and stop/restart guidance. Expect accepted-route continuity,
   visible measured/aged position and frame requalification. Also test background/
   resume and a brief tracking interruption.
4. Make a safe permitted deviation. For MLD, distinguish a replacement request from
   accepted replacement geometry. Record origin/destination and observed detour in
   consented field evidence, not public issue text.
5. Approach the destination and wait for two distinct qualifying observations.
   Expect vicinity confirmation only when its precision/radius/barrier rules pass,
   or the existing user-arrival option when eligible.
6. Retest camera entry/exit, ground and flood features with baseline Depth policy.
   Record any separate Depth/occlusion experiment as a different configuration.
7. Start online MLD, then disable **all** internet transports while staying on the
   route. Expect `A-STAR · saved MLD path` promptly after Android reports the loss,
   unchanged saved geometry/progress, and no handoff-triggered camera restart.
   Continue beyond five minutes: expect background local refresh with fresh GPS,
   retained safe guidance if refresh fails, and qualified placement continuity.
   Separately test a safe deviation before expiry and reconnect during the trip;
   both must retain local controller ownership. Record actual placement, camera
   gaps and recovery messages; host checks do not establish these results.

After a trial, collect existing device evidence from the repository root:

```powershell
./build/Capture-NavigationFieldEvidence.ps1 -DeviceSerial DEVICE_SERIAL -TrialName honor-astar-outbound -OutputDirectory artifacts/field-trials/honor-astar-outbound
```

The collector reads existing logs/device/package information, attempts the
installed APK checksum, and retains existing consent/redaction. It does not launch
the app or change device settings. Use a new output directory for each trial.
For a provider-controlled diagnostic trial, the retained device reliability report
lists the launch override; record that override explicitly.

## Acceptance boundary

Record the build, device/OS/ARCore versions, provider, Depth flags, visible failure
time and relevant location/heading/registration/quality log messages. Measure
placement at known street/sidewalk references; GPS/compass consistency alone does
not establish surveyed alignment. Sustained useful AR, recovery-time/visibility
and placement targets remain unverified without those measurements.

Report actual results and matching evidence before further behavior-dependent
fixes. The [finding matrix](../../ARCORE_FINAL_FINDING_VALIDATION_MATRIX.md)
requires device evidence independently of builds and host checks.
