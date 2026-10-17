# RescuAR.Admin — Hybrid Bootstrap + Custom CSS Responsive Patch

## Apply

1. Back up or commit your current `RescuAR.Admin` files.
2. Copy the `RescuAR.Admin/` directory from this patch archive **over your existing project**, preserving the relative paths. Overwrite only files included in this patch.
3. From the RescuAR.Admin project root, run:

   ```powershell
   npm install
   npm run build
   npm run dev
   ```

4. Test responsive behavior using your browser's device toolbar at approximately **375px, 768px, 1024px, and 1440px**, and verify all navigation, reports, charts, exports, and modals.

## Implementation

- Loads **Bootstrap 5.3.3 grid and utilities only** from pinned jsDelivr CSS URLs in `index.html`; it intentionally avoids Bootstrap Reboot/component CSS and JavaScript to keep the existing RescuAR design intact.
- Preserves the stationary desktop header/sidebar and independently scrollable content pane.
- Replaces the phone/tablet stacked sidebar with an accessible, dismissible drawer (menu toggle, backdrop, Escape key; collapsed navigation is inert).
- Uses Bootstrap responsive columns for monitoring-point cards, monitoring charts/tables, river-level metrics, dashboard statistics, hazard-context cards, hazard rules, and user summary cards.
- Adds adaptive controls, safer table overflow, stacked management panels, and viewport-fitted form drawers in `src/responsive.css`.
- Retains existing data retrieval, Supabase integrations, maps, reports, and modal behavior.

## Files to replace / add

```text
RescuAR.Admin/
├── index.html                             (replace: Bootstrap CSS links)
└── src/
    ├── main.jsx                           (replace: responsive CSS import)
    ├── App.jsx                            (replace: mobile navigation state/behavior)
    ├── responsive.css                     (NEW: responsive overrides)
    └── components/
        ├── Header.jsx                     (replace: mobile menu toggle)
        ├── Sidebar.jsx                    (replace: mobile drawer and semantic nav)
        ├── Dashboard.jsx                  (replace: responsive stat cards)
        ├── MonitoringStations.jsx         (replace: responsive station layouts)
        ├── RiverLevel.jsx                (replace: responsive metric cards)
        ├── HazardExposure.jsx             (replace: responsive context/rules)
        ├── ReportsModeration.jsx          (replace: responsive details panel class)
        ├── UserManagement.jsx             (replace: responsive list and summary)
        ├── EvacuationCenters.jsx          (replace: responsive list and drawer)
        ├── EmergencyHotlines.jsx          (replace: responsive list and drawer)
        └── Advisories.jsx                 (replace: mobile-sized drawer)
```

## Compatibility and caveats

- **No changes to package.json, package-lock.json, database, worker, PREP, or environment variables.** Bootstrap is a CSS-only, CDN-loaded enhancement; `npm install` is for the project's existing dependencies, not a new Bootstrap package.
- The device must be able to reach `cdn.jsdelivr.net` to load Bootstrap CSS. For offline or strict Content Security Policy environments, download and self-host those two pinned Bootstrap stylesheets and update `index.html` paths.
- JavaScript/JSX syntax and responsive CSS parsing were validated. A full production build could not be run in the testing container because the uploaded `node_modules` contains Windows-specific Rolldown native bindings; perform the `npm run build` check in your own project environment after a normal `npm install`.
- This patch targets interface responsiveness. It does not alter network capacity, historical water-level ingestion, or data accuracy.
