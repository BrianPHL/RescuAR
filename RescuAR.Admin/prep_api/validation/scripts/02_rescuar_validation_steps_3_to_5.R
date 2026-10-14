# ============================================================
# RESCUAR / PREP
# STEPS 3-5 VALIDATION PIPELINE
#
# Step 3: Regenerate final observed flood fractions by barangay
# Step 4: Build observed_barangay_exposure.csv from PSA 2020
# Step 5: Run spatial + population validation against LiPAD
#
# IMPORTANT:
# - observed_flood.gpkg must already be your FINAL corrected
#   NAMRIA Ulysses flood geometry (including any exclusions/holes).
# - demographics_2020.csv must contain independent PSA 2020
#   barangay population and child population.
# - This script does NOT use PREP estimates to construct the
#   independent reference dataset.
# ============================================================

library(sf)
library(dplyr)
library(stringr)

# ============================================================
# 0. PATHS
# ============================================================

find_project_root <- function(start = getwd()) {
  current <- normalizePath(start, winslash = "/", mustWork = TRUE)

  repeat {
    marker <- file.path(
      current,
      "prep_api",
      "data",
      "spatial",
      "baseline_spatial.gpkg"
    )

    if (file.exists(marker)) return(current)

    # Also support running with prep_api itself as the working directory.
    local_marker <- file.path(
      current,
      "data",
      "spatial",
      "baseline_spatial.gpkg"
    )

    if (file.exists(local_marker)) return(dirname(current))

    parent <- dirname(current)
    if (identical(parent, current)) {
      stop(
        paste(
          "Could not locate the RescuAR.Admin project root.",
          "Open RStudio inside RescuAR.Admin or one of its subfolders."
        )
      )
    }
    current <- parent
  }
}

project_root <- find_project_root()

spatial_gpkg <- file.path(
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

validation_input_dir <- file.path(
  validation_dir,
  "input"
)

validation_output_dir <- file.path(
  validation_dir,
  "output"
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

observed_flood_gpkg <- file.path(
  validation_input_dir,
  "observed_flood.gpkg"
)

demographics_2020_csv <- file.path(
  validation_input_dir,
  "demographics_2020.csv"
)

observed_fraction_csv <- file.path(
  validation_input_dir,
  "observed_barangay_flood_fraction.csv"
)

observed_exposure_csv <- file.path(
  validation_input_dir,
  "observed_barangay_exposure.csv"
)

# Optional observation/survey coverage mask. If present, spatial validation
# is restricted to this independently documented domain so unobserved areas
# are not silently treated as confirmed dry. Supported filenames:
#   observation_domain.gpkg
#   observation_domain.geojson
observation_domain_candidates <- c(
  file.path(validation_input_dir, "observation_domain.gpkg"),
  file.path(validation_input_dir, "observation_domain.geojson")
)

# ============================================================
# 1. CONFIGURATION
# ============================================================

# If automatic hazard-field detection fails, set one of these
# to the exact column name in the hazard layers.
HAZARD_CLASS_COL <- NULL
HAZARD_DEPTH_COL <- NULL

# Event metadata
EVENT_NAME <- "Typhoon Ulysses (Vamco)"
EVENT_DATE <- "2020-11-12"

# Project acceptance criteria (NOT universal official standards)
#
# The project's acceptable flood-extent accuracy rate is 85%,
# operationalized as F1 >= 0.85. F1 is used instead of raw pixel
# accuracy because flooded/non-flooded areas are spatially imbalanced.
# Precision, recall, IoU/CSI, WAPE, and Spearman remain supporting
# diagnostics and are reported separately; they do not override the
# primary 85% F1 acceptance decision.
TARGET_ACCURACY <- 0.85
ACCEPTANCE_METRIC <- "F1"
TARGET_RECALL <- 0.80
TARGET_PRECISION <- 0.80
TARGET_F1 <- TARGET_ACCURACY
TARGET_IOU <- 0.70
TARGET_WAPE <- 0.20
TARGET_SPEARMAN <- 0.80

# Frozen, retrospective 80/20 geographic partition. Create exactly once by
# sourcing 06_create_spatial_partition.R. The historical Ulysses study has
# already informed scenario comparisons; this is NOT an untouched blind test.
REQUIRE_FROZEN_SPATIAL_PARTITION <- TRUE
partition_path <- file.path(validation_dir, 'partitions', 'ulysses_spatial_partition.gpkg')
partition_manifest_path <- file.path(validation_dir, 'partitions', 'ulysses_spatial_partition_manifest.csv')

# ============================================================
# 2. HELPERS
# ============================================================

normalize_name <- function(x) {
  x |>
    as.character() |>
    stringr::str_to_upper() |>
    stringr::str_replace_all("[^A-Z0-9 ]", " ") |>
    stringr::str_squish()
}

safe_div <- function(a, b) {
  ifelse(
    is.na(b) | b == 0,
    NA_real_,
    a / b
  )
}

area_m2 <- function(x) {
  sum(
    as.numeric(
      sf::st_area(x)
    ),
    na.rm = TRUE
  )
}

load_optional_observation_domain <- function(paths, target_crs = 32651) {
  path <- paths[file.exists(paths)][1]
  if (length(path) == 0 || is.na(path)) return(NULL)

  domain <- if (grepl("\\.gpkg$", path, ignore.case = TRUE)) {
    layers <- sf::st_layers(path)$name
    layer <- if ("observation_domain" %in% layers) "observation_domain" else layers[1]
    sf::st_read(path, layer = layer, quiet = TRUE)
  } else {
    sf::st_read(path, quiet = TRUE)
  }

  if (nrow(domain) == 0 || is.na(sf::st_crs(domain))) {
    stop("Observation domain exists but is empty or has no CRS: ", path)
  }

  domain <- sf::st_make_valid(domain)
  domain <- sf::st_transform(domain, target_crs)
  attr(domain, "source_path") <- path
  domain
}

# ============================================================
# 3. LOAD BASE SPATIAL DATA
# ============================================================

if (!file.exists(spatial_gpkg)) {
  stop(
    paste0(
      "Missing baseline_spatial.gpkg:\n",
      spatial_gpkg
    )
  )
}

if (!file.exists(observed_flood_gpkg)) {
  stop(
    paste0(
      "Missing final observed_flood.gpkg:\n",
      observed_flood_gpkg,
      "\n\nFinish the NAMRIA tracing/exclusion stage first."
    )
  )
}

barangays <- st_read(
  spatial_gpkg,
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

observed_flood <- st_read(
  observed_flood_gpkg,
  quiet = TRUE
)

observed_flood <- st_make_valid(
  observed_flood
)

observed_flood <- st_transform(
  observed_flood,
  32651
)

# Detect barangay name field
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
      "Could not identify barangay-name field.\nFields: ",
      paste(
        names(barangays_utm),
        collapse = ", "
      )
    )
  )
}

