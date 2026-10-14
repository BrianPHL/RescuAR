# ============================================================
# RESCUAR / PREP
# CONTROLLED MULTI-EVENT SCENARIO-RULE CALIBRATION
#
# This is NOT a loop that synthesizes data until a desired score
# is reached. It exhaustively evaluates the legitimate 9 LiPAD
# scenario combinations against independent historical events.
# Calibration events select candidates; validation/test events are
# held out and scored only after candidate selection.
# ============================================================

suppressPackageStartupMessages({
  library(sf)
  library(dplyr)
  library(stringr)
  library(readr)
  library(jsonlite)
})

# Primary acceptance: 85% flood-extent accuracy, operationalized as F1.
# Other metrics remain supporting diagnostics and calibration signals.
TARGET_ACCURACY <- 0.85
ACCEPTANCE_METRIC <- "F1"
TARGET_RECALL <- 0.80
TARGET_PRECISION <- 0.80
TARGET_F1 <- TARGET_ACCURACY
TARGET_IOU <- 0.70
TARGET_WAPE <- 0.20
TARGET_SPEARMAN <- 0.80

DEFAULT_RULES <- list(
  Alert = list(return_period = 5, minimum_hazard = "Low"),
  Alarm = list(return_period = 25, minimum_hazard = "Medium"),
  Critical = list(return_period = 100, minimum_hazard = "High")
)

find_project_root <- function(start = getwd()) {
  current <- normalizePath(start, winslash = "/", mustWork = TRUE)
  repeat {
    marker <- file.path(current, "prep_api", "data", "spatial", "baseline_spatial.gpkg")
    if (file.exists(marker)) return(current)
    local_marker <- file.path(current, "data", "spatial", "baseline_spatial.gpkg")
    if (file.exists(local_marker)) return(dirname(current))
    parent <- dirname(current)
    if (identical(parent, current)) stop("Could not locate RescuAR.Admin project root.")
    current <- parent
  }
}

project_root <- find_project_root()
prep_root <- file.path(project_root, "prep_api")
validation_dir <- file.path(prep_root, "validation")
events_csv <- file.path(validation_dir, "events", "events.csv")
calibration_dir <- file.path(validation_dir, "calibration")
spatial_gpkg <- file.path(prep_root, "data", "spatial", "baseline_spatial.gpkg")

dir.create(calibration_dir, recursive = TRUE, showWarnings = FALSE)

normalize_name <- function(x) {
  x |>
    as.character() |>
    stringr::str_to_upper() |>
    stringr::str_replace_all("[^A-Z0-9 ]", " ") |>
    stringr::str_squish()
}

safe_div <- function(a, b) ifelse(is.na(b) | b == 0, NA_real_, a / b)

area_m2 <- function(x) {
  if (length(x) == 0 || all(st_is_empty(x))) return(0)
  sum(as.numeric(st_area(x)), na.rm = TRUE)
}

resolve_path <- function(path) {
  path <- trimws(as.character(path %||% ""))
  if (!nzchar(path)) return(NA_character_)
  if (grepl("^[A-Za-z]:[/\\\\]|^/", path)) return(path)
  file.path(prep_root, path)
}

`%||%` <- function(x, y) {
  if (is.null(x) || length(x) == 0 || is.na(x) || !nzchar(as.character(x))) y else x
}

read_spatial_file <- function(path, preferred_layer = NULL) {
  if (!file.exists(path)) stop("Missing spatial file: ", path)
  if (grepl("\\.gpkg$", path, ignore.case = TRUE)) {
    layers <- st_layers(path)$name
    layer <- if (!is.null(preferred_layer) && preferred_layer %in% layers) preferred_layer else layers[1]
    x <- st_read(path, layer = layer, quiet = TRUE)
  } else {
    x <- st_read(path, quiet = TRUE)
  }
  if (nrow(x) == 0 || is.na(st_crs(x))) stop("Spatial file is empty or has no CRS: ", path)
  st_transform(st_make_valid(x), 32651)
}

