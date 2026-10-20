get_hazard_layer <- function(return_period, data = BASELINE_DATA) {
  key <- as.character(return_period)
  if (!key %in% names(data$hazards)) {
    stop("return_period must be one of: 5, 25, 100")
  }
  data$hazards[[key]]
}

get_exposure_summary <- function(return_period, data = BASELINE_DATA) {
  rp <- as.numeric(return_period)
  data$exposure %>%
    filter(return_period_years == rp) %>%
    arrange(desc(any_hazard_pct))
}

city_exposure_totals <- function(return_period, data = BASELINE_DATA) {
  x <- get_exposure_summary(return_period, data)
  data.frame(
    return_period_years = as.numeric(return_period),
    estimated_population_exposed = sum(x$estimated_population_exposed_area_weighted, na.rm = TRUE),
    estimated_children_exposed = sum(x$estimated_children_exposed_area_weighted, na.rm = TRUE),
    barangays_intersected = sum(x$any_hazard_pct > 0, na.rm = TRUE)
  )
}
