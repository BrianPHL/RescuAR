suppressPackageStartupMessages({
  library(dplyr)
  library(readr)
  library(sf)
})

# PREP validation utility
# Run from any directory with:
#   Rscript path/to/prep_api/validation/validate_exposure.R
#
# Optional environment variables:
#   VALIDATION_RETURN_PERIOD=25
#   VALIDATION_MINIMUM_HAZARD=Medium
#   VALIDATION_OBSERVED_BARANGAY_CSV=validation/input/observed_barangay_exposure.csv
#   VALIDATION_OBSERVED_FLOOD_PATH=validation/input/observed_flood.geojson
#   VALIDATION_OBSERVED_FLOOD_LAYER=<layer name for GPKG only>
#
# The script always runs internal model-consistency checks. Population and
# spatial validation run only when the corresponding independent reference
# files are supplied.

get_script_path <- function() {
  args <- commandArgs(trailingOnly = FALSE)
  file_arg <- grep("^--file=", args, value = TRUE)
  if (length(file_arg) == 0) return(NA_character_)
  sub("^--file=", "", file_arg[[1]])
}

script_path <- get_script_path()
if (is.na(script_path)) {
  stop("Run this file with Rscript so the PREP root can be located reliably.")
}

script_dir <- dirname(normalizePath(script_path, winslash = "/", mustWork = TRUE))
prep_root <- normalizePath(file.path(script_dir, ".."), winslash = "/", mustWork = TRUE)
old_wd <- getwd()
on.exit(setwd(old_wd), add = TRUE)
setwd(prep_root)

source(file.path("R", "config.R"))
source(file.path("R", "load_data.R"))
source(file.path("R", "hazard.R"))
source(file.path("R", "exposure_model.R"))

return_period <- suppressWarnings(as.numeric(Sys.getenv("VALIDATION_RETURN_PERIOD", "25")))
minimum_hazard <- Sys.getenv("VALIDATION_MINIMUM_HAZARD", "Medium")
observed_barangay_csv <- Sys.getenv(
  "VALIDATION_OBSERVED_BARANGAY_CSV",
  file.path("validation", "input", "observed_barangay_exposure.csv")
)
observed_flood_path <- Sys.getenv("VALIDATION_OBSERVED_FLOOD_PATH", "")
observed_flood_layer <- Sys.getenv("VALIDATION_OBSERVED_FLOOD_LAYER", "")
output_dir <- file.path("validation", "output")
dir.create(output_dir, recursive = TRUE, showWarnings = FALSE)

if (!return_period %in% c(5, 25, 100)) {
  stop("VALIDATION_RETURN_PERIOD must be 5, 25, or 100.")
}
minimum_hazard <- normalize_minimum_hazard(minimum_hazard)

# Project-defined validation targets. These are NOT universal regulatory
# standards and should be pre-declared in the manuscript/protocol before
# examining final results.
TARGETS <- list(
  internal_consistency_pct = 100,
  recall = 0.90,
  precision = 0.80,
  f1 = 0.85,
  iou_csi = 0.70,
  population_wape_pct = 20,
  rank_spearman = 0.80,
  top_n = 5
)

safe_divide <- function(numerator, denominator) {
  if (is.na(denominator) || denominator == 0) return(NA_real_)
  numerator / denominator
}

normalize_name <- function(x) {
  x <- iconv(as.character(x), from = "UTF-8", to = "ASCII//TRANSLIT")
  x <- tolower(x)
  x <- gsub("[^a-z0-9]+", " ", x)
  trimws(gsub("\\s+", " ", x))
}

metric_status_min <- function(value, target) {
  if (is.na(value)) return("NOT TESTED")
  if (value >= target) "PASS" else "REVIEW"
}

metric_status_max <- function(value, target) {
  if (is.na(value)) return("NOT TESTED")
  if (value <= target) "PASS" else "REVIEW"
}

write_section <- function(con, title, df) {
  writeLines(c("", paste0("## ", title), ""), con)
  capture.output(print(df, n = Inf, width = Inf), file = con, append = TRUE)
}

cat("PREP validation started\n")
cat("Scenario:", return_period, "year /", minimum_hazard, "+\n")

# ---------------------------------------------------------------------------
# A. Internal consistency / implementation fidelity
# ---------------------------------------------------------------------------
barangay_est <- estimate_barangay_affected_population(return_period, minimum_hazard)
city_est <- estimate_city_affected_population(return_period, minimum_hazard)
facility_est <- get_facility_exposure(return_period, minimum_hazard)

