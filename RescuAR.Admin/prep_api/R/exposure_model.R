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

normalize_barangay_key <- function(x) {
  x <- iconv(as.character(x), to = "ASCII//TRANSLIT")
  x <- toupper(x)
  x <- gsub("[^A-Z0-9]+", " ", x)
  trimws(gsub("\\s+", " ", x))
}

population_weighting_available <- function(data = BASELINE_DATA) {
  !is.null(data$population_weights) &&
    inherits(data$population_weights, "sf") &&
    nrow(data$population_weights) > 0
}

prepare_population_weights <- function(data = BASELINE_DATA) {
  if (!population_weighting_available(data)) return(NULL)

  pop_col <- attr(data$population_weights, "prep_population_weight_col")
  child_col <- attr(data$population_weights, "prep_children_weight_col")

  weights <- sf::st_make_valid(data$population_weights)
  barangays <- sf::st_make_valid(data$barangays)

  if (is.null(pop_col) || !pop_col %in% names(weights)) return(NULL)

  if (!"barangay_name" %in% names(weights)) {
    if (sf::st_crs(weights) != sf::st_crs(barangays)) {
      barangays <- sf::st_transform(barangays, sf::st_crs(weights))
    }
    probe <- suppressWarnings(sf::st_point_on_surface(weights))
    joined <- sf::st_join(
      probe,
      barangays[, c("barangay_name")],
      join = sf::st_within,
      left = TRUE
    )
    weights$barangay_name <- joined$barangay_name
  }

  weights$population_weight <- suppressWarnings(as.numeric(weights[[pop_col]]))
  weights$population_weight[!is.finite(weights$population_weight) | weights$population_weight < 0] <- 0

  if (!is.null(child_col) && child_col %in% names(weights)) {
    weights$children_weight <- suppressWarnings(as.numeric(weights[[child_col]]))
    weights$children_weight[!is.finite(weights$children_weight) | weights$children_weight < 0] <- 0
  } else {
    # If no independent child-density field exists, use the population-density
    # distribution as the spatial weighting surface for children as well.
    weights$children_weight <- weights$population_weight
  }

  weights$barangay_key <- normalize_barangay_key(weights$barangay_name)
  weights$prep_weight_id <- seq_len(nrow(weights))
  weights
}

