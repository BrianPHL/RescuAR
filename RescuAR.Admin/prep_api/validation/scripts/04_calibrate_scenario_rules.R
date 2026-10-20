# ============================================================
# RESCUAR / PREP
# CONTROLLED MULTI-EVENT SCENARIO-RULE CALIBRATION
#
# This is NOT a loop that synthesizes data until a desired score
# is reached. It exhaustively evaluates the legitimate 9 LiPAD
# scenario combinations against independent historical events.
# Calibration events select candidates; validation/test events are
# held out and scored only after candidate selection. Source-only
# historical records remain registered and are skipped until a QC'd
# observed flood geometry is available.
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

mean_na <- function(x) {
  if (length(x) == 0 || all(is.na(x))) return(NA_real_)
  mean(x, na.rm = TRUE)
}

detect_demo_column <- function(names_vec, stem) {
  exact <- which(tolower(names_vec) == tolower(stem))
  if (length(exact) > 0) return(names_vec[exact[1]])
  dated <- grep(paste0("^", stem, "_[0-9]{4}$"), names_vec, ignore.case = TRUE, value = TRUE)
  if (length(dated) > 0) return(dated[1])
  NA_character_
}

weighted_candidate_score <- function(precision, recall, f1, iou, wape, spearman) {
  scores <- c(
    score_ratio(precision, TARGET_PRECISION, TRUE),
    score_ratio(recall, TARGET_RECALL, TRUE),
    score_ratio(f1, TARGET_F1, TRUE),
    score_ratio(iou, TARGET_IOU, TRUE),
    score_ratio(wape, TARGET_WAPE, FALSE),
    score_ratio(spearman, TARGET_SPEARMAN, TRUE)
  )
  weights <- c(0.15, 0.25, 0.20, 0.15, 0.15, 0.10)
  ok <- is.finite(scores)
  if (!any(ok)) return(NA_real_)
  sum(scores[ok] * weights[ok]) / sum(weights[ok])
}

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

  # Population/exposure metrics are optional for historical events.
  # This allows older spatial references (e.g. Ondoy/2013 Tumana)
  # to contribute spatial skill metrics without incorrectly reusing
  # modern demographics as event-year ground truth.
  pop_wape <- NA_real_
  pop_spearman <- NA_real_
  child_wape <- NA_real_
  child_spearman <- NA_real_
  population_metrics_available <- FALSE
  children_metrics_available <- FALSE
  demographics_supplied <- !is.na(demographics_path) && file.exists(demographics_path)

  if (demographics_supplied) {
    demographics <- readr::read_csv(
      demographics_path,
      show_col_types = FALSE,
      progress = FALSE
    )

    if (!"barangay_name" %in% names(demographics)) {
      stop("Demographics file must contain barangay_name: ", demographics_path)
    }

    population_col <- detect_demo_column(names(demographics), "population")
    children_col <- detect_demo_column(names(demographics), "children")

    if (is.na(population_col)) {
      stop(
        "Demographics file must contain population or population_YYYY: ",
        demographics_path
      )
    }

    demographics$name_key <- normalize_name(demographics$barangay_name)
    demographics$population_value <- as.numeric(demographics[[population_col]])
    demographics$children_value <- if (!is.na(children_col)) {
      as.numeric(demographics[[children_col]])
    } else {
      NA_real_
    }

    observed_frac <- barangay_fraction(observed_geom) |>
      select(name_key, observed_fraction = fraction)
    model_frac <- barangay_fraction(model_geom) |>
      select(name_key, model_fraction = fraction)

    comparison <- demographics |>
      select(name_key, population_value, children_value) |>
      left_join(observed_frac, by = "name_key") |>
      left_join(model_frac, by = "name_key") |>
      mutate(
        observed_fraction = coalesce(observed_fraction, 0),
        model_fraction = coalesce(model_fraction, 0),
        reference_population = round(population_value * observed_fraction),
        modeled_population = round(population_value * model_fraction),
        reference_children = round(children_value * observed_fraction),
        modeled_children = round(children_value * model_fraction),
        population_error = modeled_population - reference_population,
        children_error = modeled_children - reference_children
      )

    pop_denominator <- sum(comparison$reference_population, na.rm = TRUE)
    if (is.finite(pop_denominator) && pop_denominator > 0) {
      population_metrics_available <- TRUE
      pop_wape <- safe_div(
        sum(abs(comparison$population_error), na.rm = TRUE),
        pop_denominator
      )
      if (sum(is.finite(comparison$modeled_population) & is.finite(comparison$reference_population)) >= 3) {
        pop_spearman <- suppressWarnings(
          cor(
            comparison$modeled_population,
            comparison$reference_population,
            method = "spearman",
            use = "complete.obs"
          )
        )
      }
    }

    child_denominator <- sum(comparison$reference_children, na.rm = TRUE)
    if (!is.na(children_col) && is.finite(child_denominator) && child_denominator > 0) {
      children_metrics_available <- TRUE
      child_wape <- safe_div(
        sum(abs(comparison$children_error), na.rm = TRUE),
        child_denominator
      )
      if (sum(is.finite(comparison$modeled_children) & is.finite(comparison$reference_children)) >= 3) {
        child_spearman <- suppressWarnings(
          cor(
            comparison$modeled_children,
            comparison$reference_children,
            method = "spearman",
            use = "complete.obs"
          )
        )
      }
    }
  }

  accuracy_rate_met <- isTRUE(f1 >= TARGET_ACCURACY)

  spatial_supporting_targets_met <-
    isTRUE(precision >= TARGET_PRECISION) &&
    isTRUE(recall >= TARGET_RECALL) &&
    isTRUE(iou >= TARGET_IOU)

  population_supporting_targets_met <- if (population_metrics_available) {
    isTRUE(pop_wape <= TARGET_WAPE) &&
      isTRUE(pop_spearman >= TARGET_SPEARMAN)
  } else {
    NA
  }

  supporting_targets_met <- spatial_supporting_targets_met &&
    (is.na(population_supporting_targets_met) || population_supporting_targets_met)

  # Keep the primary F1 acceptance distinct from all supporting targets.
  # All-six compliance is indeterminate when independent population metrics
  # are unavailable; holdout promotion still uses the separate primary F1 flag.
  all_targets_met <- if (!population_metrics_available) NA else {
    isTRUE(accuracy_rate_met) && isTRUE(supporting_targets_met)
  }

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
    demographics_supplied = demographics_supplied,
    population_metrics_available = population_metrics_available,
    children_metrics_available = children_metrics_available,
    spatial_supporting_targets_met = spatial_supporting_targets_met,
    population_supporting_targets_met = population_supporting_targets_met,
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

