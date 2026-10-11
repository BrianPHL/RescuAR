HAZARD_CLASS_ORDER <- c("Low", "Medium", "High")
HAZARD_DEPTH_LABELS <- c(
  Low = "0.1-0.5 m",
  Medium = "0.5-1.5 m",
  High = ">1.5 m"
)

hazard_scenario_definitions <- function() {
  data.frame(
    return_period_years = c(5, 25, 100),
    annual_exceedance_probability_pct = c(20, 4, 1),
    scenario_label = c("5-Year Flood", "25-Year Flood", "100-Year Flood"),
    interpretation = c(
      "More frequent reference flood scenario; approximately 20% annual exceedance probability.",
      "Less frequent reference flood scenario; approximately 4% annual exceedance probability.",
      "Rare/extreme reference flood scenario; approximately 1% annual exceedance probability."
    ),
    stringsAsFactors = FALSE
  )
}

hazard_severity_definitions <- function() {
  data.frame(
    hazard_class = HAZARD_CLASS_ORDER,
    severity_rank = c(1L, 2L, 3L),
    depth_threshold = unname(HAZARD_DEPTH_LABELS[HAZARD_CLASS_ORDER]),
    minimum_depth_m = c(0.1, 0.5, 1.5),
    interpretation = c(
      "Low mapped flood hazard: water depth from 0.1 m to 0.5 m.",
      "Medium mapped flood hazard: water depth above 0.5 m to 1.5 m.",
      "High mapped flood hazard: water depth greater than 1.5 m."
    ),
    stringsAsFactors = FALSE
  )
}

normalize_minimum_hazard <- function(minimum_hazard = "Low") {
  x <- tools::toTitleCase(tolower(as.character(minimum_hazard)[1]))
  if (identical(x, "Moderate")) x <- "Medium"
  if (!x %in% HAZARD_CLASS_ORDER) {
    stop("minimum_hazard must be one of: Low, Medium, High")
  }
  x
}

selected_hazard_classes <- function(minimum_hazard = "Low") {
  minimum_hazard <- normalize_minimum_hazard(minimum_hazard)
  HAZARD_CLASS_ORDER[match(minimum_hazard, HAZARD_CLASS_ORDER):length(HAZARD_CLASS_ORDER)]
}

get_selected_hazard_layer <- function(return_period, minimum_hazard = "Low", data = BASELINE_DATA) {
  classes <- selected_hazard_classes(minimum_hazard)
  get_hazard_layer(return_period, data) %>%
    dplyr::filter(.data$hazard_class %in% classes)
}

estimate_barangay_affected_population <- function(return_period, minimum_hazard = "Low", data = BASELINE_DATA) {
  minimum_hazard <- normalize_minimum_hazard(minimum_hazard)
  classes <- selected_hazard_classes(minimum_hazard)
  x <- get_exposure_summary(return_period, data)

  class_to_col <- c(Low = "low_pct", Medium = "medium_pct", High = "high_pct")
  selected_cols <- unname(class_to_col[classes])

  selected_pct <- if (identical(minimum_hazard, "Low")) {
    # any_hazard_pct is the precomputed union of all mapped hazard classes.
    as.numeric(x$any_hazard_pct)
  } else {
    # Hazard classes are mutually exclusive in the supplied reference layers.
    pmin(rowSums(x[, selected_cols, drop = FALSE], na.rm = TRUE), 100)
  }

  pop <- as.numeric(x$population_2024)
  children <- as.numeric(x$children_2020)

  x %>%
    dplyr::mutate(
      population_low_exposed = round(pop * low_pct / 100),
      population_medium_exposed = round(pop * medium_pct / 100),
      population_high_exposed = round(pop * high_pct / 100),
      children_low_exposed = round(children * low_pct / 100),
      children_medium_exposed = round(children * medium_pct / 100),
      children_high_exposed = round(children * high_pct / 100),
      selected_hazard_area_pct = selected_pct,
      estimated_affected_population = round(pop * selected_pct / 100),
      estimated_affected_children = round(children * selected_pct / 100),
      estimated_unaffected_population = pmax(pop - round(pop * selected_pct / 100), 0),
      minimum_hazard_counted = minimum_hazard,
      population_estimation_method = "Area-weighted estimate assuming population is uniformly distributed within each barangay",
      affected_population_definition = "People estimated to reside within the selected mapped flood-hazard area; not an observed victim or casualty count"
    )
}