detect_hazard_class <- function(h) {
  class_candidates <- c("hazard_class", "severity", "class", "hazard", "hazard_level", "depth_class")
  idx <- which(tolower(names(h)) %in% tolower(class_candidates))
  if (length(idx) == 0) stop("Could not identify hazard class field in LiPAD layer.")
  raw <- h[[names(h)[idx[1]]]]

  if (is.numeric(raw) && all(na.omit(unique(raw)) %in% c(1, 2, 3))) {
    return(case_when(raw == 1 ~ "low", raw == 2 ~ "medium", raw == 3 ~ "high", TRUE ~ NA_character_))
  }

  txt <- tolower(trimws(as.character(raw)))
  case_when(
    grepl("high", txt) ~ "high",
    grepl("medium|moderate|med", txt) ~ "medium",
    grepl("low", txt) ~ "low",
    TRUE ~ NA_character_
  )
}

barangays <- st_read(spatial_gpkg, layer = "barangays", quiet = TRUE) |>
  st_make_valid() |>
  st_transform(32651)

name_candidates <- c("barangay_name", "barangay", "name", "brgy_name", "brgy")
name_idx <- which(tolower(names(barangays)) %in% tolower(name_candidates))
if (length(name_idx) == 0) stop("Could not identify barangay-name field.")
barangays$barangay_name <- as.character(barangays[[names(barangays)[name_idx[1]]]])
barangays$name_key <- normalize_name(barangays$barangay_name)
barangays$barangay_area_m2 <- as.numeric(st_area(barangays))
marikina_boundary <- st_union(st_geometry(barangays))

hazards <- list()
for (rp in c(5, 25, 100)) {
  h <- st_read(spatial_gpkg, layer = paste0("hazard_", rp, "yr"), quiet = TRUE) |>
    st_make_valid() |>
    st_transform(32651)
  h$derived_hazard_class <- detect_hazard_class(h)
  hazards[[as.character(rp)]] <- h
}

scenario_grid <- expand.grid(
  return_period = c(5, 25, 100),
  minimum_hazard = c("Low", "Medium", "High"),
  stringsAsFactors = FALSE
)

build_model_geom <- function(rp, minimum_hazard, domain) {
  keep <- switch(
    minimum_hazard,
    Low = c("low", "medium", "high"),
    Medium = c("medium", "high"),
    High = "high"
  )
  h <- hazards[[as.character(rp)]]
  h <- h[h$derived_hazard_class %in% keep, ]
  geom <- st_union(st_geometry(h))
  geom <- suppressWarnings(st_intersection(st_make_valid(geom), domain))
  st_make_valid(geom)
}

barangay_fraction <- function(geom) {
  ints <- suppressWarnings(
    st_intersection(
      barangays[, c("barangay_name", "name_key")],
      st_sf(geometry = geom)
    )
  )

  if (nrow(ints) > 0) {
    ints$area_m2 <- as.numeric(st_area(ints))
    grouped <- ints |>
      st_drop_geometry() |>
      group_by(barangay_name, name_key) |>
      summarise(area_m2 = sum(area_m2, na.rm = TRUE), .groups = "drop")
  } else {
    grouped <- data.frame(barangay_name = character(), name_key = character(), area_m2 = numeric())
  }

  barangays |>
    st_drop_geometry() |>
    select(barangay_name, name_key, barangay_area_m2) |>
    left_join(grouped, by = c("barangay_name", "name_key")) |>
    mutate(
      area_m2 = coalesce(area_m2, 0),
      fraction = pmin(1, pmax(0, area_m2 / barangay_area_m2))
    )
}