name_col <- names(barangays_utm)[name_idx[1]]

barangays_utm$barangay_name <- as.character(
  barangays_utm[[name_col]]
)

barangays_utm$name_key <- normalize_name(
  barangays_utm$barangay_name
)

marikina_boundary <- st_union(
  st_geometry(
    barangays_utm
  )
)

observation_domain <- load_optional_observation_domain(
  observation_domain_candidates,
  target_crs = 32651
)

if (is.null(observation_domain)) {
  analysis_domain <- marikina_boundary
  analysis_domain_label <- "Full Marikina boundary (no separate observation-domain mask supplied)"
} else {
  analysis_domain <- suppressWarnings(
    st_intersection(
      st_union(st_geometry(observation_domain)),
      marikina_boundary
    )
  )
  analysis_domain <- st_make_valid(analysis_domain)
  analysis_domain_label <- paste0(
    "Independent observation-domain mask: ",
    basename(attr(observation_domain, "source_path"))
  )
}

observed_flood <- suppressWarnings(
  st_intersection(
    observed_flood,
    analysis_domain
  )
)

observed_flood <- st_make_valid(
  observed_flood
)

# Load a separately frozen partition before evaluating any scenarios.
# Validate its union against the actual observation domain to detect stale
# partitions after a change to baseline boundaries or observation coverage.
spatial_partition <- NULL
if (file.exists(partition_path)) {
  spatial_partition <- st_read(partition_path, layer = 'spatial_partition', quiet = TRUE) |>
    st_make_valid() |> st_transform(32651)
  if (!all(c('block_id', 'subset') %in% names(spatial_partition)) ||
      anyDuplicated(spatial_partition$block_id) ||
      !setequal(unique(spatial_partition$subset), c('development', 'holdout'))) {
    stop('Frozen spatial partition contains invalid block IDs or subset labels.')
  }
  if (!file.exists(partition_manifest_path)) stop('Missing frozen spatial partition manifest.')
  manifest <- read.csv(partition_manifest_path, stringsAsFactors = FALSE)
  if (nrow(manifest) != 1 ||
      tolower(as.character(manifest$prior_Ulysses_comparisons[[1]])) != 'true' ||
      tolower(as.character(manifest$independent_blind_holdout[[1]])) != 'false') {
    stop('Partition manifest is missing the retrospective/non-blind disclosure.')
  }
  current_mask <- observation_domain_candidates[file.exists(observation_domain_candidates)][1]
  mask_name <- if (length(current_mask) == 0 || is.na(current_mask)) {
    'full_city_UNCONFIRMED_SURVEY_COVERAGE'
  } else basename(current_mask)
  if (mask_name != manifest$observation_domain_source[[1]]) {
    stop('Observation-domain source changed since the partition was frozen.')
  }
  if (!'partition_gpkg_md5' %in% names(manifest) ||
      !identical(unname(tools::md5sum(partition_path)), manifest$partition_gpkg_md5[[1]])) {
    stop('Frozen spatial partition geometry file changed. Do not adjust the holdout to improve its score.')
  }
  if (!identical(unname(tools::md5sum(spatial_gpkg)), manifest$baseline_spatial_md5[[1]])) {
    stop('Baseline spatial dataset changed since frozen partition creation.')
  }
  if (length(current_mask) > 0 && !is.na(current_mask) &&
      !identical(unname(tools::md5sum(current_mask)), manifest$observation_domain_md5[[1]])) {
    stop('Observation-domain file changed since frozen partition creation.')
  }
  union_partition <- st_union(st_geometry(spatial_partition))
  uncovered_m2 <- as.numeric(st_area(st_sym_difference(union_partition, analysis_domain)))
  if (!is.finite(uncovered_m2) || uncovered_m2 > 10) {
    stop('Frozen partition does not match the current analysis domain (m2): ', uncovered_m2)
  }
  # Area overlaps of distinct blocks would bias confusion-matrix totals.
  summed_area <- sum(as.numeric(st_area(spatial_partition)))
  if (abs(summed_area - as.numeric(st_area(union_partition))) > 10) {
    stop('Frozen spatial blocks overlap in positive area.')
  }
} else if (REQUIRE_FROZEN_SPATIAL_PARTITION) {
  stop('Spatial partition not found. Run 06_create_spatial_partition.R once before Script 02: ', partition_path)
}

# A fixed mask is used for each geographic subset and for the full city.
partition_masks <- list(full_city = analysis_domain)
if (!is.null(spatial_partition)) {
  partition_masks$development <- st_union(st_geometry(spatial_partition[spatial_partition$subset == 'development', ]))
  partition_masks$holdout <- st_union(st_geometry(spatial_partition[spatial_partition$subset == 'holdout', ]))
}

# ============================================================
# STEP 3
# REGENERATE OBSERVED BARANGAY FLOOD FRACTIONS
# ============================================================

cat(
  "\n====================================================\n",
  "STEP 3 — OBSERVED FLOOD FRACTIONS\n",
  "====================================================\n"
)

barangay_area_table <- barangays_utm

barangay_area_table$barangay_area_m2 <- as.numeric(
  st_area(
    barangay_area_table
  )
)

coverage_intersections <- suppressWarnings(
  st_intersection(
    barangay_area_table[, c("barangay_name", "name_key")],
    st_sf(geometry = analysis_domain)
  )
)

if (nrow(coverage_intersections) > 0) {
  coverage_intersections$observation_coverage_area_m2 <- as.numeric(
    st_area(coverage_intersections)
  )
  coverage_by_barangay <- coverage_intersections |>
    st_drop_geometry() |>
    group_by(barangay_name, name_key) |>
    summarise(
      observation_coverage_area_m2 = sum(observation_coverage_area_m2, na.rm = TRUE),
      .groups = "drop"
    )
} else {
  coverage_by_barangay <- data.frame(
    barangay_name = character(0),
    name_key = character(0),
    observation_coverage_area_m2 = numeric(0)
  )
}

observed_intersections <- suppressWarnings(
  st_intersection(
    barangay_area_table[
      ,
      c(
        "barangay_name",
        "name_key"
      )
    ],
    observed_flood
  )
)

if (nrow(observed_intersections) > 0) {

  observed_intersections$observed_flood_area_m2 <- as.numeric(
    st_area(
      observed_intersections
    )
  )

  observed_by_barangay <- observed_intersections |>
    st_drop_geometry() |>
    group_by(
      barangay_name,
      name_key
    ) |>
    summarise(
      observed_flood_area_m2 = sum(
        observed_flood_area_m2,
        na.rm = TRUE
      ),
      .groups = "drop"
    )

} else {

  observed_by_barangay <- data.frame(
    barangay_name = character(0),
    name_key = character(0),
    observed_flood_area_m2 = numeric(0)
  )
}