# Registry rows may be source-only until a published map is georeferenced and digitized.
# Keep those records visible, but do not pretend they are evaluable spatial geometry.
if (!"event_status" %in% names(events)) {
  events$event_status <- ifelse(
    is.na(events$observed_flood_path) | trimws(events$observed_flood_path) == "",
    "pending_observed_geometry",
    "ready"
  )
}
if (!"spatial_scope" %in% names(events)) events$spatial_scope <- NA_character_

resolved_observed_paths <- vapply(
  events$observed_flood_path,
  resolve_path,
  FUN.VALUE = character(1)
)
observed_geometry_exists <- !is.na(resolved_observed_paths) & file.exists(resolved_observed_paths)

registry_status <- events |>
  mutate(
    resolved_observed_flood_path = resolved_observed_paths,
    observed_geometry_exists = observed_geometry_exists,
    evaluation_status = ifelse(
      observed_geometry_exists,
      "ready_for_spatial_evaluation",
      "source_registered_geometry_pending"
    )
  )

registry_status_path <- file.path(calibration_dir, "event_registry_status.csv")
write.csv(registry_status, registry_status_path, row.names = FALSE)

# Do not evaluate newly attached, unapproved candidate geometry as ground truth.
# Existing ready events (e.g. Ulysses) remain unchanged.
valid_ready_status <- trimws(tolower(as.character(events$event_status))) == "ready"
valid_ready_status[is.na(valid_ready_status)] <- FALSE
source_only_mcdrrmo <- grepl("^mcdrrmo_", as.character(events$event_id))
# MCDRRMO references must also have passed 08's QC promotion (sidecar present).
mcdrrmo_qc_ok <- vapply(seq_len(nrow(events)), function(i) {
  if (!source_only_mcdrrmo[i]) return(TRUE)
  cert <- file.path(validation_dir, "events", events$event_id[i], "approved_reference_qc.json")
  if (!file.exists(cert)) return(FALSE)
  qc <- tryCatch(jsonlite::fromJSON(cert), error = function(e) NULL)
  if (is.null(qc)) return(FALSE)
  isTRUE(qc$accepted) && identical(as.character(qc$event_id), as.character(events$event_id[i]))
}, logical(1))
ready_for_evaluation <- observed_geometry_exists & valid_ready_status & mcdrrmo_qc_ok
registry_status$evaluation_status <- ifelse(
  ready_for_evaluation, "ready_for_spatial_evaluation", "source_registered_qc_or_geometry_pending"
)
write.csv(registry_status, registry_status_path, row.names = FALSE)
ready_events <- events[ready_for_evaluation, , drop = FALSE]
pending_events <- events[!ready_for_evaluation, , drop = FALSE]

if (nrow(pending_events) > 0) {
  cat(
    "Skipping ", nrow(pending_events),
    " source-only/pending event(s) with no observed geometry yet: ",
    paste(pending_events$event_id, collapse = ", "), "\n",
    sep = ""
  )
}

if (nrow(ready_events) == 0) {
  stop(
    "No event registry rows currently have an observed_flood geometry. ",
    "Georeference/digitize a registered source event or restore an existing observed reference."
  )
}

cat(
  "Evaluating ", nrow(ready_events),
  " ready historical event(s) across 9 scenario candidates...\n",
  sep = ""
)

