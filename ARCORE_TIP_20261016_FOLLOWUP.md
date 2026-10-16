# TIP arrival and camera follow-up — 16 October 2026

This records the earlier candidate and its checks. The subsequent full audit,
additional corrections and replacement APK are documented in
`AR_SYSTEM_AUDIT_20261016.md`.

The 17:42 screenshot belongs to the original 17:23 field capture, built from
`188154c`. The arrival and anchor recovery fixes in `36beaf4` were committed
after that capture. This screenshot is not a retest of those fixes.

## Evidence

- At 17:42:17, the device reported 29.8 m from the destination with ±21 m GPS
  accuracy. At 17:42:20, camera tracking and spatial continuity were live, but
  GPS route identity was suspended. The screenshot separately reports ±27 m.
  Route visibility and automatic arrival were held for location uncertainty.
- The camera acquisition worker reported 29.5 FPS at 17:42:18. This measures
  acquisition, not completed GPU conversions or presentation smoothness.
- The same capture records two-second camera conversion fence timeouts at
  17:32:35 and 17:42:21, followed by `VK_ERROR_DEVICE_LOST` at 17:42:21.831.
  Another timeout occurs at 17:45:44 in the third capture. These are direct
  evidence of render-thread stalls; they do not identify every cause of lag.
- The former exception handler disposed the newly submitted external image
  and rewrote its descriptor after a timeout without proving GPU completion.
  It also returned the previous texture after device loss, allowing old pixels
  to be published with a new frame's metadata. The invalid lifetime path is a
  plausible contributor to device loss, not a proven sole cause.

Vulkan requires submitted image uses to finish before destruction and prohibits
updating an ordinary descriptor set while its commands are pending:
[image lifetime](https://docs.vulkan.org/refpages/latest/refpages/source/vkDestroyImage.html),
[descriptor updates](https://docs.vulkan.org/refpages/latest/refpages/source/vkUpdateDescriptorSets.html).

## Changes

- Add **I'm at the destination**, available for a selected destination within
  75 m using a valid location no older than two minutes and uncertainty at most
  50 m. It requires an explicit confirmation dialog and checks the destination,
  session and eligibility again after the dialog. These limits bound availability
  of a self-report; they do not establish GPS arrival.
- Stop guidance after confirmation, display **Arrival Confirmed by You**, and
  log `source=USER_REPORTED`. Route requests are held while its dialog is open
  and after arrival, so background retries cannot resume routing. Automatic arrival continues to require three
  accurate observations over six seconds inside the 30 m accuracy-inclusive
  radius. Its log source is `GPS_VICINITY`.
- Wait at most 4 ms initially for camera conversion, then poll without waiting
  on later draw callbacks. Skip presentation of an unfinished target, retain
  its original metadata and resources, and discard a conversion held longer
  than the existing 750 ms texture freshness limit. A two-second GPU watchdog
  or device loss ends the graphics session; it cannot publish a frozen texture
  as a new frame. Resources remain retained until graphics teardown.
- Limit the conversion target's longest edge to 1280 pixels while retaining
  viewport proportions, UVs and display/projection metadata. The tested
  1080×1901 output becomes 727×1280, about 55% fewer conversion pixels. This
  trades some camera detail for less conversion work; AR geometry is unchanged.
- Add completed-conversion FPS, deferred conversions and maximum conversion
  time to camera telemetry, separately from acquisition FPS.

## Verification and required device retest

433 local regression checks passed. The final Release build completed with
zero warnings and errors; source manifest compatibility and the packaged ARM64
native bridge checks passed. The full package inspector stopped at the local
`apkanalyzer` Java launcher (`ClassNotFoundException: Files`) after one retry;
its remaining manifest assertions were not completed. Build-profile and native
ABI assertions completed before that failure. Detailed evidence is supplied
with the candidate APK. No device, integration or E2E test was performed. GPU
behavior remains unverified on the affected Samsung phone.

1. Repeat the TIP walk with A* and MLD, keeping capture running through entry
   into the destination. With uncertain GPS, automatic confirmation should
   remain held and **I'm at the destination** should be available nearby.
2. Choose **Keep guiding** first: navigation should continue. Open it again
   and confirm only when physically at the selected destination: the overlay
   should identify user confirmation and rerouting should stop.
3. Cancel the dialog, change destination and reopen the action: it should name
   the newly selected destination. End guidance and start another trip to check
   that its arrival state is reset. Repeat an outdoor arrival with accurate GPS
   to check the separate automatic confirmation.
4. Walk and pan the camera, then leave and reopen the Camera page. Check camera
   smoothness, image proportions, cyan-route registration and recovery. Send
   the capture and actual observations before further behavior-dependent fixes.
