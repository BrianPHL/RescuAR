# ============================================================
# RESCUAR / PREP
# CLEAN RESTART FROM TRACING
#
# Purpose:
#   Start external validation over from the tracing stage while
#   preserving the already accepted GCP/georeferencing work.
#
# This script:
#   1. Deletes stale observed-flood and validation outputs.
#   2. Loads the accepted georeferenced NAMRIA Ulysses TIFF.
#   3. Builds a lightweight tracing preview.
#   4. Shows an automated flood-edge guide.
#   5. Lets you trace:
#        a) outer flood polygon(s)
#        b) optional internal white/unshaded exclusion holes
#   6. Builds the final observed flood geometry.
#   7. Saves observed_flood.gpkg / observed_flood.geojson.
#   8. Recomputes observed_barangay_flood_fraction.csv.
#
# After this script passes QC:
#   run rescuar_validation_steps_3_to_5.R
#
# IMPORTANT:
#   This script intentionally DOES NOT delete:
#     - namria_control_points.csv
#     - ulysses_namria_gcps.tif
#     - ulysses_namria_georeferenced.tif
# ============================================================

library(sf)
library(terra)
library(dplyr)

# ============================================================
# 0. SETTINGS
# ============================================================

# Number of disconnected OUTER flood polygons to trace.
NUMBER_OF_OUTER_POLYGONS <- 1

# Number of white/unshaded areas INSIDE the outer flood polygon
# that should be removed from the final observed extent.
#
# Set to 0 if there are no meaningful internal exclusions.
NUMBER_OF_EXCLUSION_HOLES <- 0

# Guide tuning.
SHOW_AUTOMATED_TRACE_GUIDE <- TRUE

GUIDE_MIN_RED <- 0.55
GUIDE_MAX_BLUE <- 0.88
GUIDE_MIN_RB_DIFF <- 0.08
GUIDE_MIN_GB_DIFF <- 0.03
GUIDE_RED_GREEN_TOLERANCE <- 0.08

GUIDE_SMOOTH_WINDOW <- 7
GUIDE_SMOOTH_THRESHOLD <- 0.30
GUIDE_MAX_POINTS <- 5000

# ============================================================
# 1. PATHS
# ============================================================

project_root <- paste0(
  "C:/Users/Josh/Documents/Project/RescuAR/",
  "RescuAR.Admin"
)

gpkg <- file.path(
  project_root,
  "prep_api",
  "data",
  "spatial",
  "baseline_spatial.gpkg"
)

validation_dir <- file.path(
  project_root,
  "prep_api",
  "validation"
)

validation_work_dir <- file.path(
  validation_dir,
  "work"
)

validation_input_dir <- file.path(
  validation_dir,
  "input"
)

validation_output_dir <- file.path(
  validation_dir,
  "output"
)

dir.create(
  validation_work_dir,
  recursive = TRUE,
  showWarnings = FALSE
)

dir.create(
  validation_input_dir,
  recursive = TRUE,
  showWarnings = FALSE
)

dir.create(
  validation_output_dir,
  recursive = TRUE,
  showWarnings = FALSE
)

georef_tif <- file.path(
  validation_work_dir,
  "ulysses_namria_georeferenced.tif"
)

if (!file.exists(georef_tif)) {
  stop(
    paste0(
      "Accepted georeferenced TIFF not found:\n",
      georef_tif,
      "\n\nRun the GCP/georeferencing script first."
    )
  )
}

if (!file.exists(gpkg)) {
  stop(
    paste0(
      "baseline_spatial.gpkg not found:\n",
      gpkg
    )
  )
}

# ============================================================
# 2. CLEAN OLD VALIDATION OUTPUTS
#
# Keep GCP / georeferenced raster.
# Remove stale tracing and validation products.
# ============================================================

stale_files <- c(
  file.path(
    validation_input_dir,
    "observed_flood.gpkg"
  ),
  file.path(
    validation_input_dir,
    "observed_flood.geojson"
  ),
  file.path(
    validation_input_dir,
    "observed_barangay_flood_fraction.csv"
  ),
  file.path(
    validation_input_dir,
    "observed_barangay_exposure.csv"
  ),
  file.path(
    validation_work_dir,
    "observed_flood_manual_raw.gpkg"
  ),
  file.path(
    validation_work_dir,
    "observed_flood_exclusion_raw.gpkg"
  ),
  file.path(
    validation_output_dir,
    "spatial_validation_scenarios.csv"
  ),
  file.path(
    validation_output_dir,
    "population_validation_scenarios.csv"
  ),
  file.path(
    validation_output_dir,
    "population_validation_by_barangay.csv"
  ),
  file.path(
    validation_output_dir,
    "ulysses_validation_report.txt"
  )
)

