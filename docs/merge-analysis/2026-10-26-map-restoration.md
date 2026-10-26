# Map restoration after comparison with Athena

Compared `migration/athena-modules` (`fa777da`) with integration checkpoint `ac5c60c` and the user's built branch `migration/athena-modules-pt2` (`776aae1`). No branch merge was performed. Both checkouts received the same focused source changes.

## Findings and corrections

| Finding | Correction |
| --- | --- |
| The integration's packaged tile adapter implemented only `ITileSource`. Mapsui 5.1 fetches local tile bytes through `ILocalTileSource`; its fallback otherwise throws `NotImplementedException`. Archive-reader tests alone had missed this library contract. | `PackagedMapTileSource` implements the required local interface. A local check called the actual installed Mapsui tile-fetch conversion and obtained a raster feature from the production adapter and real packaged PNG. |
| The 93,661,024-byte archive and three context files were present in the existing user-built APK. The archive inside that APK matches the pinned SHA-256. A center tile contains an ordinary detailed Marikina street map. | Retained the supplied payload. This was not a missing LFS asset. The Circle browsing center now starts at zoom 14, inside the package's zoom 13–18 range. |
| Athena supplied an application-specific identifier for online OSM tiles; the integration used the library default. That is a plausible cause of the reported 403, not a reproduced device/network diagnosis. | Shared `MapBaseLayer` supplies the existing truthful RescuAR identifier, canonical OSM endpoint, attribution and a seven-day cache inside the dedicated replaceable map-cache directory. No contact address was invented and no tiles were downloaded during verification. |
| The integration added raw routing roads/nodes to Circle and Camera maps; Athena's Camera presentation explicitly removed the routing-road display and focused on shelters, the user and the active route. | Removed these graph-display additions from the two focused screens after user approval. General/Offline Map retains its dev roads/points. Routing datasets, engine, hazards and accepted route overlays remain intact. |
| Circle's integrated self marker came only from shared server telemetry, so sharing Off could omit the current local user. | A separate visible-page location reader supplies a labelled device-local self pin and never uploads or enables sharing. It rejects missing/mock/nonfinite, future (>5 seconds), old (>15 seconds) or coarse (>50 m) readings. A qualifying local self pin replaces the same user's last-shared pin without moving other members. |
| Extra persistent status rows and a fixed People panel reduced map space. | Retained Athena's selector, avatar/phone controls, search cards and tabs; added the approved collapsible People sheet, compact Info panel and explicit local recenter. Membership failures open Info. Camera controls use a compact row. Attribution remains visible. |

The local pin and shared member readings have separate meanings: **You · device** is a current device reading; the People rows retain recorded cloud coordinates/timestamps/battery and cached/unknown labels. Sharing still defaults Off, requires confirmed membership and valid readings, and stops on exit/account or Circle change/app stop. A local fix does not become a route origin or bypass Camera's stricter navigation GPS rules.

The original Athena map-first presentation is retained with the user's approved adaptations. This is not a wholesale restoration of its separate hazard-free router, guessed coordinates/telemetry, displaced avatar coordinates, immediate arrival rule, unscoped storage or local-success cloud fallbacks.

## Verification

- **12 local tile-contract checks:** actual installed Mapsui 5.1/BruTile 6 assemblies; production adapter; real PNG to raster conversion; XYZ schema; missing/cancelled/disabled reads; online request header/URL/attribution/cache. Used already-installed assemblies, no new package dependencies, network requests or UI calls. These are not a device rendering test.
- **421 existing local policy checks**, including six added local-position quality/expiry cases and existing sharing-consent/account/cache checks. All 3,555 packaged entries pass the production archive reader. Fake services and isolated file fixtures; no live backend.
- **41 focused Camera map checks:** same accepted dev route, GPS qualification, viewport preservation, directions and arrival rules.
- **Release Compile with actual XamlC passed, 0 warnings/errors** in `D:/Projects/RescuAR`, with `DesignTimeBuild=false`, `RescuArDiagnosticBuild=false`, `MauiXamlCValidateOnly=false`, `BuildProjectReferences=false`. Existing Release core output was reused. No APK packaging.
- All **11 changed source paths match across checkouts**. All **33 approved AR/navigation reference hashes** remain as recorded, including the existing five approved Camera-body hooks. Admin remains exactly dev's tree `036fd835eb4077192b04b4d9a48d39da3ba58cf4`. Source branches unchanged.

The earlier archive/policy tests and design-time compilation did not demonstrate actual tile rendering. The adapter defect is corrected and its installed-library fetch path now tested, but actual Android display, GPS permissions/lifecycle, and provider availability still require device confirmation.

## Checks after a later user build

1. With Detailed Offline Map On, open Circle and Camera 2D around Marikina, then turn connectivity off. Expect the packaged street map within coverage, not a 403 tile. Outside its partial coverage, online map availability still depends on the provider; inspect Info for coverage status.
2. Circle: allow location with Sharing Off. Expect one local self pin once a qualifying fix is available; other pins must correspond to actual dated members. Expand/collapse People and pan/zoom. Sharing must remain Off. Denied/stale/coarse GPS must not create a substitute pin.
3. Camera: expect named shelter-directory markers, a current qualified user pin, and only the accepted active route. Search/change destinations and switch AR/2D/Flood; expect the existing route/progress/arrival safeguards. No routing-node cloud should cover the map.
4. Background/resume, switch accounts/Circles, and toggle detail Off/On. Expect local pins to clear with page ownership, uploads to remain opt-in, and private data/routing assets to survive Clear Map Cache.

Report actual device results before further behavior-dependent fixes. No app/browser/device/backend test, APK creation, push, deployment or database change was performed.

## Commits

| Change | Integration | Built checkout |
| --- | --- | --- |
| Packaged tile contract, online identifier/cache, focused overlays | `b0ee28c` | `51cac46` |
| Local self pin, map-first layout, focused policy checks | `18d7f47` | `53c5bc8` |

References: [Mapsui 5.1 tile-fetch source](https://github.com/Mapsui/Mapsui/blob/5.1.0/Mapsui.Tiling/Layers/TileLayer.cs), [BruTile 6 local-source interface](https://github.com/BruTile/BruTile/blob/6.0.0/BruTile/ILocalTileSource.cs), [OSM tile policy](https://operations.osmfoundation.org/policies/tiles/).
