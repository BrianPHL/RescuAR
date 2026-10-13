# ============================================================
# RESCUAR / PREP
# EXPLICIT CALIBRATION PROMOTION GATE
#
# This script never runs automatically. Promotion requires:
#   1. APPROVE_PROMOTION <- TRUE
#   2. enough independent calibration and holdout events
#   3. holdout events meeting the project 85% F1 accuracy gate (default behavior)
#
# It writes only the scenario-rule configuration. It does not
# alter NAMRIA references, LiPAD hazard polygons, or demographics.
# ============================================================

suppressPackageStartupMessages({
  library(jsonlite)
  library(readr)
  library(dplyr)
})

# ------------------------------------------------------------
# MANUAL APPROVAL SETTINGS
# ------------------------------------------------------------

APPROVE_PROMOTION <- FALSE
REQUIRE_ALL_HOLDOUT_ROWS_TO_MEET_TARGETS <- TRUE  # all holdout rows must have F1 >= 0.85
MIN_CALIBRATION_EVENTS <- 2
MIN_HOLDOUT_EVENTS <- 1

# ------------------------------------------------------------
# PROJECT ROOT
# ------------------------------------------------------------

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
calibration_dir <- file.path(prep_root, "validation", "calibration")
config_dir <- file.path(prep_root, "config")

dir.create(config_dir, recursive = TRUE, showWarnings = FALSE)

candidate_path <- file.path(calibration_dir, "selected_rules_unapproved.json")
holdout_path <- file.path(calibration_dir, "holdout_selected_rule_scores.csv")
production_path <- file.path(config_dir, "exposure_scenario_rules.json")

if (!file.exists(candidate_path)) {
  stop("Missing calibration candidate. Run 04_calibrate_scenario_rules.R first.")
}

candidate <- jsonlite::fromJSON(candidate_path, simplifyVector = FALSE)

if (!isTRUE(APPROVE_PROMOTION)) {
  stop(
    paste(
      "Promotion is intentionally disabled.",
      "Review the calibration and holdout reports, then set",
      "APPROVE_PROMOTION <- TRUE only when the configuration is defensible."
    )
  )
}

`%||%` <- function(x, y) {
  if (is.null(x) || length(x) == 0 || is.na(x)) y else x
}

calibration_count <- as.integer(candidate$calibration_event_count %||% 0)
holdout_count <- as.integer(candidate$holdout_event_count %||% 0)

if (calibration_count < MIN_CALIBRATION_EVENTS) {
  stop(
    "Promotion blocked: only ", calibration_count,
    " calibration event(s); minimum required is ", MIN_CALIBRATION_EVENTS, "."
  )
}

if (holdout_count < MIN_HOLDOUT_EVENTS) {
  stop(
    "Promotion blocked: only ", holdout_count,
    " holdout event(s); minimum required is ", MIN_HOLDOUT_EVENTS, "."
  )
}

if (!file.exists(holdout_path)) {
  stop("Promotion blocked: holdout_selected_rule_scores.csv is missing.")
}

holdout <- readr::read_csv(holdout_path, show_col_types = FALSE, progress = FALSE)
if (nrow(holdout) == 0) stop("Promotion blocked: no held-out scores are available.")

if (REQUIRE_ALL_HOLDOUT_ROWS_TO_MEET_TARGETS && !all(holdout$all_targets_met %in% TRUE)) {
  failed <- holdout |> filter(!all_targets_met)
  print(failed)
  stop("Promotion blocked: one or more held-out events do not meet the project acceptable accuracy rate (F1 >= 0.85).")
}

candidate$approved <- TRUE
candidate$source <- "approved_multi_event_calibration"
candidate$approved_at <- format(Sys.time(), "%Y-%m-%dT%H:%M:%S%z")
candidate$notes <- paste(
  "Approved after explicit promotion gate.",
  "Production uses only the selected LiPAD scenario mapping;",
  "independent observations remain separate validation data."
)

backup_path <- NULL
if (file.exists(production_path)) {
  backup_path <- paste0(
    production_path,
    ".backup-",
    format(Sys.time(), "%Y%m%d-%H%M%S")
  )
  file.copy(production_path, backup_path, overwrite = TRUE)
}

jsonlite::write_json(
  candidate,
  production_path,
  auto_unbox = TRUE,
  pretty = TRUE,
  null = "null"
)

cat(
  "\nAPPROVED calibration configuration promoted.\n",
  "Production config: ", production_path, "\n",
  if (!is.null(backup_path)) paste0("Previous config backup: ", backup_path, "\n") else "",
  "Redeploy the PREP API and Admin frontend so the approved mapping is loaded.\n",
  sep = ""
)