evaluate_event_candidate <- function(event, rp, minimum_hazard) {
  observed_path <- resolve_path(event$observed_flood_path)
  demographics_path <- resolve_path(event$demographics_path)
  domain_path <- resolve_path(event$observation_domain_path)

  observed <- read_spatial_file(observed_path, "observed_flood")
  observed_geom <- st_union(st_geometry(observed))

  if (!is.na(domain_path) && file.exists(domain_path)) {
    domain <- read_spatial_file(domain_path, "observation_domain")
    domain_geom <- suppressWarnings(st_intersection(st_union(st_geometry(domain)), marikina_boundary))
  } else {
    domain_geom <- marikina_boundary
  }
  domain_geom <- st_make_valid(domain_geom)

  observed_geom <- suppressWarnings(st_intersection(observed_geom, domain_geom))
  observed_geom <- st_make_valid(observed_geom)
  model_geom <- build_model_geom(rp, minimum_hazard, domain_geom)

  tp <- suppressWarnings(st_intersection(model_geom, observed_geom))
  tp_m2 <- area_m2(tp)
  model_m2 <- area_m2(model_geom)
  observed_m2 <- area_m2(observed_geom)
  fp_m2 <- max(0, model_m2 - tp_m2)
  fn_m2 <- max(0, observed_m2 - tp_m2)

  precision <- safe_div(tp_m2, tp_m2 + fp_m2)
  recall <- safe_div(tp_m2, tp_m2 + fn_m2)
  f1 <- if (is.na(precision) || is.na(recall) || precision + recall == 0) NA_real_ else 2 * precision * recall / (precision + recall)
  iou <- safe_div(tp_m2, tp_m2 + fp_m2 + fn_m2)

  demographics <- readr::read_csv(demographics_path, show_col_types = FALSE, progress = FALSE)
  required_demo <- c("barangay_name", "population_2020", "children_2020")
  if (!all(required_demo %in% names(demographics))) {
    stop("Demographics file must contain: ", paste(required_demo, collapse = ", "), " | ", demographics_path)
  }
  demographics$name_key <- normalize_name(demographics$barangay_name)

  observed_frac <- barangay_fraction(observed_geom) |>
    select(name_key, observed_fraction = fraction)
  model_frac <- barangay_fraction(model_geom) |>
    select(name_key, model_fraction = fraction)

  comparison <- demographics |>
    select(name_key, population_2020, children_2020) |>
    left_join(observed_frac, by = "name_key") |>
    left_join(model_frac, by = "name_key") |>
    mutate(
      observed_fraction = coalesce(observed_fraction, 0),
      model_fraction = coalesce(model_fraction, 0),
      reference_population = round(population_2020 * observed_fraction),
      modeled_population = round(population_2020 * model_fraction),
      reference_children = round(children_2020 * observed_fraction),
      modeled_children = round(children_2020 * model_fraction),
      population_error = modeled_population - reference_population,
      children_error = modeled_children - reference_children
    )

  pop_wape <- safe_div(sum(abs(comparison$population_error)), sum(comparison$reference_population))
  pop_spearman <- suppressWarnings(cor(comparison$modeled_population, comparison$reference_population, method = "spearman"))
  child_wape <- safe_div(sum(abs(comparison$children_error)), sum(comparison$reference_children))
  child_spearman <- suppressWarnings(cor(comparison$modeled_children, comparison$reference_children, method = "spearman"))

  accuracy_rate_met <- isTRUE(f1 >= TARGET_ACCURACY)

  supporting_targets_met <-
    isTRUE(precision >= TARGET_PRECISION) &&
    isTRUE(recall >= TARGET_RECALL) &&
    isTRUE(iou >= TARGET_IOU) &&
    isTRUE(pop_wape <= TARGET_WAPE) &&
    isTRUE(pop_spearman >= TARGET_SPEARMAN)

  # Kept for downstream compatibility. This field is the primary
  # 85% F1 acceptance gate, not a claim that every diagnostic passed.
  all_targets_met <- accuracy_rate_met

  data.frame(
    event_id = event$event_id,
    event_name = event$event_name,
    event_date = event$event_date,
    role = event$role,
    trigger_state = event$trigger_state,
    return_period = rp,
    minimum_hazard = minimum_hazard,
    observed_area_km2 = observed_m2 / 1e6,
    tp_km2 = tp_m2 / 1e6,
    fp_km2 = fp_m2 / 1e6,
    fn_km2 = fn_m2 / 1e6,
    precision = precision,
    recall = recall,
    f1 = f1,
    iou_csi = iou,
    population_wape = pop_wape,
    population_spearman = pop_spearman,
    children_wape = child_wape,
    children_spearman = child_spearman,
    accuracy_rate_met = accuracy_rate_met,
    supporting_targets_met = supporting_targets_met,
    all_targets_met = all_targets_met,
    observation_domain_supplied = !is.na(domain_path) && file.exists(domain_path),
    stringsAsFactors = FALSE
  )
}

if (!file.exists(events_csv)) stop("Missing event registry: ", events_csv)
events <- readr::read_csv(events_csv, show_col_types = FALSE, progress = FALSE)

