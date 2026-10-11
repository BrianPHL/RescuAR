suppressPackageStartupMessages({
  library(dplyr)
  library(readr)
  library(sf)
  library(jsonlite)
})

source(file.path("R", "config.R"), local = TRUE)

safe_read_csv <- function(path) {
  if (!file.exists(path)) stop("Missing required file: ", path)
  readr::read_csv(path, show_col_types = FALSE, progress = FALSE)
}

safe_read_layer <- function(gpkg, layer) {
  if (!file.exists(gpkg)) stop("Missing spatial database: ", gpkg)
  sf::st_read(gpkg, layer = layer, quiet = TRUE)
}

load_baseline_data <- function() {
  gpkg <- prep_path("data", "spatial", "baseline_spatial.gpkg")

  weather <- safe_read_csv(prep_path("data", "processed", "weather_daily.csv"))
  demographics <- safe_read_csv(prep_path("data", "processed", "demographics_barangay.csv"))
  features <- safe_read_csv(prep_path("data", "processed", "barangay_features_baseline.csv"))
  exposure <- safe_read_csv(prep_path("data", "processed", "barangay_hazard_exposure.csv"))
  barangays <- safe_read_layer(gpkg, "barangays")

  # barangay_id is an identifier, not a measurement. Keep it as character
  # in datasets that actually contain the field so dplyr joins do not fail
  # when GeoPackage IDs are text while CSV IDs are inferred as numeric.
  # demographics_barangay.csv is keyed by barangay_name only and intentionally
  # has no barangay_id column.
  normalize_barangay_id <- function(x) {
    if ("barangay_id" %in% names(x)) {
      x <- x %>% mutate(barangay_id = as.character(.data$barangay_id))
    }
    x
  }

  demographics <- normalize_barangay_id(demographics)
  features <- normalize_barangay_id(features)
  exposure <- normalize_barangay_id(exposure)
  barangays <- normalize_barangay_id(barangays)

  list(
    weather = weather,
    demographics = demographics,
    features = features,
    exposure = exposure,
    barangays = barangays,
    facilities = safe_read_layer(gpkg, "facilities"),
    river = safe_read_layer(gpkg, "marikina_river"),
    hazards = list(
      `5` = safe_read_layer(gpkg, "hazard_5yr"),
      `25` = safe_read_layer(gpkg, "hazard_25yr"),
      `100` = safe_read_layer(gpkg, "hazard_100yr")
    ),
    manifest = jsonlite::fromJSON(
      prep_path("data", "data_manifest.json"),
      simplifyVector = FALSE
    )
  )
}

BASELINE_DATA <- load_baseline_data()