existing_stale <- stale_files[
  file.exists(stale_files)
]

if (length(existing_stale) > 0) {
  file.remove(existing_stale)

  cat(
    "\nDeleted stale validation outputs:\n"
  )

  print(existing_stale)
}

# ============================================================
# 3. LOAD BARANGAYS
# ============================================================

barangays <- st_read(
  gpkg,
  layer = "barangays",
  quiet = TRUE
)

barangays <- st_make_valid(
  barangays
)

barangays_utm <- st_transform(
  barangays,
  32651
)

name_candidates <- c(
  "barangay_name",
  "barangay",
  "name",
  "brgy_name",
  "brgy"
)

name_idx <- which(
  tolower(names(barangays_utm)) %in%
    tolower(name_candidates)
)

if (length(name_idx) == 0) {
  stop(
    paste0(
      "Cannot identify barangay-name field.\nFields: ",
      paste(
        names(barangays_utm),
        collapse = ", "
      )
    )
  )
}

name_col <- names(
  barangays_utm
)[name_idx[1]]

barangays_utm$validation_barangay_name <-
  as.character(
    barangays_utm[[name_col]]
  )

id_candidates <- c(
  "barangay_id",
  "brgy_id",
  "id"
)

id_idx <- which(
  tolower(names(barangays_utm)) %in%
    tolower(id_candidates)
)

if (length(id_idx) > 0) {
  id_col <- names(
    barangays_utm
  )[id_idx[1]]

  barangays_utm$validation_barangay_id <-
    as.character(
      barangays_utm[[id_col]]
    )
} else {
  barangays_utm$validation_barangay_id <-
    sprintf(
      "BRGY_%02d",
      seq_len(
        nrow(barangays_utm)
      )
    )
}

marikina_boundary <- st_union(
  st_geometry(
    barangays_utm
  )
)

marikina_boundary <- st_make_valid(
  marikina_boundary
)

# ============================================================
# 4. LOAD GEOREFERENCED NAMRIA RASTER
# ============================================================

ulysses_full <- rast(
  georef_tif
)

if (nlyr(ulysses_full) < 3) {
  stop(
    "Georeferenced NAMRIA raster must contain at least 3 bands."
  )
}

terraOptions(
  tempdir = validation_work_dir,
  memfrac = 0.25,
  progress = 1
)

# ============================================================
# 5. CREATE LIGHTWEIGHT UTM PREVIEW
# ============================================================

max_preview_dimension <- 2500

largest_dimension <- max(
  nrow(ulysses_full),
  ncol(ulysses_full)
)

aggregate_factor <- max(
  1,
  ceiling(
    largest_dimension /
      max_preview_dimension
  )
)

preview_tif <- file.path(
  validation_work_dir,
  "ulysses_restart_trace_preview_utm.tif"
)

if (file.exists(preview_tif)) {
  file.remove(preview_tif)
}

cat(
  "\nCreating lightweight tracing preview...",
  "\nAggregation factor: ",
  aggregate_factor,
  "\n",
  sep = ""
)

preview <- aggregate(
  ulysses_full,
  fact = aggregate_factor,
  fun = mean,
  na.rm = TRUE,
  filename = preview_tif,
  overwrite = TRUE
)

rm(ulysses_full)
gc(verbose = FALSE)

# ============================================================
# 6. BUILD AUTOMATED TRACE GUIDE
# ============================================================

guide_points <- data.frame(
  x = numeric(0),
  y = numeric(0)
)