summary_population <- as.numeric(city_est$estimated_affected_population[[1]])
summary_children <- as.numeric(city_est$estimated_affected_children[[1]])
summary_barangays <- as.numeric(city_est$barangays_with_mapped_exposure[[1]])

sum_population <- sum(as.numeric(barangay_est$estimated_affected_population), na.rm = TRUE)
sum_children <- sum(as.numeric(barangay_est$estimated_affected_children), na.rm = TRUE)
count_barangays <- sum(as.numeric(barangay_est$selected_hazard_area_pct) > 0, na.rm = TRUE)
expected_barangay_rows <- nrow(BASELINE_DATA$barangays)
unique_facilities <- dplyr::n_distinct(facility_est$osm_id)

internal_checks <- tibble::tribble(
  ~check, ~expected, ~observed, ~tolerance, ~pass,
  "City exposed population equals sum of barangay estimates", summary_population, sum_population, 1, abs(summary_population - sum_population) <= 1,
  "City exposed children equals sum of barangay estimates", summary_children, sum_children, 1, abs(summary_children - sum_children) <= 1,
  "Mapped barangay count equals computed exposed barangay count", summary_barangays, count_barangays, 0, summary_barangays == count_barangays,
  "All baseline barangays are represented", expected_barangay_rows, nrow(barangay_est), 0, expected_barangay_rows == nrow(barangay_est),
  "Facility exposure rows are unique by OSM ID", nrow(facility_est), unique_facilities, 0, nrow(facility_est) == unique_facilities,
  "Hazard-area percentages stay within 0-100", 1, as.integer(all(barangay_est$selected_hazard_area_pct >= 0 & barangay_est$selected_hazard_area_pct <= 100, na.rm = TRUE)), 0, all(barangay_est$selected_hazard_area_pct >= 0 & barangay_est$selected_hazard_area_pct <= 100, na.rm = TRUE),
  "Population estimates are non-negative", 1, as.integer(all(barangay_est$estimated_affected_population >= 0, na.rm = TRUE)), 0, all(barangay_est$estimated_affected_population >= 0, na.rm = TRUE)
)

internal_consistency_pct <- mean(internal_checks$pass) * 100
internal_summary <- tibble::tibble(
  metric = "Internal consistency pass rate",
  value = internal_consistency_pct,
  target = TARGETS$internal_consistency_pct,
  status = ifelse(internal_consistency_pct == TARGETS$internal_consistency_pct, "PASS", "REVIEW")
)

readr::write_csv(internal_checks, file.path(output_dir, "internal_consistency_checks.csv"))
readr::write_csv(internal_summary, file.path(output_dir, "internal_consistency_summary.csv"))

# ---------------------------------------------------------------------------
# B. Population exposure validation against an independent barangay reference
# ---------------------------------------------------------------------------
population_metrics <- tibble::tibble()
population_comparison <- tibble::tibble()