observed_fraction <- barangay_area_table |>
  st_drop_geometry() |>
  select(
    barangay_name,
    name_key,
    barangay_area_m2
  ) |>
  left_join(
    observed_by_barangay,
    by = c(
      "barangay_name",
      "name_key"
    )
  ) |>
  left_join(
    coverage_by_barangay,
    by = c(
      "barangay_name",
      "name_key"
    )
  ) |>
  mutate(
    observation_coverage_area_m2 = coalesce(
      observation_coverage_area_m2,
      0
    ),
    observation_coverage_fraction = pmin(
      1,
      pmax(
        0,
        observation_coverage_area_m2 / barangay_area_m2
      )
    ),
    observation_coverage_percent = observation_coverage_fraction * 100,
    observed_flood_area_m2 = coalesce(
      observed_flood_area_m2,
      0
    ),
    observed_flood_fraction = pmin(
      1,
      pmax(
        0,
        observed_flood_area_m2 /
          barangay_area_m2
      )
    ),
    observed_flood_percent =
      observed_flood_fraction * 100,
    interpretation = ifelse(
      observed_flood_fraction > 0,
      "overlaps_digitized_NAMRIA_extent",
      "no_overlap_with_digitized_extent_not_confirmed_dry"
    )
  ) |>
  arrange(
    desc(
      observed_flood_fraction
    )
  )

write.csv(
  observed_fraction |>
    select(
      -name_key
    ),
  observed_fraction_csv,
  row.names = FALSE
)

observed_total_m2 <- area_m2(
  observed_flood
)

barangay_total_m2 <- sum(
  observed_fraction$observed_flood_area_m2,
  na.rm = TRUE
)

cat(
  "Observed flood area: ",
  round(observed_total_m2 / 1e6, 4),
  " km²\n",
  "Barangay-summed area: ",
  round(barangay_total_m2 / 1e6, 4),
  " km²\n",
  "Difference: ",
  round(
    abs(observed_total_m2 - barangay_total_m2),
    2
  ),
  " m²\n",
  sep = ""
)

print(
  observed_fraction |>
    select(
      barangay_name,
      observed_flood_fraction,
      observed_flood_percent
    )
)

# ============================================================
# STEP 4
# BUILD demographics_2020.csv IF NEEDED
# ============================================================

cat(
  "\n====================================================\n",
  "STEP 4 — POPULATION REFERENCE DATASET\n",
  "====================================================\n"
)

if (!file.exists(demographics_2020_csv)) {

  demographics_template <- observed_fraction |>
    arrange(
      barangay_name
    ) |>
    transmute(
      barangay_name,
      population_2020 = NA_real_,
      children_2020 = NA_real_,
      population_source = "PSA 2020 Census",
      children_source = "PSA 2020 age/children table",
      notes = ""
    )

  write.csv(
    demographics_template,
    demographics_2020_csv,
    row.names = FALSE
  )

  stop(
    paste0(
      "\nCreated demographics template:\n",
      demographics_2020_csv,
      "\n\nFill population_2020 and children_2020 for all 16 ",
      "barangays using PSA 2020 data, save it, then rerun ",
      "this script."
    )
  )
}

demographics <- read.csv(
  demographics_2020_csv,
  stringsAsFactors = FALSE
)

required_demo_cols <- c(
  "barangay_name",
  "population_2020",
  "children_2020"
)

if (!all(required_demo_cols %in% names(demographics))) {
  stop(
    paste0(
      "demographics_2020.csv must contain:\n",
      paste(
        required_demo_cols,
        collapse = ", "
      )
    )
  )
}

demographics$name_key <- normalize_name(
  demographics$barangay_name
)

joined_reference <- observed_fraction |>
  left_join(
    demographics |>
      select(
        name_key,
        population_2020,
        children_2020,
        any_of(
          c(
            "population_source",
            "children_source",
            "notes"
          )
        )
      ),
    by = "name_key"
  )

missing_demo <- joined_reference |>
  filter(
    is.na(population_2020) |
      is.na(children_2020)
  )

if (nrow(missing_demo) > 0) {

  cat(
    "\nMissing demographics for:\n"
  )

  print(
    missing_demo$barangay_name
  )

  stop(
    "Complete demographics_2020.csv before continuing."
  )
}

# These are DERIVED spatial-exposure references, not direct
# observed affected-person counts.
observed_exposure <- joined_reference |>
  transmute(
    event_name = EVENT_NAME,
    event_date = EVENT_DATE,
    barangay_name = barangay_name,

    observed_exposed_population = round(
      population_2020 *
        observed_flood_fraction
    ),

    observed_exposed_children = round(
      children_2020 *
        observed_flood_fraction
    ),

    reference_source = paste(
      "NAMRIA post-Ulysses flood extent + PSA 2020 demographics"
    ),

    reference_definition = paste(
      "Derived spatial exposure: PSA 2020 barangay population/children",
      "multiplied by independently digitized NAMRIA Ulysses flood-area",
      "fraction; assumes uniform population distribution within each barangay."
    ),

    notes = paste(
      "Derived reference, not a direct count of victims, evacuees,",
      "casualties, or reported affected persons.",
      "NAMRIA survey coverage and manual digitization are incomplete/uncertain."
    )
  )

write.csv(
  observed_exposure,
  observed_exposure_csv,
  row.names = FALSE
)

cat(
  "\nCreated:\n",
  observed_exposure_csv,
  "\n",
  sep = ""
)

# ============================================================
# 5A. HAZARD-CLASS DETECTION
# ============================================================