all_metrics <- bind_rows(lapply(seq_len(nrow(ready_events)), function(i) {
  event <- as.list(ready_events[i, ])
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
  if (is.na(value) || !is.finite(value)) return(NA_real_)
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
      population_event_count = n_distinct(event_id[population_metrics_available %in% TRUE]),
      target_pass_rate = mean(accuracy_rate_met, na.rm = TRUE),
      mean_precision = mean_na(precision),
      mean_recall = mean_na(recall),
      mean_f1 = mean_na(f1),
      mean_iou = mean_na(iou_csi),
      mean_population_wape = mean_na(population_wape),
      mean_population_spearman = mean_na(population_spearman),
      .groups = "drop"
    ) |>
    rowwise() |>
    mutate(
      trigger_state = state,
      composite_score = weighted_candidate_score(
        mean_precision,
        mean_recall,
        mean_f1,
        mean_iou,
        mean_population_wape,
        mean_population_spearman
      )
    ) |>
    ungroup()
}))

if (nrow(candidate_scores) == 0) {
  candidate_scores <- data.frame(
    return_period = numeric(),
    minimum_hazard = character(),
    event_count = integer(),
    population_event_count = integer(),
    target_pass_rate = numeric(),
    mean_precision = numeric(),
    mean_recall = numeric(),
    mean_f1 = numeric(),
    mean_iou = numeric(),
    mean_population_wape = numeric(),
    mean_population_spearman = numeric(),
    trigger_state = character(),
    composite_score = numeric(),
    stringsAsFactors = FALSE
  )
}

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

selected_table <- if (length(selected_rows) > 0) {
  bind_rows(selected_rows)
} else {
  candidate_scores[0, , drop = FALSE]
}
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
reference_only_event_count <- length(unique(all_metrics$event_id[all_metrics$role == "reference_only"]))
registered_event_count <- nrow(events)
ready_event_count <- nrow(ready_events)
pending_event_count <- nrow(pending_events)

rule_config <- list(
  version = paste0("candidate-", format(Sys.time(), "%Y%m%d-%H%M%S")),
  approved = FALSE,
  source = "controlled_multi_event_calibration",
  calibration_event_count = calibration_event_count,
  holdout_event_count = holdout_event_count,
  reference_only_event_count = reference_only_event_count,
  registered_event_count = registered_event_count,
  ready_event_count = ready_event_count,
  pending_event_count = pending_event_count,
  generated_at = format(Sys.time(), "%Y-%m-%dT%H:%M:%S%z"),
  validation_acceptance = list(
    metric = ACCEPTANCE_METRIC,
    minimum = TARGET_ACCURACY,
    description = "Acceptable flood-extent accuracy is F1 >= 0.85 (85%). Supporting diagnostics are reported separately."
  ),
  notes = paste(
    "UNAPPROVED candidate configuration.",
    "Calibration used only ready events with role=calibration.",
    "Validation/test events were held out from candidate selection.",
    "Reference-only and source-only records never select automatic trigger mappings.",
    "Spatial-only historical events may omit demographics; missing population metrics are not treated as zero skill.",
    "Use 05_promote_calibrated_rules.R for explicit gated promotion."
  ),
  selection_notes = selection_notes,
  rules = selected_rules
)

candidate_json <- file.path(calibration_dir, "selected_rules_unapproved.json")
jsonlite::write_json(rule_config, candidate_json, auto_unbox = TRUE, pretty = TRUE, null = "null")

holdout_pass_rate <- if (nrow(holdout) > 0) mean(holdout$accuracy_rate_met, na.rm = TRUE) else NA_real_

report <- c(
  "RESCUAR / PREP — CONTROLLED MULTI-EVENT CALIBRATION REPORT",
  "",
  paste0("Event registry records: ", registered_event_count),
  paste0("Ready events evaluated: ", ready_event_count),
  paste0("Source-only/pending geometry events: ", pending_event_count),
  paste0("Calibration events evaluated: ", calibration_event_count),
  paste0("Held-out validation/test events evaluated: ", holdout_event_count),
  paste0("Reference-only events evaluated: ", reference_only_event_count),
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
  "Historical source records without observed geometry remain registered but are skipped until georeferencing/digitization is complete.",
  "Spatial-only events can be scored without event-year demographics; population metrics remain NA rather than borrowing modern demographics.",
  "This process searches only legitimate LiPAD scenario combinations. It does not generate synthetic ground truth, alter independent observations, or recursively change data to force a target score."
)

writeLines(report, file.path(calibration_dir, "multi_event_calibration_report.txt"))

cat(
  "\nCalibration analysis complete.\n",
  "Event registry status: ", registry_status_path, "\n",
  "All event/candidate metrics: ", metrics_path, "\n",
  "Candidate summary: ", candidate_scores_path, "\n",
  "Unapproved selected rules: ", candidate_json, "\n",
  "Holdout scores: ", holdout_path, "\n",
  sep = ""
)