required_event_cols <- c(
  "event_id", "event_name", "event_date", "role", "trigger_state",
  "observed_flood_path", "demographics_path", "observation_domain_path"
)
if (!all(required_event_cols %in% names(events))) {
  stop("events.csv must contain: ", paste(required_event_cols, collapse = ", "))
}
if (nrow(events) == 0) {
  stop("events.csv has no historical events. Add independent events before calibration.")
}

valid_roles <- c("calibration", "validation", "test", "reference_only")
events$role <- tolower(trimws(events$role))
if (any(!events$role %in% valid_roles)) {
  stop("Invalid event role. Allowed: ", paste(valid_roles, collapse = ", "))
}

events$trigger_state <- tools::toTitleCase(tolower(trimws(events$trigger_state)))
events$trigger_state[!events$trigger_state %in% c("Alert", "Alarm", "Critical")] <- NA_character_

cat("Evaluating ", nrow(events), " historical event(s) across 9 scenario candidates...\n", sep = "")

all_metrics <- bind_rows(lapply(seq_len(nrow(events)), function(i) {
  event <- as.list(events[i, ])
  bind_rows(lapply(seq_len(nrow(scenario_grid)), function(j) {
    evaluate_event_candidate(
      event,
      scenario_grid$return_period[j],
      scenario_grid$minimum_hazard[j]
    )
  }))
}))

metrics_path <- file.path(calibration_dir, "event_candidate_metrics.csv")
write.csv(all_metrics, metrics_path, row.names = FALSE)

score_ratio <- function(value, target, higher_is_better = TRUE) {
  if (is.na(value) || !is.finite(value)) return(0)
  if (higher_is_better) min(value / target, 1) else min(target / max(value, .Machine$double.eps), 1)
}

candidate_scores <- bind_rows(lapply(c("Alert", "Alarm", "Critical"), function(state) {
  rows <- all_metrics |>
    filter(role == "calibration", trigger_state == state)

  if (nrow(rows) == 0) return(data.frame())

  rows |>
    group_by(return_period, minimum_hazard) |>
    summarise(
      event_count = n_distinct(event_id),
      target_pass_rate = mean(accuracy_rate_met, na.rm = TRUE),
      mean_precision = mean(precision, na.rm = TRUE),
      mean_recall = mean(recall, na.rm = TRUE),
      mean_f1 = mean(f1, na.rm = TRUE),
      mean_iou = mean(iou_csi, na.rm = TRUE),
      mean_population_wape = mean(population_wape, na.rm = TRUE),
      mean_population_spearman = mean(population_spearman, na.rm = TRUE),
      .groups = "drop"
    ) |>
    rowwise() |>
    mutate(
      trigger_state = state,
      composite_score =
        0.15 * score_ratio(mean_precision, TARGET_PRECISION, TRUE) +
        0.25 * score_ratio(mean_recall, TARGET_RECALL, TRUE) +
        0.20 * score_ratio(mean_f1, TARGET_F1, TRUE) +
        0.15 * score_ratio(mean_iou, TARGET_IOU, TRUE) +
        0.15 * score_ratio(mean_population_wape, TARGET_WAPE, FALSE) +
        0.10 * score_ratio(mean_population_spearman, TARGET_SPEARMAN, TRUE)
    ) |>
    ungroup()
}))

candidate_scores_path <- file.path(calibration_dir, "calibration_candidate_scores.csv")
write.csv(candidate_scores, candidate_scores_path, row.names = FALSE)

selected_rows <- list()
selected_rules <- DEFAULT_RULES
selection_notes <- list()

for (state in c("Alert", "Alarm", "Critical")) {
  rows <- candidate_scores |> filter(trigger_state == state)
  if (nrow(rows) == 0) {
    selection_notes[[state]] <- "No calibration events with this trigger state; retained provisional default."
    next
  }

  best <- rows |>
    arrange(
      desc(target_pass_rate),
      desc(composite_score),
      desc(mean_iou),
      desc(mean_recall),
      desc(mean_precision)
    ) |>
    slice_head(n = 1)

  selected_rules[[state]] <- list(
    return_period = as.numeric(best$return_period),
    minimum_hazard = as.character(best$minimum_hazard)
  )
  selected_rows[[state]] <- best
  selection_notes[[state]] <- paste0(
    "Selected from ", best$event_count,
    " calibration event(s); target pass rate=",
    round(best$target_pass_rate * 100, 1), "%"
  )
}