detect_hazard_class <- function(hazard_sf) {

  nm <- names(hazard_sf)

  # Explicit override
  if (!is.null(HAZARD_CLASS_COL)) {

    if (!HAZARD_CLASS_COL %in% nm) {
      stop(
        paste0(
          "HAZARD_CLASS_COL not found: ",
          HAZARD_CLASS_COL
        )
      )
    }

    raw <- hazard_sf[[HAZARD_CLASS_COL]]

  } else {

    class_candidates <- c(
      "hazard_class",
      "severity",
      "class",
      "hazard",
      "hazard_level",
      "depth_class"
    )

    idx <- which(
      tolower(nm) %in%
        tolower(class_candidates)
    )

    if (length(idx) > 0) {

      raw <- hazard_sf[[nm[idx[1]]]]

    } else {

      raw <- NULL
    }
  }

  if (!is.null(raw)) {

    # Numeric 1/2/3 class support
    if (
      is.numeric(raw) &&
      all(
        na.omit(unique(raw)) %in%
          c(1, 2, 3)
      )
    ) {

      return(
        dplyr::case_when(
          raw == 1 ~ "low",
          raw == 2 ~ "medium",
          raw == 3 ~ "high",
          TRUE ~ NA_character_
        )
      )
    }

    txt <- tolower(
      trimws(
        as.character(raw)
      )
    )

    return(
      dplyr::case_when(
        grepl(
          "high",
          txt
        ) ~ "high",
        grepl(
          "medium|moderate|med",
          txt
        ) ~ "medium",
        grepl(
          "low",
          txt
        ) ~ "low",
        TRUE ~ NA_character_
      )
    )
  }

  # Depth-field fallback
  if (!is.null(HAZARD_DEPTH_COL)) {

    if (!HAZARD_DEPTH_COL %in% nm) {
      stop(
        paste0(
          "HAZARD_DEPTH_COL not found: ",
          HAZARD_DEPTH_COL
        )
      )
    }

    depth <- as.numeric(
      hazard_sf[[HAZARD_DEPTH_COL]]
    )

  } else {

    depth_candidates <- c(
      "depth",
      "depth_m",
      "flood_depth",
      "max_depth",
      "water_depth"
    )

    idx <- which(
      tolower(nm) %in%
        tolower(depth_candidates)
    )

    if (length(idx) == 0) {

      stop(
        paste0(
          "\nCould not automatically identify hazard severity/depth field.\n",
          "Available columns:\n",
          paste(
            nm,
            collapse = ", "
          ),
          "\n\nSet HAZARD_CLASS_COL or HAZARD_DEPTH_COL near the top of the script."
        )
      )
    }

    depth <- as.numeric(
      hazard_sf[[nm[idx[1]]]]
    )
  }

  dplyr::case_when(
    depth >= 0.1 &
      depth <= 0.5 ~ "low",
    depth > 0.5 &
      depth <= 1.5 ~ "medium",
    depth > 1.5 ~ "high",
    TRUE ~ NA_character_
  )
}

# ============================================================
# 5B. BUILD A SCENARIO POLYGON
# ============================================================

build_scenario_polygon <- function(
  layer_name,
  minimum_hazard
) {

  h <- st_read(
    spatial_gpkg,
    layer = layer_name,
    quiet = TRUE
  )

  h <- st_make_valid(
    h
  )

  h <- st_transform(
    h,
    32651
  )

  h$derived_hazard_class <- detect_hazard_class(
    h
  )

  keep_classes <- switch(
    minimum_hazard,
    "Low" = c(
      "low",
      "medium",
      "high"
    ),
    "Medium" = c(
      "medium",
      "high"
    ),
    "High" = c(
      "high"
    ),
    stop(
      "Invalid minimum_hazard"
    )
  )

  h <- h[
    h$derived_hazard_class %in%
      keep_classes,
  ]

  if (nrow(h) == 0) {
    stop(
      paste0(
        "No polygons remained for ",
        layer_name,
        " / ",
        minimum_hazard
      )
    )
  }

  geom <- st_union(
    st_geometry(
      h
    )
  )

  geom <- st_make_valid(
    geom
  )

  geom <- suppressWarnings(
    st_intersection(
      geom,
      analysis_domain
    )
  )

  st_sf(
    geometry = geom
  )
}

# ============================================================
# STEP 5C
# SPATIAL VALIDATION
# ============================================================

cat(
  "\n====================================================\n",
  "STEP 5 — SPATIAL VALIDATION\n",
  "====================================================\n"
)

scenario_grid <- expand.grid(
  return_period = c(
    5,
    25,
    100
  ),
  minimum_hazard = c(
    "Low",
    "Medium",
    "High"
  ),
  stringsAsFactors = FALSE
)

scenario_grid <- scenario_grid |>
  arrange(
    return_period,
    factor(
      minimum_hazard,
      levels = c(
        "Low",
        "Medium",
        "High"
      )
    )
  )

layer_for_rp <- function(rp) {
  paste0(
    "hazard_",
    rp,
    "yr"
  )
}

observed_geom <- st_union(
  st_geometry(
    observed_flood
  )
)

spatial_results <- list()
population_rows <- list()
scenario_barangay_rows <- list()
partition_spatial_rows <- list()
partition_population_rows <- list()

# Area-based, exhaustive 2x2 confusion matrix for each scenario/subset.
# TN is CONDITIONAL on documented observation coverage; if the full city is
# used without a coverage mask, no claim of confirmed non-flooding is made.
evaluate_partition_spatial <- function(model_geom, observed_geom, mask, subset, rp, mh) {
  model_clip <- suppressWarnings(st_intersection(model_geom, mask))
  observed_clip <- suppressWarnings(st_intersection(observed_geom, mask))
  intersection <- suppressWarnings(st_intersection(model_clip, observed_clip))
  tp <- area_m2(intersection)
  pred <- area_m2(model_clip)
  obs <- area_m2(observed_clip)
  domain <- area_m2(mask)
  fp <- max(0, pred - tp)
  fn <- max(0, obs - tp)
  tn <- domain - tp - fp - fn
  if (!is.finite(tn) || tn < -10) stop('Invalid confusion matrix (negative TN) for ', subset)
  tn <- max(0, tn)
  if (abs((tp + fp + fn + tn) - domain) > 10) stop('Confusion matrix areas do not sum to domain.')
  precision <- safe_div(tp, tp + fp)
  recall <- safe_div(tp, tp + fn)
  f1 <- if (is.na(precision) || is.na(recall) || precision + recall == 0) NA_real_ else {
    2 * precision * recall / (precision + recall)
  }
  iou <- safe_div(tp, tp + fp + fn)
  data.frame(
    subset = subset, return_period = rp, minimum_hazard = mh,
    domain_km2 = domain / 1e6, observed_flood_km2 = obs / 1e6,
    predicted_flood_km2 = pred / 1e6,
    tp_km2 = tp / 1e6, fp_km2 = fp / 1e6,
    fn_km2 = fn / 1e6, tn_km2 = tn / 1e6,
    precision = precision, recall = recall, f1 = f1, iou_csi = iou,
    specificity = safe_div(tn, tn + fp),
    overall_area_accuracy = safe_div(tp + tn, domain),
    precision_pass = !is.na(precision) && precision >= TARGET_PRECISION,
    recall_pass = !is.na(recall) && recall >= TARGET_RECALL,
    f1_pass = !is.na(f1) && f1 >= TARGET_F1,
    iou_pass = !is.na(iou) && iou >= TARGET_IOU,
    precision_gap = precision - TARGET_PRECISION,
    recall_gap = recall - TARGET_RECALL,
    f1_gap = f1 - TARGET_F1,
    iou_gap = iou - TARGET_IOU,
    observation_mask_supplied = !is.null(observation_domain),
    retrospective_partition = TRUE,
    independent_blind_event_holdout = FALSE,
    stringsAsFactors = FALSE
  )
}