if (SHOW_AUTOMATED_TRACE_GUIDE) {

  rgb_max <- max(
    unlist(
      terra::global(
        preview[[1:3]],
        "max",
        na.rm = TRUE
      )
    ),
    na.rm = TRUE
  )

  rgb_scale <- ifelse(
    rgb_max > 2,
    255,
    1
  )

  r_norm <- preview[[1]] / rgb_scale
  g_norm <- preview[[2]] / rgb_scale
  b_norm <- preview[[3]] / rgb_scale

  flood_candidate <- (
    (r_norm >= GUIDE_MIN_RED) &
      (b_norm <= GUIDE_MAX_BLUE) &
      ((r_norm - b_norm) >= GUIDE_MIN_RB_DIFF) &
      ((g_norm - b_norm) >= GUIDE_MIN_GB_DIFF) &
      (r_norm >= (g_norm - GUIDE_RED_GREEN_TOLERANCE))
  )

  flood_candidate <- terra::ifel(
    flood_candidate,
    1,
    0
  )

  city_sf <- st_sf(
    id = 1,
    geometry = marikina_boundary
  )

  city_vect <- terra::vect(
    city_sf
  )

  city_mask <- terra::rasterize(
    city_vect,
    preview[[1]],
    field = 1,
    background = NA
  )

  flood_candidate <- terra::mask(
    flood_candidate,
    city_mask
  )

  smooth_window <- matrix(
    1,
    nrow = GUIDE_SMOOTH_WINDOW,
    ncol = GUIDE_SMOOTH_WINDOW
  )

  flood_probability <- terra::focal(
    flood_candidate,
    w = smooth_window,
    fun = "mean",
    na.rm = TRUE,
    fillvalue = 0
  )

  flood_smoothed <- terra::ifel(
    flood_probability >= GUIDE_SMOOTH_THRESHOLD,
    1,
    0
  )

  neighbor_sum <- terra::focal(
    flood_smoothed,
    w = matrix(
      1,
      nrow = 3,
      ncol = 3
    ),
    fun = "sum",
    na.rm = TRUE,
    fillvalue = 0
  )

  guide_boundary <- terra::ifel(
    (flood_smoothed == 1) &
      (neighbor_sum < 9),
    1,
    NA
  )

  guide_points <- as.data.frame(
    guide_boundary,
    xy = TRUE,
    na.rm = TRUE
  )

  if (
    nrow(guide_points) >
      GUIDE_MAX_POINTS
  ) {

    keep <- unique(
      round(
        seq(
          1,
          nrow(guide_points),
          length.out =
            GUIDE_MAX_POINTS
        )
      )
    )

    guide_points <-
      guide_points[
        keep,
        ,
        drop = FALSE
      ]
  }

  guide_csv <- file.path(
    validation_work_dir,
    "namria_restart_trace_guide_points.csv"
  )

  write.csv(
    guide_points[
      ,
      c(
        "x",
        "y"
      )
    ],
    guide_csv,
    row.names = FALSE
  )

  graphics.off()

  windows(
    width = 12,
    height = 10
  )

  plotRGB(
    preview,
    r = 1,
    g = 2,
    b = 3,
    stretch = "lin",
    axes = TRUE
  )

  plot(
    st_geometry(
      barangays_utm
    ),
    add = TRUE,
    border = "cyan",
    lwd = 1
  )

  if (nrow(guide_points) > 0) {
    points(
      guide_points$x,
      guide_points$y,
      pch = 20,
      col = "limegreen",
      cex = 0.45
    )
  }

  title(
    main =
      "NAMRIA Ulysses — Automated Trace Guide",
    sub =
      "Lime = suggested flood edge | Cyan = barangays"
  )

  cat(
    "\nReview the automated guide.",
    "\nPress ENTER in the Console when ready to trace.\n"
  )

  invisible(
    readline()
  )

  graphics.off()

  rm(
    r_norm,
    g_norm,
    b_norm,
    flood_candidate,
    city_mask,
    flood_probability,
    flood_smoothed,
    neighbor_sum,
    guide_boundary
  )

  gc(verbose = FALSE)
}

# ============================================================
# 7. GENERIC POLYGON TRACING FUNCTION
# ============================================================