selected_table <- bind_rows(selected_rows)
selected_table_path <- file.path(calibration_dir, "selected_calibration_rules.csv")
write.csv(selected_table, selected_table_path, row.names = FALSE)

holdout <- bind_rows(lapply(c("Alert", "Alarm", "Critical"), function(state) {
  rule <- selected_rules[[state]]
  all_metrics |>
    filter(
      role %in% c("validation", "test"),
      trigger_state == state,
      return_period == rule$return_period,
      minimum_hazard == rule$minimum_hazard
    )
}))

holdout_path <- file.path(calibration_dir, "holdout_selected_rule_scores.csv")
write.csv(holdout, holdout_path, row.names = FALSE)

calibration_event_count <- length(unique(all_metrics$event_id[all_metrics$role == "calibration"]))
holdout_event_count <- length(unique(all_metrics$event_id[all_metrics$role %in% c("validation", "test")]))

rule_config <- list(
  version = paste0("candidate-", format(Sys.time(), "%Y%m%d-%H%M%S")),
  approved = FALSE,
  source = "controlled_multi_event_calibration",
  calibration_event_count = calibration_event_count,
  holdout_event_count = holdout_event_count,
  generated_at = format(Sys.time(), "%Y-%m-%dT%H:%M:%S%z"),
  validation_acceptance = list(
    metric = ACCEPTANCE_METRIC,
    minimum = TARGET_ACCURACY,
    description = "Acceptable flood-extent accuracy is F1 >= 0.85 (85%). Supporting diagnostics are reported separately."
  ),
  notes = paste(
    "UNAPPROVED candidate configuration.",
    "Calibration used only events with role=calibration.",
    "Validation/test events were held out from candidate selection.",
    "Use 05_promote_calibrated_rules.R for explicit gated promotion."
  ),
  selection_notes = selection_notes,
  rules = selected_rules
)

candidate_json <- file.path(calibration_dir, "selected_rules_unapproved.json")
jsonlite::write_json(rule_config, candidate_json, auto_unbox = TRUE, pretty = TRUE, null = "null")

holdout_pass_rate <- if (nrow(holdout) > 0) mean(holdout$all_targets_met, na.rm = TRUE) else NA_real_

report <- c(
  "RESCUAR / PREP — CONTROLLED MULTI-EVENT CALIBRATION REPORT",
  "",
  paste0("Historical events evaluated: ", length(unique(all_metrics$event_id))),
  paste0("Calibration events: ", calibration_event_count),
  paste0("Held-out validation/test events: ", holdout_event_count),
  "",
  "Selected candidate mappings (NOT YET APPROVED FOR PRODUCTION):",
  paste0("Alert -> ", selected_rules$Alert$return_period, "-Year / ", selected_rules$Alert$minimum_hazard, "+"),
  paste0("Alarm -> ", selected_rules$Alarm$return_period, "-Year / ", selected_rules$Alarm$minimum_hazard, "+"),
  paste0("Critical -> ", selected_rules$Critical$return_period, "-Year / ", selected_rules$Critical$minimum_hazard, "+"),
  "",
  if (is.na(holdout_pass_rate)) {
    "No holdout score is available. Candidate rules MUST NOT be promoted as calibrated."
  } else {
    paste0("Held-out 85% F1 accuracy pass rate: ", round(holdout_pass_rate * 100, 1), "%")
  },
  "",
  paste0("Primary acceptance criterion: ", ACCEPTANCE_METRIC, " >= ", TARGET_ACCURACY, " (85%)."),
  "Precision, recall, IoU/CSI, WAPE, and Spearman remain supporting diagnostics/calibration signals.",
  "This process searches only legitimate LiPAD scenario combinations. It does not generate synthetic ground truth, alter NAMRIA observations, or recursively change data to force a target score."
)

writeLines(report, file.path(calibration_dir, "multi_event_calibration_report.txt"))

cat(
  "\nCalibration analysis complete.\n",
  "All event/candidate metrics: ", metrics_path, "\n",
  "Candidate summary: ", candidate_scores_path, "\n",
  "Unapproved selected rules: ", candidate_json, "\n",
  "Holdout scores: ", holdout_path, "\n",
  sep = ""
)
