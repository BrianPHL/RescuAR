find_prep_root <- function(start = getwd()) {
  current <- normalizePath(start, winslash = "/", mustWork = TRUE)
  repeat {
    marker <- file.path(current, "data", "data_manifest.json")
    if (file.exists(marker)) return(current)
    parent <- dirname(current)
    if (identical(parent, current)) {
      stop("Could not locate PREP baseline project root. Run from the extracted project folder.")
    }
    current <- parent
  }
}

PREP_ROOT <- find_prep_root()
prep_path <- function(...) file.path(PREP_ROOT, ...)

BASELINE_MODEL_VERSION <- "hazard-affected-population-1.0.0"
