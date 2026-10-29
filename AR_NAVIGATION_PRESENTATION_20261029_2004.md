# GPS startup and guidance presentation — 29 October 2026

## Evidence and confirmed causes

Reviewed `Logs/RescuAR_FieldTest_20261029_200439.zip` and the 6m11s recording `Screen_Recording_20261029_201218.mp4`. Offline video review covers 10-second samples across the recording and larger frames around retry, AR entry and overlapping cards. Evidence is in `artifacts/field-review-20261029-2004/`.

The complete SM-A546E capture covers 20:04:43–20:13:10 PHT. Installed APK hash `bbad4d0c9c80a9857ea2dcefcc3da21ea60540fd0e2cb0ba81a24c9bb04646b3` matches the preceding route-lifecycle candidate. Current checkout baseline is `941590f797df8a4fa6d112d69ea7ebaf2861c7a2`; these corrections are uncommitted.

1. **2D startup recovery incorrectly required AR.** At 20:06:39, a 100 m network observation correctly failed the existing 30 m route-planning limit. The page marked GPS retry pending, but its retry trigger required the AR sub-tab and an initialized, running ARCore session. The 2D view intentionally pauses ARCore, so it never retried. The manual retry began at approximately 20:07:28; a fresh 11.5 m GPS fix was selected at 20:07:32 and MLD accepted a 267.3 m route at 20:07:34. A* later accepted a 269.5 m route at 20:10:26. Both views still share the same geographic routing pipeline.
2. **Duplicate fixes falsely switched positioning into recovery.** Eleven `NAV_OBSERVATION` entries rejected a repeated native acquisition as “duplicate or older positioning observation.” Example: at 20:11:15.193 the GPS observation was reliable; at 20:11:17.202 the same acquisition timestamp was processed again and marked held. That second poll was neither new movement nor a new accuracy failure. Repeating it through matching/stability could unnecessarily suppress AR geometry and alternate instruction text.
3. **Two instruction writers competed.** Route progress rendered consolidated instructions, while each status refresh unconditionally rendered geographic recovery instructions. A*'s separate road-access cue could additionally hide the accepted route's instruction card. This created short-lived presentation changes independent of a new route or real turn.
4. **Overlay positions relied on previous measured heights.** Five cards were independent top-aligned children with dynamically assigned margins. A visibility/text-height change could be applied before those margins were recomputed. The 06:10 video frame clearly shows “Mapped route nearby” covering “Confirming your route position.” The no-emergency status updater also set its card visible and immediately hidden on each refresh; corresponding `NAV_UI` transitions occur within the same millisecond.

## Changes

- Retry pending GPS acquisition automatically in both AR and 2D Map. AR heading-only recovery retains its AR readiness requirements. Existing destination identity, active-request, page visibility, cooldown and arrival guards remain.
- Keep the waiting state during background GPS retries. The 2D map explains automatic recovery and does not repeatedly open a blocking route-loading dialog while GPS is still inadequate. A qualified fix clears the waiting state and continues ordinary route calculation. No accuracy limit was relaxed.
- Consume each real acquisition timestamp once per progress-loop lifetime. Startup fixes already used to initialize progress are not counted again. Repeated/older reads do not alter matching, fusion, arrival evidence or position confidence; original timestamps still govern expiry.
- Use the same current-state choice for normal and recovery instruction rendering. Accepted A* instructions remain visible alongside an applicable access notice; genuine hazard, emergency and arrival priority remain.
- Put all five AR status/instruction/locator cards in one naturally measured vertical stack. Remove absolute top offsets and resize callbacks that fed previous measurements back into placement. Resolve emergency-card visibility once per refresh.

## Visualization review

- **AR:** Duplicate-fix rejection and conflicting cards were confirmed sources of unnecessary display changes. Correcting those improves continuity/readability without changing route coordinates, the 0.25 m ribbon width, ground placement, heading, or tracking safeguards. The recording contains actual cyan geometry and logged registration seeds for both trips. It does not establish surveyed road alignment. Real GPS expiry, tracking loss or uncertain alignment must still withhold geometry.
- **2D:** The original blue route competed visually with the blue GPS marker and map labels. The accepted route now uses cyan with a dark teal edge and white halo. Its geometry and fixed route-start marker are unchanged; the blue GPS estimate and uncertainty circle stay visually distinct. Cached route styling adds one simple casing feature per route, with no new sensor, ML or per-frame geometry workload.
- Cold GPS/heading acquisition still delayed first visible AR geometry in this recording (logged first-visible milestones at 20:08:52 and 20:11:25). These changes do not claim to eliminate genuine acquisition/tracking delays or improve physical GPS accuracy.

## Verification

- Release diagnostic APK build passed: **0 warnings, 0 errors**, 1m17.10s.
- **68 focused presentation checks passed:** 2D GPS retry without ARCore, heading-only restrictions, no flood-view retries, XML layout/spacing invariants, duplicate acquisition handling and existing map presentation safeguards. These are host policy/source checks, not a rendered-device layout test.
- **884 alignment-recovery checks passed**, including existing registration, PDR, matching, route identity, hazard and arrival safeguards. Some acquisition checks also run in the focused group; counts are not a distinct-test total.
- Release readiness, full APK manifest/ARM64/native verification and `git diff --check` passed. No UI automation, integration test or physical-device verification was run.
- Candidate: `artifacts/field-test-20261029-guidance-presentation/RescuAR-20261029-Guidance-Presentation.apk`.
- APK SHA-256: `14821a40dd61330edafdc1086e9dfe1114c3a718e477e217819754efbc1def50`.
- Native bridge remains `c744905f5916525c0fe10f7c55b6d7c8b27347d0796c18988d62b8c37073b3b5`. Source hashes and package evidence accompany the APK.

## Required device check

With logging enabled, run on each available phone for online MLD and offline A*. Use the same well-lit outdoor route.

1. Start from 2D Map while GPS is initially unavailable/inaccurate. **Do not tap Retry route.** Expected: the waiting message stays readable; once a fresh fix meets the existing planning limit, routing loads automatically. If no qualified fix arrives, remaining in the waiting state is expected.
2. Switch to AR. Stand/turn for 30 seconds, then walk 20–30 m. Expected: instruction/status/access cards occupy separate rows; an accepted instruction card does not flash off because a locator appears or one GPS fix is reread. Recovery wording may still change when actual positioning/tracking quality changes. Cyan geometry must continue to obey those safeguards.
3. Return to 2D and inspect cyan contrast versus the blue GPS marker. Stop and select another center, then repeat AR/2D switching without restarting. Expected: the new route loads, map and AR retain the same accepted route, and existing arrival confirmation is still required.

Return the capture and actual results before additional behavior-dependent placement changes. Build/policy checks alone do not verify device appearance or physical alignment.
