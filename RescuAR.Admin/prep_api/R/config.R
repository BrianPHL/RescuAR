find_prep_root <- function(start = getwd()) {
  current <- normalizePath(start, winslash = "/", mustWork = TRUE)
  repeat {
    marker <- file.path(current, "data", "data_manifest.json")
    if (file.exists(marker)) return(current)
    parent <- dirname(current)
    if (identical(parent, current)) {
      stop("Could not locate PREP baseline project root. Run from the extracted prep_api folder or one of its subfolders.")
    }
    current <- parent
  }
}

PREP_ROOT <- find_prep_root()
prep_path <- function(...) file.path(PREP_ROOT, ...)

BASELINE_MODEL_VERSION <- "hazard-affected-population-1.1.0"

DEFAULT_EXPOSURE_RULE_CONFIG <- list(
  version = "provisional-1.0.0",
  approved = FALSE,
  source = "project_provisional_rule",
  calibration_event_count = 0L,
  holdout_event_count = 0L,
  generated_at = NULL,
  notes = "Default project mapping; not an empirically calibrated hydraulic equivalence.",
  rules = list(
    Alert = list(return_period = 5, minimum_hazard = "Low"),
    Alarm = list(return_period = 25, minimum_hazard = "Medium"),
    Critical = list(return_period = 100, minimum_hazard = "High")
  )
)

validate_exposure_rule_config <- function(config) {
  if (is.null(config$rules)) stop("Exposure rule config must contain a rules object.")

  required_states <- c("Alert", "Alarm", "Critical")
  missing_states <- setdiff(required_states, names(config$rules))
  if (length(missing_states) > 0) {
    stop("Exposure rule config is missing state(s): ", paste(missing_states, collapse = ", "))
  }

  for (state in required_states) {
    rule <- config$rules[[state]]
    rp <- suppressWarnings(as.numeric(rule$return_period))
    hazard <- tools::toTitleCase(tolower(as.character(rule$minimum_hazard)[1]))
    if (!rp %in% c(5, 25, 100)) {
      stop("Invalid return_period for ", state, ": ", rule$return_period)
    }
    if (!hazard %in% c("Low", "Medium", "High")) {
      stop("Invalid minimum_hazard for ", state, ": ", rule$minimum_hazard)
    }
    config$rules[[state]]$return_period <- rp
    config$rules[[state]]$minimum_hazard <- hazard
  }

  config$approved <- isTRUE(config$approved)
  config
}

read_exposure_rule_config <- function(path = prep_path("config", "exposure_scenario_rules.json")) {
  if (!file.exists(path)) {
    return(validate_exposure_rule_config(DEFAULT_EXPOSURE_RULE_CONFIG))
  }

  parsed <- tryCatch(
    jsonlite::fromJSON(path, simplifyVector = FALSE),
    error = function(e) NULL
  )

  if (is.null(parsed)) {
    warning("Could not parse exposure_scenario_rules.json; using provisional defaults.")
    return(validate_exposure_rule_config(DEFAULT_EXPOSURE_RULE_CONFIG))
  }

  tryCatch(
    validate_exposure_rule_config(parsed),
    error = function(e) {
      warning("Invalid exposure scenario rule config: ", conditionMessage(e), ". Using provisional defaults.")
      validate_exposure_rule_config(DEFAULT_EXPOSURE_RULE_CONFIG)
    }
  )
}

current_exposure_rule_config <- function() {
  read_exposure_rule_config()
}