# For population diagnostics, allocate each barangay's 2020 population to
# the fraction of its ORIGINAL area intersecting the respective subset's
# hazard/reference polygons. This is only a derived uniform-density PROXY.
partition_exposure_by_barangay <- function(geom, mask) {
  clipped <- suppressWarnings(st_intersection(geom, mask))
  intersects <- suppressWarnings(st_intersection(
    barangay_area_table[, c('name_key')], st_sf(geometry = clipped)
  ))
  if (nrow(intersects) > 0) {
    areas <- intersects |> mutate(flood_area_m2 = as.numeric(st_area(intersects))) |>
      st_drop_geometry() |> group_by(name_key) |>
      summarise(flood_area_m2 = sum(flood_area_m2), .groups = 'drop')
  } else {
    areas <- data.frame(name_key = character(), flood_area_m2 = numeric())
  }
  barangay_area_table |> st_drop_geometry() |>
    select(name_key, barangay_area_m2) |>
    left_join(areas, by = 'name_key') |>
    mutate(flood_area_m2 = coalesce(flood_area_m2, 0),
           flooded_fraction_of_barangay = pmin(1, pmax(0, flood_area_m2 / barangay_area_m2))) |>
    left_join(demographics |> select(name_key, population_2020), by = 'name_key') |>
    mutate(exposed_population_proxy = population_2020 * flooded_fraction_of_barangay)
}


for (
  i in seq_len(
    nrow(
      scenario_grid
    )
  )
) {

  rp <- scenario_grid$return_period[i]
  mh <- scenario_grid$minimum_hazard[i]

  layer_name <- layer_for_rp(
    rp
  )

  cat(
    "\nScenario: ",
    rp,
    "-Year / ",
    mh,
    "+\n",
    sep = ""
  )

  model_sf <- build_scenario_polygon(
    layer_name,
    mh
  )

  model_geom <- st_union(
    st_geometry(
      model_sf
    )
  )

  # Evaluate the same fixed scenario on ALL precommitted geographic subsets.
  for (subset in names(partition_masks)) {
    sm <- evaluate_partition_spatial(
      model_geom, observed_geom, partition_masks[[subset]], subset, rp, mh
    )
    partition_spatial_rows[[length(partition_spatial_rows) + 1L]] <- sm
    if (subset != 'full_city') {
      reference_pop <- partition_exposure_by_barangay(observed_geom, partition_masks[[subset]])
      model_pop <- partition_exposure_by_barangay(model_geom, partition_masks[[subset]])
      pp <- reference_pop |> select(name_key, exposed_population_proxy) |>
        rename(reference_proxy = exposed_population_proxy) |>
        left_join(model_pop |> select(name_key, exposed_population_proxy) |>
                    rename(modeled_proxy = exposed_population_proxy), by = 'name_key')
      wape <- safe_div(sum(abs(pp$modeled_proxy - pp$reference_proxy)),
                       sum(pp$reference_proxy))
      spearman <- suppressWarnings(cor(pp$modeled_proxy, pp$reference_proxy, method = 'spearman'))
      partition_population_rows[[length(partition_population_rows) + 1L]] <- data.frame(
        subset = subset, return_period = rp, minimum_hazard = mh,
        population_proxy_wape = wape, population_proxy_spearman = spearman,
        population_wape_pass = !is.na(wape) && wape <= TARGET_WAPE,
        population_spearman_pass = !is.na(spearman) && spearman >= TARGET_SPEARMAN,
        wape_gap = wape - TARGET_WAPE,
        spearman_gap = spearman - TARGET_SPEARMAN,
        reference_type = 'derived_uniform_barangay_density_proxy_NOT_observed_affected_people',
        stringsAsFactors = FALSE
      )
    }
  }

  tp_geom <- suppressWarnings(
    st_intersection(
      model_geom,
      observed_geom
    )
  )

  tp_m2 <- if (
    length(tp_geom) == 0 ||
    all(st_is_empty(tp_geom))
  ) {
    0
  } else {
    area_m2(tp_geom)
  }

  model_m2 <- area_m2(
    model_geom
  )

  observed_m2 <- area_m2(
    observed_geom
  )

  fp_m2 <- max(
    0,
    model_m2 - tp_m2
  )

  fn_m2 <- max(
    0,
    observed_m2 - tp_m2
  )

  precision <- safe_div(
    tp_m2,
    tp_m2 + fp_m2
  )

  recall <- safe_div(
    tp_m2,
    tp_m2 + fn_m2
  )

  f1 <- if (
    is.na(precision) ||
    is.na(recall) ||
    precision + recall == 0
  ) {
    NA_real_
  } else {
    2 *
      precision *
      recall /
      (
        precision +
          recall
      )
  }

  iou <- safe_div(
    tp_m2,
    tp_m2 +
      fp_m2 +
      fn_m2
  )

  spatial_results[[i]] <- data.frame(
    return_period = rp,
    minimum_hazard = mh,
    tp_km2 = tp_m2 / 1e6,
    fp_km2 = fp_m2 / 1e6,
    fn_km2 = fn_m2 / 1e6,
    precision = precision,
    recall = recall,
    f1 = f1,
    iou_csi = iou,
    precision_target_met =
      !is.na(precision) &&
      precision >= TARGET_PRECISION,
    recall_target_met =
      !is.na(recall) &&
      recall >= TARGET_RECALL,
    f1_target_met =
      !is.na(f1) &&
      f1 >= TARGET_F1,
    iou_target_met =
      !is.na(iou) &&
      iou >= TARGET_IOU
  )

  # ----------------------------------------------------------
  # MODELED HAZARD FRACTION PER BARANGAY
  # ----------------------------------------------------------

  model_intersection <- suppressWarnings(
    st_intersection(
      barangay_area_table[
        ,
        c(
          "barangay_name",
          "name_key"
        )
      ],
      model_sf
    )
  )

  model_by_barangay <- model_intersection |>
    mutate(
      model_hazard_area_m2 = as.numeric(
        st_area(
          model_intersection
        )
      )
    ) |>
    st_drop_geometry() |>
    group_by(
      barangay_name,
      name_key
    ) |>
    summarise(
      model_hazard_area_m2 = sum(
        model_hazard_area_m2,
        na.rm = TRUE
      ),
      .groups = "drop"
    )

  per_barangay <- barangay_area_table |>
    st_drop_geometry() |>
    select(
      barangay_name,
      name_key,
      barangay_area_m2
    ) |>
    left_join(
      model_by_barangay,
      by = c(
        "barangay_name",
        "name_key"
      )
    ) |>
    mutate(
      model_hazard_area_m2 = coalesce(
        model_hazard_area_m2,
        0
      ),
      model_hazard_fraction = pmin(
        1,
        pmax(
          0,
          model_hazard_area_m2 /
            barangay_area_m2
        )
      )
    ) |>
    left_join(
      demographics |>
        select(
          name_key,
          population_2020,
          children_2020
        ),
      by = "name_key"
    ) |>
    left_join(
      observed_fraction |>
        select(
          name_key,
          observed_flood_fraction
        ),
      by = "name_key"
    ) |>
    mutate(
      modeled_exposed_population = round(
        population_2020 *
          model_hazard_fraction
      ),
      modeled_exposed_children = round(
        children_2020 *
          model_hazard_fraction
      ),
      reference_exposed_population = round(
        population_2020 *
          observed_flood_fraction
      ),
      reference_exposed_children = round(
        children_2020 *
          observed_flood_fraction
      ),
      population_error =
        modeled_exposed_population -
        reference_exposed_population,
      children_error =
        modeled_exposed_children -
        reference_exposed_children,
      return_period = rp,
      minimum_hazard = mh
    )

  scenario_barangay_rows[[i]] <- per_barangay

  # ----------------------------------------------------------
  # POPULATION METRICS
  # ----------------------------------------------------------

  pop_abs_err <- abs(
    per_barangay$population_error
  )

  child_abs_err <- abs(
    per_barangay$children_error
  )

  pop_mae <- mean(
    pop_abs_err
  )

  pop_rmse <- sqrt(
    mean(
      per_barangay$population_error^2
    )
  )

  pop_wape <- safe_div(
    sum(
      pop_abs_err
    ),
    sum(
      per_barangay$reference_exposed_population
    )
  )

  child_mae <- mean(
    child_abs_err
  )

  child_rmse <- sqrt(
    mean(
      per_barangay$children_error^2
    )
  )

  child_wape <- safe_div(
    sum(
      child_abs_err
    ),
    sum(
      per_barangay$reference_exposed_children
    )
  )

  pop_spearman <- suppressWarnings(
    cor(
      per_barangay$modeled_exposed_population,
      per_barangay$reference_exposed_population,
      method = "spearman"
    )
  )

  child_spearman <- suppressWarnings(
    cor(
      per_barangay$modeled_exposed_children,
      per_barangay$reference_exposed_children,
      method = "spearman"
    )
  )

  modeled_top5_pop <- per_barangay |>
    arrange(
      desc(
        modeled_exposed_population
      )
    ) |>
    slice_head(
      n = 5
    ) |>
    pull(
      name_key
    )

  reference_top5_pop <- per_barangay |>
    arrange(
      desc(
        reference_exposed_population
      )
    ) |>
    slice_head(
      n = 5
    ) |>
    pull(
      name_key
    )

  top5_pop_overlap <- length(
    intersect(
      modeled_top5_pop,
      reference_top5_pop
    )
  )

  modeled_top5_child <- per_barangay |>
    arrange(
      desc(
        modeled_exposed_children
      )
    ) |>
    slice_head(
      n = 5
    ) |>
    pull(
      name_key
    )

  reference_top5_child <- per_barangay |>
    arrange(
      desc(
        reference_exposed_children
      )
    ) |>
    slice_head(
      n = 5
    ) |>
    pull(
      name_key
    )

  top5_child_overlap <- length(
    intersect(
      modeled_top5_child,
      reference_top5_child
    )
  )

  population_rows[[i]] <- data.frame(
    return_period = rp,
    minimum_hazard = mh,

    population_mae = pop_mae,
    population_rmse = pop_rmse,
    population_wape = pop_wape,
    population_spearman = pop_spearman,
    population_top5_overlap = top5_pop_overlap,
    population_top5_overlap_percent =
      top5_pop_overlap / 5,

    children_mae = child_mae,
    children_rmse = child_rmse,
    children_wape = child_wape,
    children_spearman = child_spearman,
    children_top5_overlap = top5_child_overlap,
    children_top5_overlap_percent =
      top5_child_overlap / 5,

    population_wape_target_met =
      !is.na(pop_wape) &&
      pop_wape <= TARGET_WAPE,

    population_spearman_target_met =
      !is.na(pop_spearman) &&
      pop_spearman >= TARGET_SPEARMAN
  )
}