trace_polygon <- function(
  polygon_number,
  mode = c(
    "outer",
    "exclusion"
  )
) {

  mode <- match.arg(mode)

  plotRGB(
    preview,
    r = 1,
    g = 2,
    b = 3,
    stretch = "lin",
    axes = TRUE
  )

  plot(
    st_geometry(
      barangays_utm
    ),
    add = TRUE,
    border = "cyan",
    lwd = 0.8
  )

  if (
    SHOW_AUTOMATED_TRACE_GUIDE &&
    nrow(guide_points) > 0
  ) {
    points(
      guide_points$x,
      guide_points$y,
      pch = 20,
      col = "limegreen",
      cex = 0.4
    )
  }

  if (mode == "outer") {
    plot_title <- paste(
      "Trace OUTER Flood Polygon",
      polygon_number
    )

    plot_subtitle <- paste(
      "Trace outer colored flood boundary.",
      "Right-click / Esc to finish."
    )

    line_color <- "magenta"

  } else {

    plot_title <- paste(
      "Trace EXCLUSION / WHITE HOLE",
      polygon_number
    )

    plot_subtitle <- paste(
      "Trace only white/unshaded area INSIDE flood polygon.",
      "Right-click / Esc to finish."
    )

    line_color <- "red"
  }

  title(
    main = plot_title,
    sub = plot_subtitle
  )

  cat(
    "\n====================================================\n",
    toupper(mode),
    " POLYGON ",
    polygon_number,
    "\n",
    "====================================================\n",
    if (mode == "outer") {
      paste0(
        "Trace the OUTER boundary of the NAMRIA ",
        "yellow/orange/red flood shading.\n"
      )
    } else {
      paste0(
        "Trace ONLY an internal white/unshaded ",
        "area that should be excluded.\n"
      )
    },
    "Right-click or press ESC when finished.\n",
    "====================================================\n",
    sep = ""
  )

  clicked <- locator(
    type = "l",
    lwd = 2,
    col = line_color
  )

  if (
    is.null(clicked) ||
    length(clicked$x) < 3
  ) {
    stop(
      paste0(
        mode,
        " polygon ",
        polygon_number,
        " needs at least 3 vertices."
      )
    )
  }

  coords <- cbind(
    clicked$x,
    clicked$y
  )

  if (
    !isTRUE(
      all.equal(
        coords[1, ],
        coords[nrow(coords), ]
      )
    )
  ) {
    coords <- rbind(
      coords,
      coords[1, ]
    )
  }

  st_sfc(
    st_polygon(
      list(coords)
    ),
    crs = 32651
  )
}

# ============================================================
# 8. TRACE OUTER FLOOD POLYGON(S)
# ============================================================

graphics.off()

windows(
  width = 12,
  height = 10
)

outer_polygons <- vector(
  "list",
  NUMBER_OF_OUTER_POLYGONS
)

for (
  i in seq_len(
    NUMBER_OF_OUTER_POLYGONS
  )
) {
  outer_polygons[[i]] <- trace_polygon(
    polygon_number = i,
    mode = "outer"
  )

  cat(
    "\nOuter polygon ",
    i,
    " completed.\n",
    sep = ""
  )
}

graphics.off()

outer_geometries <- lapply(
  outer_polygons,
  function(x) {
    x[[1]]
  }
)

outer_sfc <- st_sfc(
  outer_geometries,
  crs = 32651
)

outer_sfc <- st_make_valid(
  outer_sfc
)

raw_outer <- st_sf(
  polygon_id =
    seq_along(outer_sfc),
  geometry = outer_sfc
)

raw_trace_gpkg <- file.path(
  validation_work_dir,
  "observed_flood_manual_raw.gpkg"
)

if (file.exists(raw_trace_gpkg)) {
  file.remove(raw_trace_gpkg)
}

st_write(
  raw_outer,
  raw_trace_gpkg,
  layer = "raw_manual_trace",
  quiet = TRUE
)

# ============================================================
# 9. TRACE OPTIONAL EXCLUSION HOLES
# ============================================================

raw_exclusions <- NULL

if (
  NUMBER_OF_EXCLUSION_HOLES > 0
) {

  graphics.off()

  windows(
    width = 12,
    height = 10
  )

  exclusion_polygons <- vector(
    "list",
    NUMBER_OF_EXCLUSION_HOLES
  )

  for (
    i in seq_len(
      NUMBER_OF_EXCLUSION_HOLES
    )
  ) {
    exclusion_polygons[[i]] <- trace_polygon(
      polygon_number = i,
      mode = "exclusion"
    )

    cat(
      "\nExclusion polygon ",
      i,
      " completed.\n",
      sep = ""
    )
  }

  graphics.off()

  exclusion_geometries <- lapply(
    exclusion_polygons,
    function(x) {
      x[[1]]
    }
  )

  exclusion_sfc <- st_sfc(
    exclusion_geometries,
    crs = 32651
  )

  exclusion_sfc <- st_make_valid(
    exclusion_sfc
  )

  raw_exclusions <- st_sf(
    exclusion_id =
      seq_along(exclusion_sfc),
    geometry = exclusion_sfc
  )

  exclusion_gpkg <- file.path(
    validation_work_dir,
    "observed_flood_exclusion_raw.gpkg"
  )

  if (file.exists(exclusion_gpkg)) {
    file.remove(exclusion_gpkg)
  }

  st_write(
    raw_exclusions,
    exclusion_gpkg,
    layer = "raw_exclusion_trace",
    quiet = TRUE
  )
}

