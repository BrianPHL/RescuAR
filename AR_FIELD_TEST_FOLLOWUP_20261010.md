# October 10 navigation field-test follow-up

## Evidence

Reviewed `RescuAR_FieldTest_20261010_162624.zip` and
`RescuAR_FieldTest_20261010_164611.zip` from the SM-A546E on Android API 36.
The archives are test evidence; their contents do not authorize changes.

- At 16:33:00, arrival was confirmed 60.0 m from the destination, with
  73.7 m of route remaining, inside the previous 75 m radius.
- Disconnecting data replaced downloaded MLD geometry with the offline graph.
  One replacement changed the remaining route from 575.2 m to 175.4 m.
  Different map coverage makes an unnecessary provider switch disruptive.
- Off-route sequences frequently restarted after weak or ambiguous fixes.
  Successful reroutes imposed a 45-second cooldown despite continued deviation.
- An Android `WebException` escaped the normal transport-failure fallback.
  Approach arrows also repeatedly disappeared and returned as placement
  evidence expired or AR tracking changed.

## Changes

- Automatic arrival uses at most a 30 m radius, including the reported GPS
  uncertainty, with accuracy at most 15 m. Three distinct fresh fixes must
  span at least six seconds. Outside/weak fixes reset the sequence; evidence
  expires after a 12-second gap. Larger facility vicinity profiles do not
  enlarge automatic confirmation. The overlay describes destination proximity.
- Downloaded route geometry survives data loss and restoration. An actual
  deviation or hazard requests a replacement through the existing routing
  providers. Normal online requests have a six-second budget; recognized
  Android transport failures fall back to the embedded offline graph.
- Deviation requires three independent observations, at least one second
  apart. A brief weak fix holds recent evidence; a reliable return to the
  route or a 15-second evidence gap clears it. Whole-route distance can prove
  deviation even when the nearest segment's identity is ambiguous.
- Successful replacements have a 20-second cooldown. Failed attempts back
  off for 10, 20, 40, then at most 60 seconds. Large unexplained detours still
  require a second consistent result.
- Replacement navigation state can update without an AR floor. AR geometry
  resumes only after fresh road-entry observations and normal tracking and
  placement checks. Checked road approaches remain bounded to 50 m and
  cannot cross mapped major-road barriers; precise AR placement retains its
  independent 20 m limit. Unmapped connections are not manufactured.
- A distance summary remains available when AR guidance is unavailable.
  Expired location evidence never keeps a directional floor arrow visible.

## Device retest

1. **House to SV Tennis Court:** pass the former early-arrival point without
   an arrival overlay. Near the selected pin, remain for at least six seconds
   while accurate fixes confirm proximity. Poor GPS should delay confirmation.
2. **House to SV Clubhouse:** begin online, then turn data off and back on.
   The downloaded route and progress should remain. Where road coverage or
   AR placement is unavailable, expect the distance/map fallback. Reach the
   destination without restarting the app.
3. **SV Clubhouse to SV Tennis Court:** repeatedly choose another accessible
   mapped street. Sustained accurate deviation should request a new route;
   a short GPS outage should not erase all recent evidence. During temporary
   AR tracking loss, the accepted route should update and its geometry should
   return after road entry and tracking recover.

Report the actual results and provide new field logs for any failure. These
changes cannot verify facility entry or supply missing subdivision road data.

## Host verification

- Regression runner: 375 checks passed using cached dependencies with normal
  local build access. Restricted runs hit a package-cache lock and an Evergine
  addon-resolution error before verification; the unrestricted run resolved both.
- ARCore release-readiness declarations: passed.
- Android builds: diagnostic and standalone configurations succeeded with
  zero warnings and zero errors using the existing SDK and cached dependencies.
- Standalone test package: `artifacts/field-test-20261010/RescuAR-20261010.apk`.
  App assemblies are embedded; diagnostic controls and route overrides are
  disabled. This is a debug-signed field-test package, not a published release.
- Package validation: passed native-bridge, ARM64-only ABI, merged manifest,
  ARCore/Vulkan requirements, and build-profile checks. The Windows SDK launcher
  initially failed on a path containing spaces; short tool paths resolved it.
  The native bridge hash matches both supplied field-test manifests.
- APK SHA-256:
  `18268c4359aff8af01ce90636772c4de99119a49fbde0c70ef897cc61061a02b`.
- Package reports are saved beside the APK. Generated packages/reports are
  ignored build artifacts; source changes remain uncommitted.
- Device behavior: pending the retest above. No device, integration, or browser
  automation was performed.
