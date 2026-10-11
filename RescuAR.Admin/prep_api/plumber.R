#* @apiTitle PREP Hazard Exposure & Affected Population API
#* @apiDescription Deterministic flood-hazard exposure service for estimating the population and facilities spatially exposed under 5-, 25-, and 100-year reference flood scenarios.

suppressPackageStartupMessages({
  library(plumber)
  library(jsonlite)
  library(dplyr)
  library(sf)
})

source(file.path("R", "config.R"))
source(file.path("R", "load_data.R"))
source(file.path("R", "hazard.R"))
source(file.path("R", "exposure_model.R"))

`%||%` <- function(x, y) {
  if (is.null(x) || length(x) == 0 || (length(x) == 1 && is.na(x))) y else x
}

# Cross-origin access for the separately deployed RescuAR Admin frontend.
# Configure PREP_ALLOWED_ORIGINS as a comma-separated list in production.
# Example: https://rescuar-admin-production.up.railway.app
#* @filter cors
function(req, res) {
  configured <- Sys.getenv("PREP_ALLOWED_ORIGINS", "*")
  allowed <- trimws(strsplit(configured, ",", fixed = TRUE)[[1]])
  origin <- req$HTTP_ORIGIN %||% "*"

  if ("*" %in% allowed) {
    res$setHeader("Access-Control-Allow-Origin", "*")
  } else if (origin %in% allowed) {
    res$setHeader("Access-Control-Allow-Origin", origin)
    res$setHeader("Vary", "Origin")
  }

  res$setHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
  res$setHeader("Access-Control-Allow-Headers", "Content-Type, Authorization")
  res$setHeader("Access-Control-Max-Age", "86400")

  if (identical(req$REQUEST_METHOD, "OPTIONS")) {
    res$status <- 204
    return("")
  }

  plumber::forward()
}

#* Health check
#* @get /health
function() {
  list(
    ok = TRUE,
    service = "PREP Hazard Exposure & Affected Population API",
    version = BASELINE_MODEL_VERSION,
    scope = "deterministic hazard exposure estimation",
    predictive = FALSE
  )
}

#* Hazard definitions, severity thresholds, assumptions, and limitations
#* @get /v1/exposure/definitions
function() {
  hazard_affected_population_metadata()
}

#* Estimated affected population by barangay
#* @param return_period 5, 25, or 100
#* @param minimum_hazard Low, Medium, or High
#* @get /v1/exposure/population
function(return_period = 25, minimum_hazard = "Low") {
  rp <- suppressWarnings(as.numeric(return_period))
  if (!rp %in% c(5, 25, 100)) return(list(error = "return_period must be 5, 25, or 100"))
  tryCatch(
    estimate_barangay_affected_population(rp, minimum_hazard),
    error = function(e) list(error = conditionMessage(e))
  )
}

#* Exposed facilities by flood-hazard scenario
#* @param return_period 5, 25, or 100
#* @param minimum_hazard Low, Medium, or High
#* @get /v1/exposure/facilities
function(return_period = 25, minimum_hazard = "Low") {
  rp <- suppressWarnings(as.numeric(return_period))
  if (!rp %in% c(5, 25, 100)) return(list(error = "return_period must be 5, 25, or 100"))
  tryCatch(
    sf::st_drop_geometry(get_facility_exposure(rp, minimum_hazard)),
    error = function(e) list(error = conditionMessage(e))
  )
}

#* Full affected-population scenario for RescuAR Admin
#* @post /v1/exposure/scenario
function(req, res) {
  body <- tryCatch(jsonlite::fromJSON(req$postBody), error = function(e) NULL)
  if (is.null(body)) {
    res$status <- 400
    return(list(error = "invalid_json"))
  }
  rp <- if (!is.null(body$return_period)) suppressWarnings(as.numeric(body$return_period)) else 25
  minimum_hazard <- if (!is.null(body$minimum_hazard)) body$minimum_hazard else "Low"
  if (!rp %in% c(5, 25, 100)) {
    res$status <- 400
    return(list(error = "return_period must be 5, 25, or 100"))
  }

  tryCatch({
    barangays <- estimate_barangay_affected_population(rp, minimum_hazard)
    facilities <- get_facility_exposure(rp, minimum_hazard) %>% sf::st_drop_geometry()
    list(
      ok = TRUE,
      model = hazard_affected_population_metadata(),
      summary = estimate_city_affected_population(rp, minimum_hazard),
      barangays = barangays,
      facilities = facilities
    )
  }, error = function(e) {
    res$status <- 400
    list(error = conditionMessage(e))
  })
}

# Backward-compatible aliases from the previous hazard-exposure build.
#* @param return_period 5, 25, or 100
#* @param minimum_hazard Low, Medium, or High
#* @get /v1/hazard/exposure
function(return_period = 25, minimum_hazard = "Low") {
  estimate_barangay_affected_population(as.numeric(return_period), minimum_hazard)
}

#* @post /v1/inundation/scenario
function(req, res) {
  body <- tryCatch(jsonlite::fromJSON(req$postBody), error = function(e) NULL)
  if (is.null(body)) {
    res$status <- 400
    return(list(error = "invalid_json"))
  }
  rp <- if (!is.null(body$return_period)) suppressWarnings(as.numeric(body$return_period)) else 25
  minimum_hazard <- if (!is.null(body$minimum_hazard)) body$minimum_hazard else "Low"
  list(
    deprecated_endpoint = TRUE,
    use_instead = "/v1/exposure/scenario",
    result = list(
      model = hazard_affected_population_metadata(),
      summary = estimate_city_affected_population(rp, minimum_hazard),
      barangays = estimate_barangay_affected_population(rp, minimum_hazard),
      facilities = sf::st_drop_geometry(get_facility_exposure(rp, minimum_hazard))
    )
  )
}