# ============================================================
# 10. BUILD FINAL OBSERVED FLOOD GEOMETRY
# ============================================================

outer_union <- st_union(
  st_geometry(
    raw_outer
  )
)

outer_union <- st_make_valid(
  outer_union
)

if (
  !is.null(raw_exclusions) &&
  nrow(raw_exclusions) > 0
) {

  exclusion_union <- st_union(
    st_geometry(
      raw_exclusions
    )
  )

  exclusion_union <- st_make_valid(
    exclusion_union
  )

  corrected_flood <- suppressWarnings(
    st_difference(
      outer_union,
      exclusion_union
    )
  )

} else {
  corrected_flood <- outer_union
}

corrected_flood <- st_make_valid(
  corrected_flood
)

corrected_flood <- suppressWarnings(
  st_intersection(
    corrected_flood,
    marikina_boundary
  )
)

corrected_flood <- st_make_valid(
  corrected_flood
)

corrected_flood <- corrected_flood[
  !st_is_empty(
    corrected_flood
  )
]

if (length(corrected_flood) == 0) {
  stop(
    "Final corrected flood geometry is empty."
  )
}

corrected_flood <- st_union(
  corrected_flood
)

# ============================================================
# 11. CREATE FINAL OBSERVED FLOOD FEATURE
# ============================================================

observed_flood <- st_sf(
  event_name =
    "Typhoon Ulysses (Vamco)",

  event_date =
    as.Date("2020-11-12"),

  reference_source =
    "NAMRIA post-Ulysses flood extent map",

  reference_definition =
    paste(
      "Flood footprint manually traced from the",
      "accepted georeferenced NAMRIA post-Ulysses map;",
      "internal white/unshaded exclusions removed where traced."
    ),

  observation_type =
    "derived_from_observed_post_event_map",

  derivation_method =
    "manual_native_raster_click_digitization",

  directly_observed_person_count =
    FALSE,

  limitations =
    paste(
      "NAMRIA survey/map coverage is incomplete;",
      "manual tracing and georeferencing introduce positional uncertainty;",
      "areas outside mapped extent are not confirmed dry/safe."
    ),

  geometry =
    corrected_flood
)

st_crs(
  observed_flood
) <- 32651

# ============================================================
# 12. SAVE FINAL observed_flood.gpkg / GeoJSON
# ============================================================

observed_gpkg <- file.path(
  validation_input_dir,
  "observed_flood.gpkg"
)

observed_geojson <- file.path(
  validation_input_dir,
  "observed_flood.geojson"
)

if (file.exists(observed_gpkg)) {
  file.remove(observed_gpkg)
}

if (file.exists(observed_geojson)) {
  file.remove(observed_geojson)
}

st_write(
  observed_flood,
  observed_gpkg,
  layer = "observed_flood",
  quiet = TRUE
)

st_write(
  st_transform(
    observed_flood,
    4326
  ),
  observed_geojson,
  quiet = TRUE
)

# ============================================================
# 13. CALCULATE OBSERVED FLOOD FRACTION PER BARANGAY
# ============================================================

barangay_areas <- barangays_utm

barangay_areas$barangay_id <-
  barangay_areas$validation_barangay_id

barangay_areas$barangay_name <-
  barangay_areas$validation_barangay_name

barangay_areas$barangay_area_m2 <-
  as.numeric(
    st_area(
      barangay_areas
    )
  )

flood_intersection <- suppressWarnings(
  st_intersection(
    barangay_areas[
      ,
      c(
        "barangay_id",
        "barangay_name"
      )
    ],
    observed_flood
  )
)