spatial_results <- bind_rows(
  spatial_results
)

population_results <- bind_rows(
  population_rows
)

scenario_barangay <- bind_rows(
  scenario_barangay_rows
)

combined_results <- spatial_results |>
  left_join(
    population_results,
    by = c("return_period", "minimum_hazard")
  ) |>
  mutate(
    accuracy_rate_met =
      !is.na(f1) &
      f1 >= TARGET_ACCURACY,
    supporting_targets_met =
      precision >= TARGET_PRECISION &
      recall >= TARGET_RECALL &
      iou_csi >= TARGET_IOU &
      population_wape <= TARGET_WAPE &
      population_spearman >= TARGET_SPEARMAN,
    # Distinguish actual compliance with ALL six thresholds from the
    # primary F1-only acceptance decision. Do not conflate the two.
    all_targets_met = accuracy_rate_met & supporting_targets_met
  )

# ============================================================
# 5D. SAVE VALIDATION OUTPUTS
# ============================================================

spatial_output <- file.path(
  validation_output_dir,
  "spatial_validation_scenarios.csv"
)

population_output <- file.path(
  validation_output_dir,
  "population_validation_scenarios.csv"
)

barangay_output <- file.path(
  validation_output_dir,
  "population_validation_by_barangay.csv"
)

combined_output <- file.path(
  validation_output_dir,
  "combined_validation_scenarios.csv"
)

write.csv(
  spatial_results,
  spatial_output,
  row.names = FALSE
)

write.csv(
  population_results,
  population_output,
  row.names = FALSE
)

write.csv(
  scenario_barangay |>
    select(
      -name_key
    ),
  barangay_output,
  row.names = FALSE
)

write.csv(
  combined_results,
  combined_output,
  row.names = FALSE
)

partition_spatial <- dplyr::bind_rows(partition_spatial_rows)
partition_population <- dplyr::bind_rows(partition_population_rows)
write.csv(partition_spatial, file.path(validation_output_dir, 'spatial_partition_confusion_matrix.csv'), row.names = FALSE)
write.csv(partition_population, file.path(validation_output_dir, 'spatial_partition_population_proxy.csv'), row.names = FALSE)