if (file.exists(observed_barangay_csv)) {
  observed <- readr::read_csv(observed_barangay_csv, show_col_types = FALSE)
  required <- c("barangay_name", "observed_exposed_population")
  missing_cols <- setdiff(required, names(observed))

  if (length(missing_cols) > 0) {
    warning("Population validation skipped. Missing column(s): ", paste(missing_cols, collapse = ", "))
  } else {
    observed <- observed %>%
      mutate(join_name = normalize_name(.data$barangay_name))

    predicted <- barangay_est
    if (inherits(predicted, "sf")) predicted <- sf::st_drop_geometry(predicted)
    predicted <- predicted %>%
      mutate(join_name = normalize_name(.data$barangay_name)) %>%
      select(
        join_name,
        barangay_name,
        estimated_affected_population,
        estimated_affected_children,
        selected_hazard_area_pct
      )

    population_comparison <- predicted %>%
      left_join(observed, by = "join_name", suffix = c("_model", "_observed")) %>%
      mutate(
        observed_exposed_population = suppressWarnings(as.numeric(.data$observed_exposed_population)),
        population_error = .data$estimated_affected_population - .data$observed_exposed_population,
        population_abs_error = abs(.data$population_error)
      )

    valid_pop <- population_comparison %>%
      filter(is.finite(.data$observed_exposed_population))

    if (nrow(valid_pop) > 0) {
      mae <- mean(valid_pop$population_abs_error)
      rmse <- sqrt(mean(valid_pop$population_error^2))
      wape <- safe_divide(sum(valid_pop$population_abs_error), sum(valid_pop$observed_exposed_population)) * 100
      spearman <- if (nrow(valid_pop) >= 3) {
        suppressWarnings(cor(valid_pop$estimated_affected_population, valid_pop$observed_exposed_population, method = "spearman", use = "complete.obs"))
      } else NA_real_

      n_top <- min(TARGETS$top_n, nrow(valid_pop))
      model_top <- valid_pop %>% arrange(desc(.data$estimated_affected_population)) %>% slice_head(n = n_top) %>% pull(.data$join_name)
      observed_top <- valid_pop %>% arrange(desc(.data$observed_exposed_population)) %>% slice_head(n = n_top) %>% pull(.data$join_name)
      top_n_overlap_pct <- safe_divide(length(intersect(model_top, observed_top)), n_top) * 100

      population_metrics <- tibble::tibble(
        metric = c("MAE persons/barangay", "RMSE persons/barangay", "WAPE %", "Spearman rank correlation", paste0("Top-", n_top, " overlap %")),
        value = c(mae, rmse, wape, spearman, top_n_overlap_pct),
        target = c(NA, NA, TARGETS$population_wape_pct, TARGETS$rank_spearman, NA),
        status = c(
          "REPORT",
          "REPORT",
          metric_status_max(wape, TARGETS$population_wape_pct),
          metric_status_min(spearman, TARGETS$rank_spearman),
          "REPORT"
        )
      )

      # Optional child metric if the independent reference includes it.
      if ("observed_exposed_children" %in% names(valid_pop)) {
        valid_child <- valid_pop %>%
          mutate(observed_exposed_children = suppressWarnings(as.numeric(.data$observed_exposed_children))) %>%
          filter(is.finite(.data$observed_exposed_children)) %>%
          mutate(
            child_error = .data$estimated_affected_children - .data$observed_exposed_children,
            child_abs_error = abs(.data$child_error)
          )

        if (nrow(valid_child) > 0) {
          child_mae <- mean(valid_child$child_abs_error)
          child_rmse <- sqrt(mean(valid_child$child_error^2))
          child_wape <- safe_divide(sum(valid_child$child_abs_error), sum(valid_child$observed_exposed_children)) * 100
          population_metrics <- bind_rows(
            population_metrics,
            tibble::tibble(
              metric = c("Child MAE persons/barangay", "Child RMSE persons/barangay", "Child WAPE %"),
              value = c(child_mae, child_rmse, child_wape),
              target = c(NA, NA, TARGETS$population_wape_pct),
              status = c("REPORT", "REPORT", metric_status_max(child_wape, TARGETS$population_wape_pct))
            )
          )
        }
      }

      readr::write_csv(population_comparison, file.path(output_dir, "barangay_population_comparison.csv"))
      readr::write_csv(population_metrics, file.path(output_dir, "population_validation_metrics.csv"))
    }
  }
} else {
  message("No observed barangay CSV found; population validation skipped: ", observed_barangay_csv)
}

# ---------------------------------------------------------------------------
# C. Spatial inundation/hazard-footprint validation
# ---------------------------------------------------------------------------
spatial_metrics <- tibble::tibble()