weighted_fraction_for_hazard <- function(hz, data = BASELINE_DATA) {
  weights <- prepare_population_weights(data)
  if (is.null(weights)) return(NULL)

  if (nrow(hz) == 0) {
    totals <- sf::st_drop_geometry(weights) %>%
      dplyr::group_by(.data$barangay_key) %>%
      dplyr::summarise(
        total_population_weight = sum(.data$population_weight, na.rm = TRUE),
        total_children_weight = sum(.data$children_weight, na.rm = TRUE),
        .groups = "drop"
      )
    totals$population_weight_fraction <- 0
    totals$children_weight_fraction <- 0
    return(totals)
  }

  target_crs <- 32651
  weights <- sf::st_transform(weights, target_crs)
  hz <- sf::st_transform(sf::st_make_valid(hz), target_crs)
  hz_union <- sf::st_union(sf::st_geometry(hz))

  geom_types <- unique(as.character(sf::st_geometry_type(weights, by_geometry = TRUE)))
  point_like <- all(geom_types %in% c("POINT", "MULTIPOINT"))

  if (point_like) {
    hit <- lengths(sf::st_intersects(weights, hz_union)) > 0
    exposed_population_weight <- weights$population_weight * as.numeric(hit)
    exposed_children_weight <- weights$children_weight * as.numeric(hit)
  } else {
    weights$prep_area_m2 <- as.numeric(sf::st_area(weights))

    intersections <- suppressWarnings(
      sf::st_intersection(
        weights[, c("prep_weight_id")],
        sf::st_sf(geometry = hz_union)
      )
    )

    if (nrow(intersections) == 0) {
      overlap <- data.frame(
        prep_weight_id = weights$prep_weight_id,
        overlap_fraction = 0
      )
    } else {
      intersections$overlap_m2 <- as.numeric(sf::st_area(intersections))
      overlap <- sf::st_drop_geometry(intersections) %>%
        dplyr::group_by(.data$prep_weight_id) %>%
        dplyr::summarise(overlap_m2 = sum(.data$overlap_m2, na.rm = TRUE), .groups = "drop") %>%
        dplyr::right_join(
          sf::st_drop_geometry(weights)[, c("prep_weight_id", "prep_area_m2")],
          by = "prep_weight_id"
        ) %>%
        dplyr::mutate(
          overlap_m2 = dplyr::coalesce(.data$overlap_m2, 0),
          overlap_fraction = pmin(1, pmax(0, .data$overlap_m2 / .data$prep_area_m2))
        ) %>%
        dplyr::select(.data$prep_weight_id, .data$overlap_fraction)
    }

    weights <- weights %>%
      dplyr::left_join(overlap, by = "prep_weight_id") %>%
      dplyr::mutate(overlap_fraction = dplyr::coalesce(.data$overlap_fraction, 0))

    exposed_population_weight <- weights$population_weight * weights$overlap_fraction
    exposed_children_weight <- weights$children_weight * weights$overlap_fraction
  }

  base <- sf::st_drop_geometry(weights)
  base$exposed_population_weight <- exposed_population_weight
  base$exposed_children_weight <- exposed_children_weight

  base %>%
    dplyr::group_by(.data$barangay_key) %>%
    dplyr::summarise(
      total_population_weight = sum(.data$population_weight, na.rm = TRUE),
      exposed_population_weight = sum(.data$exposed_population_weight, na.rm = TRUE),
      total_children_weight = sum(.data$children_weight, na.rm = TRUE),
      exposed_children_weight = sum(.data$exposed_children_weight, na.rm = TRUE),
      .groups = "drop"
    ) %>%
    dplyr::mutate(
      population_weight_fraction = ifelse(
        .data$total_population_weight > 0,
        pmin(1, pmax(0, .data$exposed_population_weight / .data$total_population_weight)),
        NA_real_
      ),
      children_weight_fraction = ifelse(
        .data$total_children_weight > 0,
        pmin(1, pmax(0, .data$exposed_children_weight / .data$total_children_weight)),
        NA_real_
      )
    )
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

  # Default: legacy area-weighted exposure fractions.
  low_pop_fraction <- as.numeric(x$low_pct) / 100
  med_pop_fraction <- as.numeric(x$medium_pct) / 100
  high_pop_fraction <- as.numeric(x$high_pct) / 100
  low_child_fraction <- low_pop_fraction
  med_child_fraction <- med_pop_fraction
  high_child_fraction <- high_pop_fraction
  selected_pop_fraction <- selected_pct / 100
  selected_child_fraction <- selected_pct / 100
  estimation_method <- "Area-weighted estimate assuming population is uniformly distributed within each barangay"

  # Optional improvement: if a population weighting/grid layer is installed,
  # calculate exposure from its within-barangay spatial distribution instead
  # of assuming uniform distribution.
  if (population_weighting_available(data)) {
    x$barangay_key <- normalize_barangay_key(x$barangay_name)

    class_fraction <- function(class_name) {
      hz <- get_hazard_layer(return_period, data) %>%
        dplyr::filter(.data$hazard_class == class_name)
      weighted_fraction_for_hazard(hz, data)
    }

    weighted_low <- class_fraction("Low")
    weighted_medium <- class_fraction("Medium")
    weighted_high <- class_fraction("High")
    weighted_selected <- weighted_fraction_for_hazard(
      get_selected_hazard_layer(return_period, minimum_hazard, data),
      data
    )

    apply_fraction <- function(tbl, column, fallback) {
      if (is.null(tbl) || nrow(tbl) == 0) return(fallback)
      idx <- match(x$barangay_key, tbl$barangay_key)
      candidate <- tbl[[column]][idx]
      ifelse(is.finite(candidate), candidate, fallback)
    }

    low_pop_fraction <- apply_fraction(weighted_low, "population_weight_fraction", low_pop_fraction)
    med_pop_fraction <- apply_fraction(weighted_medium, "population_weight_fraction", med_pop_fraction)
    high_pop_fraction <- apply_fraction(weighted_high, "population_weight_fraction", high_pop_fraction)
    low_child_fraction <- apply_fraction(weighted_low, "children_weight_fraction", low_child_fraction)
    med_child_fraction <- apply_fraction(weighted_medium, "children_weight_fraction", med_child_fraction)
    high_child_fraction <- apply_fraction(weighted_high, "children_weight_fraction", high_child_fraction)
    selected_pop_fraction <- apply_fraction(weighted_selected, "population_weight_fraction", selected_pop_fraction)
    selected_child_fraction <- apply_fraction(weighted_selected, "children_weight_fraction", selected_child_fraction)

    estimation_method <- paste(
      "Population-distribution-weighted estimate using optional spatial weights layer",
      paste0("'", attr(data$population_weights, "prep_source_layer"), "'"),
      "within each barangay; area weighting is used only where weights are unavailable"
    )
  }

  x %>%
    dplyr::mutate(
      population_low_exposed = round(pop * low_pop_fraction),
      population_medium_exposed = round(pop * med_pop_fraction),
      population_high_exposed = round(pop * high_pop_fraction),
      children_low_exposed = round(children * low_child_fraction),
      children_medium_exposed = round(children * med_child_fraction),
      children_high_exposed = round(children * high_child_fraction),
      selected_hazard_area_pct = selected_pct,
      selected_population_weighted_pct = selected_pop_fraction * 100,
      selected_children_weighted_pct = selected_child_fraction * 100,
      estimated_affected_population = round(pop * selected_pop_fraction),
      estimated_affected_children = round(children * selected_child_fraction),
      estimated_unaffected_population = pmax(pop - round(pop * selected_pop_fraction), 0),
      minimum_hazard_counted = minimum_hazard,
      population_estimation_method = estimation_method,
      affected_population_definition = "People estimated to reside within the selected mapped flood-hazard area; not an observed victim or casualty count"
    ) %>%
    dplyr::select(-dplyr::any_of("barangay_key"))
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
    population_weighting_mode = if (population_weighting_available(data)) "spatial_weights" else "uniform_area_weighting",
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

hazard_affected_population_metadata <- function(data = BASELINE_DATA) {
  weighting_mode <- if (population_weighting_available(data)) {
    paste0(
      "Optional within-barangay spatial weighting layer '",
      attr(data$population_weights, "prep_source_layer"),
      "'"
    )
  } else {
    "Uniform area weighting within each barangay"
  }

  list(
    model_name = "PREP Hazard Exposure and Affected Population Model",
    version = BASELINE_MODEL_VERSION,
    model_type = "deterministic GIS exposure estimation",
    predictive = FALSE,
    primary_measure = "estimated affected population",
    affected_definition = "Population estimated to reside inside the selected mapped flood-hazard zone.",
    hazard = "Flood inundation hazard represented by UP DREAM/LiPAD 5-, 25-, and 100-year reference flood-hazard polygons.",
    hazard_classes = hazard_severity_definitions(),
    scenarios = hazard_scenario_definitions(),
    population_method = weighting_mode,
    children_method = if (population_weighting_available(data)) "Spatial child-weight layer when available; otherwise population-weight distribution is used for child spatial weighting" else "Estimated affected children = barangay child population x selected mapped hazard-area fraction",
    facility_method = "A mapped facility is exposed when its point intersects the selected flood-hazard polygon.",
    automatic_rule_config = current_exposure_rule_config(),
    key_assumption = if (population_weighting_available(data)) "Installed spatial weights approximate within-barangay population distribution; their quality controls exposure accuracy." else "Population and children are uniformly distributed inside each barangay because a gridded population layer is not installed.",
    important_limitations = c(
      "Affected population is an exposure estimate, not an observed count of victims, evacuees, injuries, or casualties.",
      "Return-period maps are reference hazard scenarios and are not live flood forecasts.",
      "Areas outside the mapped polygons mean no mapped hazard for that selected scenario; they are not guaranteed safe.",
      "Automatic Alert/Alarm/Critical mappings must not be described as calibrated unless the installed rule configuration is explicitly approved after holdout validation.",
      "Population baseline is 2024 while child-population baseline is 2020.",
      if (population_weighting_available(data)) "Spatial weighting improves on uniform area weighting but does not directly observe where every resident is located." else "Uniform population distribution may overestimate or underestimate exposure within individual barangays."
    )
  )
}

# Backward-compatible function names used by the earlier hazard-exposure build.
estimate_barangay_exposure <- estimate_barangay_affected_population
estimate_city_exposure <- estimate_city_affected_population
hazard_exposure_model_metadata <- hazard_affected_population_metadata