# Explicitly show every compliance gap versus the declared NEW thresholds.
full_city_pop_review <- population_results |>
  transmute(subset = 'full_city', return_period, minimum_hazard,
            population_proxy_wape = population_wape,
            population_proxy_spearman = population_spearman)
all_pop_review <- bind_rows(
  partition_population |> select(subset, return_period, minimum_hazard,
                                 population_proxy_wape, population_proxy_spearman),
  full_city_pop_review
)
partition_review <- partition_spatial |>
  left_join(all_pop_review, by = c('subset', 'return_period', 'minimum_hazard')) |>
  dplyr::mutate(
    population_wape_pass = !is.na(population_proxy_wape) & population_proxy_wape <= TARGET_WAPE,
    population_spearman_pass = !is.na(population_proxy_spearman) & population_proxy_spearman >= TARGET_SPEARMAN,
    population_wape_gap = population_proxy_wape - TARGET_WAPE,
    population_spearman_gap = population_proxy_spearman - TARGET_SPEARMAN,
    primary_F1_acceptance = ifelse(f1_pass, 'PASS', 'FAIL'),
    supporting_spatial_all_pass = precision_pass & recall_pass & iou_pass,
    all_six_thresholds_pass = f1_pass & precision_pass & recall_pass & iou_pass &
                              population_wape_pass & population_spearman_pass,
    interpretation = ifelse(
      subset == 'holdout',
      'Retrospective Ulysses spatial holdout; NOT a blind/independent event',
      ifelse(subset == 'development', 'Retrospective development area', 'Original full-city sensitivity analysis')
    )
  )
write.csv(partition_review, file.path(validation_output_dir, 'spatial_partition_acceptance_review.csv'), row.names = FALSE)

# Verify the new full-city 2x2 matrix reproduces the legacy TP, FP and FN.
control <- partition_spatial |> filter(subset == 'full_city') |>
  left_join(spatial_results, by = c('return_period', 'minimum_hazard'), suffix = c('_matrix', '_legacy'))
for (field in c('tp_km2', 'fp_km2', 'fn_km2', 'f1', 'iou_csi')) {
  diff <- abs(control[[paste0(field, '_matrix')]] - control[[paste0(field, '_legacy')]])
  if (any(!is.finite(diff)) || any(diff > 1e-5)) stop('Matrix mismatch with existing validation: ', field)
}

cat(
  "\nSPATIAL VALIDATION RESULTS\n"
)

print(
  spatial_results
)

cat(
  "\nPOPULATION VALIDATION RESULTS\n"
)

print(
  population_results
)

# ============================================================
# 5E. SIMPLE TEXT REPORT
# ============================================================

report_file <- file.path(
  validation_output_dir,
  "ulysses_validation_report.txt"
)

best_iou <- spatial_results |>
  arrange(
    desc(
      iou_csi
    )
  ) |>
  slice_head(
    n = 1
  )

best_population <- population_results |>
  arrange(
    population_wape
  ) |>
  slice_head(
    n = 1
  )

spatial_target_pass <- with(
  best_iou,
  isTRUE(f1 >= TARGET_ACCURACY)
)

population_target_pass <- with(
  best_population,
  isTRUE(population_wape <= TARGET_WAPE) &&
    isTRUE(population_spearman >= TARGET_SPEARMAN)
)

overall_target_pass <- any(combined_results$accuracy_rate_met %in% TRUE, na.rm = TRUE)

validation_decision <- data.frame(
  best_spatial_return_period = best_iou$return_period,
  best_spatial_minimum_hazard = best_iou$minimum_hazard,
  precision = best_iou$precision,
  recall = best_iou$recall,
  f1 = best_iou$f1,
  iou_csi = best_iou$iou_csi,
  tp_km2 = best_iou$tp_km2,
  fp_km2 = best_iou$fp_km2,
  fn_km2 = best_iou$fn_km2,
  lowest_population_wape_return_period = best_population$return_period,
  lowest_population_wape_minimum_hazard = best_population$minimum_hazard,
  population_wape = best_population$population_wape,
  population_spearman = best_population$population_spearman,
  acceptance_metric = ACCEPTANCE_METRIC,
  acceptable_accuracy_rate = TARGET_ACCURACY,
  best_spatial_accuracy_rate = best_iou$f1,
  acceptable_accuracy_rate_met = spatial_target_pass,
  spatial_targets_met_for_best_spatial_scenario = spatial_target_pass,
  population_targets_met_for_lowest_wape_scenario = population_target_pass,
  any_single_scenario_meets_all_targets = any(combined_results$all_targets_met %in% TRUE, na.rm = TRUE),
  overall_targets_met = any(combined_results$all_targets_met %in% TRUE, na.rm = TRUE),
  primary_F1_acceptance_met = overall_target_pass,
  analysis_domain = analysis_domain_label,
  stringsAsFactors = FALSE
)

decision_output <- file.path(
  validation_output_dir,
  "validation_decision.csv"
)

write.csv(validation_decision, decision_output, row.names = FALSE)

