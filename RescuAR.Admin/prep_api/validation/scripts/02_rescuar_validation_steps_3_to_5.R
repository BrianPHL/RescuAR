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

project_root <- paste0(
  "C:/Users/Josh/Documents/Project/RescuAR/",
  "RescuAR.Admin"
)

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
TARGET_RECALL <- 0.90
TARGET_PRECISION <- 0.80
TARGET_F1 <- 0.85
TARGET_IOU <- 0.70
TARGET_WAPE <- 0.20
TARGET_SPEARMAN <- 0.80

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

observed_flood <- suppressWarnings(
  st_intersection(
    observed_flood,
    marikina_boundary
  )
)

observed_flood <- st_make_valid(
  observed_flood
)

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
  mutate(
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
      marikina_boundary
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
    "Observed NAMRIA flood area inside Marikina: ",
    round(
      observed_total_m2 / 1e6,
      4
    ),
    " km²"
  ),
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
  "",
  "Project targets:",
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
  "- Manual georeferencing/digitization introduces positional uncertainty.",
  "- Population exposure assumes uniform population distribution within barangays.",
  "- Derived exposed population is not a direct observed affected-person count.",
  "- Areas outside the NAMRIA mapped extent must not be described as safe/non-flooded.",
  "- Internal consistency must be reported separately from external flood-model accuracy."
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
  "\n\nBarangay comparison:\n",
  barangay_output,
  "\n\nText report:\n",
  report_file,
  "\n",
  "====================================================\n",
  sep = ""
)