if (nzchar(observed_flood_path) && file.exists(observed_flood_path)) {
  observed_flood <- if (nzchar(observed_flood_layer)) {
    sf::st_read(observed_flood_path, layer = observed_flood_layer, quiet = TRUE)
  } else {
    sf::st_read(observed_flood_path, quiet = TRUE)
  }

  if (is.na(sf::st_crs(observed_flood))) {
    stop("Observed flood extent has no CRS. Assign the correct CRS before validation.")
  }

  model_flood <- get_selected_hazard_layer(return_period, minimum_hazard)
  city_boundary <- sf::st_union(sf::st_make_valid(BASELINE_DATA$barangays))
  target_crs <- sf::st_crs(BASELINE_DATA$barangays)

  model_flood <- sf::st_transform(sf::st_make_valid(model_flood), target_crs)
  observed_flood <- sf::st_transform(sf::st_make_valid(observed_flood), target_crs)
  city_boundary <- sf::st_transform(sf::st_make_valid(city_boundary), target_crs)

  model_union <- sf::st_union(model_flood)
  observed_union <- sf::st_union(observed_flood)

  # Clip both datasets to Marikina's administrative footprint to make the
  # comparison spatially equivalent.
  model_union <- suppressWarnings(sf::st_intersection(model_union, city_boundary))
  observed_union <- suppressWarnings(sf::st_intersection(observed_union, city_boundary))

  tp_geom <- suppressWarnings(sf::st_intersection(model_union, observed_union))
  fp_geom <- suppressWarnings(sf::st_difference(model_union, observed_union))
  fn_geom <- suppressWarnings(sf::st_difference(observed_union, model_union))

  geom_area <- function(x) {
    if (length(x) == 0 || all(sf::st_is_empty(x))) return(0)
    as.numeric(sum(sf::st_area(x), na.rm = TRUE))
  }

  tp <- geom_area(tp_geom)
  fp <- geom_area(fp_geom)
  fn <- geom_area(fn_geom)

  precision <- safe_divide(tp, tp + fp)
  recall <- safe_divide(tp, tp + fn)
  f1 <- safe_divide(2 * precision * recall, precision + recall)
  iou <- safe_divide(tp, tp + fp + fn)

  spatial_metrics <- tibble::tibble(
    metric = c("Precision", "Recall", "F1", "IoU / CSI"),
    value = c(precision, recall, f1, iou),
    target = c(TARGETS$precision, TARGETS$recall, TARGETS$f1, TARGETS$iou_csi),
    status = c(
      metric_status_min(precision, TARGETS$precision),
      metric_status_min(recall, TARGETS$recall),
      metric_status_min(f1, TARGETS$f1),
      metric_status_min(iou, TARGETS$iou_csi)
    )
  )

  readr::write_csv(spatial_metrics, file.path(output_dir, "spatial_validation_metrics.csv"))

  # Diagnostic GeoPackage lets reviewers visually inspect agreement/errors.
  gpkg <- file.path(output_dir, "spatial_validation_layers.gpkg")
  if (file.exists(gpkg)) file.remove(gpkg)
  sf::st_write(sf::st_as_sf(model_union), gpkg, layer = "model_hazard", quiet = TRUE)
  sf::st_write(sf::st_as_sf(observed_union), gpkg, layer = "observed_flood", append = TRUE, quiet = TRUE)
  if (tp > 0) sf::st_write(sf::st_as_sf(tp_geom), gpkg, layer = "true_positive", append = TRUE, quiet = TRUE)
  if (fp > 0) sf::st_write(sf::st_as_sf(fp_geom), gpkg, layer = "false_positive", append = TRUE, quiet = TRUE)
  if (fn > 0) sf::st_write(sf::st_as_sf(fn_geom), gpkg, layer = "false_negative", append = TRUE, quiet = TRUE)
} else {
  message("No observed flood extent supplied; spatial validation skipped.")
}

# ---------------------------------------------------------------------------
# D. Human-readable validation report
# ---------------------------------------------------------------------------
report_file <- file.path(output_dir, "validation_report.txt")
con <- file(report_file, open = "wt", encoding = "UTF-8")

writeLines(c(
  "PREP Flood Hazard Exposure Validation Report",
  paste0("Scenario: ", return_period, "-Year / ", minimum_hazard, "+"),
  paste0("Generated: ", format(Sys.time(), "%Y-%m-%d %H:%M:%S")),
  "",
  "IMPORTANT: Project targets below are pre-declared engineering/research acceptance targets, not universal PAGASA, NDRRMC, legal, or regulatory accuracy requirements."
), con)

write_section(con, "Internal consistency", internal_checks)
write_section(con, "Internal consistency summary", internal_summary)

if (nrow(population_metrics) > 0) {
  write_section(con, "Population exposure validation", population_metrics)
} else {
  writeLines(c("", "## Population exposure validation", "", "NOT TESTED - supply an independent barangay-level observed/reference dataset."), con)
}

if (nrow(spatial_metrics) > 0) {
  write_section(con, "Spatial flood-extent validation", spatial_metrics)
} else {
  writeLines(c("", "## Spatial flood-extent validation", "", "NOT TESTED - supply an independent georeferenced observed flood extent."), con)
}

writeLines(c(
  "",
  "## Interpretation rules",
  "",
  "- 100% internal consistency is expected because frontend/API/model arithmetic should reconcile exactly aside from explicit rounding tolerances.",
  "- Spatial metrics test overlap between the selected reference hazard footprint and an independent observed flood extent.",
  "- Population metrics are valid only when the reference dataset measures the same concept: residents spatially exposed to inundation/hazard, not merely evacuees, casualties, or administratively reported affected persons.",
  "- A failed target means REVIEW, not automatic invalidation. Investigate source quality, event comparability, CRS/alignment, scenario selection, and the uniform-population assumption.",
  "- Validation should be repeated across multiple independent flood events when data are available."
), con)

close(con)

cat("Validation complete. Outputs written to:", normalizePath(output_dir, winslash = "/", mustWork = TRUE), "\n")
cat("Internal consistency:", sprintf("%.1f%%", internal_consistency_pct), "\n")
if (nrow(population_metrics) > 0) print(population_metrics)
if (nrow(spatial_metrics) > 0) print(spatial_metrics)
