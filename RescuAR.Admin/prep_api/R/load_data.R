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

optional_read_population_weights <- function(gpkg) {
  if (!file.exists(gpkg)) return(NULL)

  layer_names <- tryCatch(
    sf::st_layers(gpkg)$name,
    error = function(e) character(0)
  )

  preferred <- c("population_weights", "population_grid", "population_density")
  selected <- preferred[preferred %in% layer_names]
  if (length(selected) == 0) return(NULL)

  layer <- selected[1]
  weights <- sf::st_read(gpkg, layer = layer, quiet = TRUE)

  population_candidates <- c("population_weight", "population_2024", "population", "pop_weight", "pop")
  children_candidates <- c("children_weight", "children_2020", "children", "child_weight")

  pop_col <- population_candidates[population_candidates %in% names(weights)][1]
  child_col <- children_candidates[children_candidates %in% names(weights)][1]

  if (is.na(pop_col) || is.null(pop_col)) {
    warning(
      "Optional population weighting layer '", layer,
      "' was found but contains no supported population-weight field. Falling back to area weighting."
    )
    return(NULL)
  }

  attr(weights, "prep_population_weight_col") <- pop_col
  attr(weights, "prep_children_weight_col") <- if (!is.na(child_col) && !is.null(child_col)) child_col else NULL
  attr(weights, "prep_source_layer") <- layer
  weights
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

  population_weights <- optional_read_population_weights(gpkg)

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
    population_weights = population_weights,
    manifest = jsonlite::fromJSON(
      prep_path("data", "data_manifest.json"),
      simplifyVector = FALSE
    )
  )
}

BASELINE_DATA <- load_baseline_data()