report_lines <- c(
  "RESCUAR / PREP — ULYSSES VALIDATION REPORT",
  "",
  "IMPORTANT INTERPRETATION:",
  paste(
    "This evaluates LiPAD reference hazard scenarios against a",
    "NAMRIA-derived Ulysses flood reference."
  ),
  paste(
    "Because Ulysses is not independently assigned here to one",
    "specific LiPAD return-period/severity scenario, the 9-scenario",
    "comparison is a sensitivity analysis, not proof that the",
    "best-scoring scenario is the true Ulysses return period."
  ),
  "",
  paste0(
    "Observed NAMRIA flood area inside analysis domain: ",
    round(
      observed_total_m2 / 1e6,
      4
    ),
    " km²"
  ),
  paste0("Analysis domain: ", analysis_domain_label),
  "",
  "VALIDATION DECISION:",
  if (overall_target_pass) {
    "PASS — at least one evaluated LiPAD scenario reaches the project-defined acceptable spatial accuracy rate of 85% using F1 score."
  } else {
    "DOES NOT YET MEET THE PROJECT ACCEPTABLE ACCURACY RATE — no evaluated LiPAD scenario reaches F1 >= 0.85; investigate the spatial errors and do not tune the independent reference to force a pass."
  },
  "",
  "Best spatial IoU/CSI scenario (descriptive):",
  paste0(
    best_iou$return_period,
    "-Year / ",
    best_iou$minimum_hazard,
    "+ | Precision=",
    round(
      best_iou$precision,
      3
    ),
    " Recall=",
    round(
      best_iou$recall,
      3
    ),
    " F1=",
    round(
      best_iou$f1,
      3
    ),
    " IoU=",
    round(
      best_iou$iou_csi,
      3
    )
  ),
  paste0(
    "Spatial areas: TP=", round(best_iou$tp_km2, 3),
    " km² | FP=", round(best_iou$fp_km2, 3),
    " km² | FN=", round(best_iou$fn_km2, 3), " km²"
  ),
  paste0(
    "Primary acceptable accuracy rate (F1 >= ", TARGET_ACCURACY, "): ",
    ifelse(best_iou$f1 >= TARGET_ACCURACY, "PASS", "FAIL"),
    " | observed F1=", round(best_iou$f1, 3)
  ),
  paste0(
    "Supporting spatial diagnostics: Precision=", ifelse(best_iou$precision >= TARGET_PRECISION, "PASS", "FAIL"),
    " | Recall=", ifelse(best_iou$recall >= TARGET_RECALL, "PASS", "FAIL"),
    " | F1=", ifelse(best_iou$f1 >= TARGET_F1, "PASS", "FAIL"),
    " | IoU=", ifelse(best_iou$iou_csi >= TARGET_IOU, "PASS", "FAIL")
  ),
  "",
  "Lowest population WAPE scenario (descriptive):",
  paste0(
    best_population$return_period,
    "-Year / ",
    best_population$minimum_hazard,
    "+ | WAPE=",
    round(
      best_population$population_wape * 100,
      2
    ),
    "% | Spearman=",
    round(
      best_population$population_spearman,
      3
    )
  ),
  paste0(
    "Population target status: WAPE=", ifelse(best_population$population_wape <= TARGET_WAPE, "PASS", "FAIL"),
    " | Spearman=", ifelse(best_population$population_spearman >= TARGET_SPEARMAN, "PASS", "FAIL")
  ),
  "",
  "Project acceptance and supporting targets:",
  paste0(
    "Primary acceptable spatial accuracy rate: F1 >= ",
    TARGET_ACCURACY,
    " (85%)"
  ),
  "Supporting diagnostics (reported separately from the primary acceptance decision):",
  paste0(
    "Recall >= ",
    TARGET_RECALL
  ),
  paste0(
    "Precision >= ",
    TARGET_PRECISION
  ),
  paste0(
    "F1 >= ",
    TARGET_F1
  ),
  paste0(
    "IoU/CSI >= ",
    TARGET_IOU
  ),
  paste0(
    "Population WAPE <= ",
    TARGET_WAPE * 100,
    "%"
  ),
  paste0(
    "Population Spearman >= ",
    TARGET_SPEARMAN
  ),
  "",
  "Limitations:",
  "- One historical event (Ulysses) is being tested.",
  "- NAMRIA survey/map coverage is incomplete.",
  if (is.null(observation_domain)) {
    "- No independent observation-domain mask was supplied; false-positive interpretation outside mapped/surveyed coverage is therefore uncertain."
  } else {
    "- Spatial metrics were restricted to the supplied observation-domain mask to avoid treating unobserved areas as confirmed dry."
  },
  "- Manual georeferencing/digitization introduces positional uncertainty.",
  "- Population exposure assumes uniform population distribution within barangays.",
  "- Derived exposed population is not a direct observed affected-person count.",
  "- Areas outside the NAMRIA mapped extent must not be described as safe/non-flooded.",
  "- Internal consistency must be reported separately from external flood-model accuracy."
)

# One explicit 2x2 example (not a raster count): 100-Year / Low+.
example <- partition_spatial |> filter(return_period == 100, minimum_hazard == 'Low')
report_lines <- c(
  report_lines,
  '',
  'SPATIAL PARTITION AND CONFUSION MATRIX (km2):',
  'Rows: predicted flooded / predicted dry; columns: reference flooded / reference dry.',
  'TN and overall-area accuracy are CONDITIONAL on complete observation coverage.',
  'Ulysses was previously used for scenario comparison: this spatial holdout is retrospective, NOT blind or independent event validation.',
  paste0('Partition geometry: ', partition_path),
  unlist(lapply(seq_len(nrow(example)), function(k) {
    x <- example[k, ]
    c(
      sprintf('%s | F1=%.3f (%s) | Recall=%.3f (%s) | Precision=%.3f (%s) | IoU=%.3f (%s)',
              x$subset, x$f1, ifelse(x$f1_pass, 'PASS', 'FAIL'),
              x$recall, ifelse(x$recall_pass, 'PASS', 'FAIL'),
              x$precision, ifelse(x$precision_pass, 'PASS', 'FAIL'),
              x$iou_csi, ifelse(x$iou_pass, 'PASS', 'FAIL')),
      sprintf('  predicted flooded: TP=%.3f | FP=%.3f km2', x$tp_km2, x$fp_km2),
      sprintf('  predicted dry:     FN=%.3f | TN=%.3f km2', x$fn_km2, x$tn_km2)
    )
  })),
  '',
  'All nine scenarios and geographic subsets: spatial_partition_confusion_matrix.csv',
  'Threshold gap audit: spatial_partition_acceptance_review.csv',
  'Population split: spatial_partition_population_proxy.csv (derived proxy only; NOT counts of affected people).',
  unlist(lapply(c('development', 'holdout'), function(sub) {
    x <- partition_population |>
      filter(subset == sub, return_period == 100, minimum_hazard == 'Low')
    if (!nrow(x)) return(character(0))
    sprintf('%s population proxy (100-Year/Low+): WAPE=%.2f%% (%s); Spearman=%.3f (%s)',
            sub, x$population_proxy_wape * 100,
            ifelse(x$population_wape_pass, 'PASS', 'FAIL'),
            x$population_proxy_spearman,
            ifelse(x$population_spearman_pass, 'PASS', 'FAIL'))
  })),
  'Support thresholds never override F1-only PRIMARY acceptance. The all_targets_met field is now the conjunction of all six tests.',
  'The full-city 100-Year / Low+ example was selected descriptively from already inspected Ulysses data; do not select a new winning scenario using holdout results.'
)

writeLines(
  report_lines,
  report_file
)

cat(
  "\n====================================================\n",
  "VALIDATION COMPLETE\n",
  "====================================================\n",
  "Observed fractions:\n",
  observed_fraction_csv,
  "\n\nObserved population reference:\n",
  observed_exposure_csv,
  "\n\nSpatial metrics:\n",
  spatial_output,
  "\n\nPopulation metrics:\n",
  population_output,
  "\n\nCombined scenario decision table:\n",
  combined_output,
  "\n\nBarangay comparison:\n",
  barangay_output,
  "\n\nDecision summary:\n",
  decision_output,
  "\n\nText report:\n",
  report_file,
  "\n",
  "====================================================\n",
  sep = ""
)