if (nrow(flood_intersection) > 0) {

  flood_intersection$observed_flood_area_m2 <-
    as.numeric(
      st_area(
        flood_intersection
      )
    )

  flood_by_barangay <-
    flood_intersection %>%
    st_drop_geometry() %>%
    group_by(
      barangay_id,
      barangay_name
    ) %>%
    summarise(
      observed_flood_area_m2 =
        sum(
          observed_flood_area_m2,
          na.rm = TRUE
        ),
      .groups = "drop"
    )

} else {

  flood_by_barangay <- data.frame(
    barangay_id = character(0),
    barangay_name = character(0),
    observed_flood_area_m2 = numeric(0)
  )
}

barangay_flood_fraction <-
  barangay_areas %>%
  st_drop_geometry() %>%
  select(
    barangay_id,
    barangay_name,
    barangay_area_m2
  ) %>%
  left_join(
    flood_by_barangay,
    by = c(
      "barangay_id",
      "barangay_name"
    )
  ) %>%
  mutate(
    observed_flood_area_m2 =
      coalesce(
        observed_flood_area_m2,
        0
      ),

    observed_flood_fraction =
      pmin(
        1,
        pmax(
          0,
          observed_flood_area_m2 /
            barangay_area_m2
        )
      ),

    observed_flood_percent =
      observed_flood_fraction *
        100,

    interpretation =
      ifelse(
        observed_flood_fraction > 0,
        "overlaps_digitized_NAMRIA_extent",
        "no_overlap_with_digitized_extent_not_confirmed_dry"
      )
  ) %>%
  arrange(
    desc(
      observed_flood_fraction
    )
  )

flood_fraction_csv <- file.path(
  validation_input_dir,
  "observed_barangay_flood_fraction.csv"
)

write.csv(
  barangay_flood_fraction,
  flood_fraction_csv,
  row.names = FALSE,
  fileEncoding = "UTF-8"
)

# ============================================================
# 14. AREA CONSISTENCY CHECK
# ============================================================

total_observed_flood_m2 <- sum(
  as.numeric(
    st_area(
      observed_flood
    )
  )
)

intersection_total_m2 <- sum(
  barangay_flood_fraction$observed_flood_area_m2,
  na.rm = TRUE
)

area_difference_m2 <- abs(
  intersection_total_m2 -
    total_observed_flood_m2
)

cat(
  "\n====================================================\n",
  "AREA CONSISTENCY CHECK\n",
  "====================================================\n",
  "Observed clipped flood: ",
  round(
    total_observed_flood_m2 /
      1e6,
    4
  ),
  " km2\n",
  "Barangay intersection:  ",
  round(
    intersection_total_m2 /
      1e6,
    4
  ),
  " km2\n",
  "Absolute difference:    ",
  round(
    area_difference_m2,
    4
  ),
  " m2\n",
  "====================================================\n",
  sep = ""
)

# ============================================================
# 15. FINAL QC PLOT
# ============================================================

graphics.off()

windows(
  width = 12,
  height = 10
)

plotRGB(
  preview,
  r = 1,
  g = 2,
  b = 3,
  stretch = "lin",
  axes = TRUE
)

plot(
  st_geometry(
    barangays_utm
  ),
  add = TRUE,
  border = "cyan",
  lwd = 1.2
)

plot(
  st_geometry(
    observed_flood
  ),
  add = TRUE,
  border = "magenta",
  lwd = 3
)

title(
  main =
    "FINAL NAMRIA Ulysses Observed Flood Reference",
  sub =
    "Cyan = barangays | Magenta = final observed flood extent"
)

# ============================================================
# 16. PRINT FINAL STATUS
# ============================================================

cat(
  "\n====================================================\n",
  "TRACING RESTART COMPLETE\n",
  "====================================================\n"
)

print(
  barangay_flood_fraction[
    ,
    c(
      "barangay_name",
      "observed_flood_fraction",
      "observed_flood_percent"
    )
  ]
)

cat(
  "\nFiles created:\n",
  observed_gpkg,
  "\n",
  observed_geojson,
  "\n",
  flood_fraction_csv,
  "\n\n",
  "NEXT STEP:\n",
  "Inspect the final magenta QC outline.",
  "\nIf correct, run rescuar_validation_steps_3_to_5.R.",
  "\n====================================================\n",
  sep = ""
)
