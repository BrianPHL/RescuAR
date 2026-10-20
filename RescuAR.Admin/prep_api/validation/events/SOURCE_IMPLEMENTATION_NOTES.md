# Espina + Ecological Profile event-data implementation

## Added historical source events

### Ondoy 2009 — Tumana
- Event date: 2009-09-26.
- Source: Espina (2018), Figure 4.1.1.7, page 10.
- Supporting text reports >2.5 m inundation in the eastern portion and a 22.8 m Marikina River level.
- Status: source extracted; georeferencing/digitization pending.

### 2013 Monsoon — Tumana
- Exact day/month not stated by the source.
- Source: Espina (2018), page 9, two related map panels (Figures 4.1.1.5–4.1.1.6).
- Supporting text reports >2 m inundation in eastern Tumana.
- The page contains internally inconsistent figure-title/map-caption labeling, so both panels are retained and no automatic merge is performed.
- Status: source extracted; georeferencing/digitization pending.

## Added Ecological Profile context

The Marikina City Ecological Profile was converted into structured context tables for:
- 2020 population and household totals by barangay;
- land area and gross population density;
- city elevation/slope/river context;
- citywide 0-14 population context;
- MCDRRMO 2018 flood-hazard administrative inventory.

These context files are not independent historical flood extents and are not automatically used to alter LiPAD hazard geometry or the NAMRIA Ulysses reference.

## Why the historical events remain `reference_only`

The embedded source maps are raster figures without native CRS metadata. Automatically treating them as precise GIS ground truth would introduce undocumented positional error. The project therefore stores them as traceable source evidence now and requires a separate QC'd georeferencing/digitization step before they can contribute spatial metrics.