estimate_city_affected_population <- function(return_period, minimum_hazard = "Low", data = BASELINE_DATA) {
  x <- estimate_barangay_affected_population(return_period, minimum_hazard, data)
  defs <- hazard_scenario_definitions()
  scenario <- defs[defs$return_period_years == as.numeric(return_period), , drop = FALSE]

  city_pop <- sum(x$population_2024, na.rm = TRUE)
  city_children <- sum(x$children_2020, na.rm = TRUE)
  affected_pop <- sum(x$estimated_affected_population, na.rm = TRUE)
  affected_children <- sum(x$estimated_affected_children, na.rm = TRUE)

  data.frame(
    return_period_years = as.numeric(return_period),
    scenario_label = scenario$scenario_label,
    annual_exceedance_probability_pct = scenario$annual_exceedance_probability_pct,
    minimum_hazard_counted = normalize_minimum_hazard(minimum_hazard),
    minimum_depth_threshold = unname(HAZARD_DEPTH_LABELS[normalize_minimum_hazard(minimum_hazard)]),
    estimated_affected_population = affected_pop,
    estimated_affected_population_pct = if (city_pop > 0) round(affected_pop / city_pop * 100, 1) else NA_real_,
    estimated_affected_children = affected_children,
    estimated_affected_children_pct = if (city_children > 0) round(affected_children / city_children * 100, 1) else NA_real_,
    barangays_with_mapped_exposure = sum(x$selected_hazard_area_pct > 0, na.rm = TRUE),
    city_population_baseline = city_pop,
    city_children_baseline = city_children,
    stringsAsFactors = FALSE
  )
}

get_facility_exposure <- function(return_period, minimum_hazard = "Low", data = BASELINE_DATA) {
  hz <- get_selected_hazard_layer(return_period, minimum_hazard, data)
  facilities <- sf::st_make_valid(data$facilities)
  barangays <- sf::st_make_valid(data$barangays)
  hz <- sf::st_make_valid(hz)

  if (sf::st_crs(facilities) != sf::st_crs(hz)) facilities <- sf::st_transform(facilities, sf::st_crs(hz))
  if (sf::st_crs(barangays) != sf::st_crs(hz)) barangays <- sf::st_transform(barangays, sf::st_crs(hz))

  joined <- sf::st_join(
    facilities,
    hz[, c("hazard_class", "depth_class")],
    join = sf::st_intersects,
    left = FALSE
  )

  if (nrow(joined) == 0) return(joined)

  joined <- sf::st_join(
    joined,
    barangays[, c("barangay_id", "barangay_name")],
    join = sf::st_within,
    left = TRUE
  )

  rank <- match(joined$hazard_class, HAZARD_CLASS_ORDER)
  joined$hazard_rank <- rank
  joined <- joined[order(joined$osm_id, -joined$hazard_rank), ]
  joined <- joined[!duplicated(joined$osm_id), ]
  joined$hazard_rank <- NULL
  joined$return_period_years <- as.numeric(return_period)
  joined$minimum_hazard_counted <- normalize_minimum_hazard(minimum_hazard)
  joined
}

summarize_facility_exposure <- function(return_period, minimum_hazard = "Low", data = BASELINE_DATA) {
  x <- get_facility_exposure(return_period, minimum_hazard, data)
  if (nrow(x) == 0) {
    return(data.frame(
      facility_type = character(0),
      exposed_facilities = integer(0),
      stringsAsFactors = FALSE
    ))
  }
  sf::st_drop_geometry(x) %>%
    dplyr::count(.data$facility_type, name = "exposed_facilities") %>%
    dplyr::arrange(dplyr::desc(.data$exposed_facilities))
}

hazard_affected_population_metadata <- function() {
  list(
    model_name = "PREP Hazard Exposure and Affected Population Model",
    version = BASELINE_MODEL_VERSION,
    model_type = "deterministic GIS area-weighted exposure estimation",
    predictive = FALSE,
    primary_measure = "estimated affected population",
    affected_definition = "Population estimated to reside inside the selected mapped flood-hazard zone.",
    hazard = "Flood inundation hazard represented by UP DREAM/LiPAD 5-, 25-, and 100-year reference flood-hazard polygons.",
    hazard_classes = hazard_severity_definitions(),
    scenarios = hazard_scenario_definitions(),
    population_method = "Estimated affected population = barangay population x selected mapped hazard-area fraction",
    children_method = "Estimated affected children = barangay child population x selected mapped hazard-area fraction",
    facility_method = "A mapped facility is exposed when its point intersects the selected flood-hazard polygon.",
    key_assumption = "Population and children are uniformly distributed inside each barangay because gridded population data are not installed.",
    important_limitations = c(
      "Affected population is an exposure estimate, not an observed count of victims, evacuees, injuries, or casualties.",
      "Return-period maps are reference hazard scenarios and are not live flood forecasts.",
      "Areas outside the mapped polygons mean no mapped hazard for that selected scenario; they are not guaranteed safe.",
      "The model does not infer a return-period scenario from the current PAGASA river level.",
      "Population baseline is 2024 while child-population baseline is 2020.",
      "Uniform population distribution may overestimate or underestimate exposure within individual barangays."
    )
  )
}

# Backward-compatible function names used by the earlier hazard-exposure build.
estimate_barangay_exposure <- estimate_barangay_affected_population
estimate_city_exposure <- estimate_city_affected_population
hazard_exposure_model_metadata <- hazard_affected_population_metadata
