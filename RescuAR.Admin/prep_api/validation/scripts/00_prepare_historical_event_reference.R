# ============================================================
# RESCUAR / PREP
# REGISTERED HISTORICAL EVENT GEOREFERENCE + TRACE UTILITY
# Revised: robust Tumana lookup + tracing accuracy aids
#
# Purpose:
#   Convert a registered historical source-map image (e.g. Espina
#   Ondoy 2009 / 2013 monsoon Tumana maps) into an independently
#   digitized observed_flood.gpkg using paired control-point markers.
#
# Method:
#   1. Select source-image control points.
#   2. Select the SAME landmarks on the PREP UTM reference map.
#   3. Fit an affine source->EPSG:32651 transform and report RMSE.
#   4. Overlay non-hazard reference guides (Tumana boundary / river)
#      back onto the source image after registration.
#   5. Select high-confidence flood-boundary markers.
#   6. Trace one or more outer flood polygons and optional exclusion holes.
#   7. Transform, combine, and clip the trace to the event observation scope.
#   8. Save QC files and, after explicit confirmation, mark the event
#      geometry as ready in events.csv (role remains reference_only).
#
# IMPORTANT:
#   - Control points/markers improve registration and repeatability;
#     they DO NOT create new ground truth.
#   - Tumana boundary / Marikina River overlays are orientation aids only.
#   - LiPAD/PREP hazard output must NOT be used as a tracing guide.
#   - Do not tune points or the trace to force validation metrics to pass.
# ============================================================

suppressPackageStartupMessages({
  library(sf)
  library(terra)
  library(dplyr)
  library(readr)
})

# ============================================================
# 0. SETTINGS
# ============================================================

# Change to "monsoon_2013_tumana" for the second Espina event,
# or set PREP_EVENT_ID before sourcing the script.
EVENT_ID <- trimws(Sys.getenv("PREP_EVENT_ID", "ondoy_2009_tumana"))

# Georeferencing controls.
# At least 6 well-distributed points are recommended; 8-12 is preferable.
NUMBER_OF_CONTROL_POINTS <- 8

# High-confidence flood-edge anchors shown while tracing.
# These are manually selected from the historical source map itself.
NUMBER_OF_BOUNDARY_MARKERS <- 12

# Trace geometry controls.
# Increase these only when the source map genuinely contains separate
# disconnected flooded regions or clearly unflooded holes.
NUMBER_OF_OUTER_POLYGONS <- 1
NUMBER_OF_EXCLUSION_HOLES <- 0

# Visual tracing aids. These do not alter the trace automatically.
DRAW_TRACE_GRID <- TRUE
TRACE_GRID_COLUMNS <- 10
TRACE_GRID_ROWS <- 10
DRAW_REGISTERED_REFERENCE_GUIDES <- TRUE

# Optional snapping of manually clicked trace vertices to manually created
# boundary markers. OFF by default because it changes geometry.
# If enabled, only vertices already very close to a boundary marker are snapped.
SNAP_TRACE_TO_BOUNDARY_MARKERS <- FALSE
SNAP_DISTANCE_FRACTION_OF_SOURCE_DIAGONAL <- 0.006

# Project QC thresholds only; these are not official NAMRIA standards.
MAX_AFFINE_RMSE_M <- 50
MAX_LOO_RMSE_M <- 80

# Both Espina figures depict Barangay Tumana as their documented map scope.
# Keep TRUE only when the complete Tumana boundary is visibly represented
# by the selected source map.
CREATE_TUMANA_OBSERVATION_DOMAIN <- TRUE

# Registry is updated only after QC passes and the user types YES.
UPDATE_EVENT_REGISTRY_AFTER_CONFIRMATION <- TRUE

# ============================================================
# 1. PATHS / HELPERS
# ============================================================

find_project_root <- function(start = getwd()) {
  current <- normalizePath(start, winslash = "/", mustWork = TRUE)

  repeat {
    marker <- file.path(
      current, "prep_api", "data", "spatial", "baseline_spatial.gpkg"
    )
    if (file.exists(marker)) return(current)

    # Allows execution while the working directory is already prep_api/.
    local_marker <- file.path(current, "data", "spatial", "baseline_spatial.gpkg")
    if (file.exists(local_marker)) return(dirname(current))

    parent <- dirname(current)
    if (identical(parent, current)) {
      stop(
        "Could not locate RescuAR.Admin project root.\n",
        "Open RStudio inside RescuAR.Admin or one of its subfolders."
      )
    }
    current <- parent
  }
}

resolve_path <- function(path, prep_root) {
  path <- trimws(as.character(path))
  if (length(path) == 0 || is.na(path) || !nzchar(path)) return(NA_character_)
  if (grepl("^[A-Za-z]:[/\\\\]|^/", path)) return(path)
  file.path(prep_root, path)
}

normalize_name <- function(x) {
  x <- iconv(as.character(x), from = "", to = "ASCII//TRANSLIT")
  x[is.na(x)] <- ""
  gsub("[^A-Z0-9]", "", toupper(x))
}

first_existing_column <- function(x, candidates) {
  lower_names <- tolower(names(x))
  for (candidate in tolower(candidates)) {
    idx <- which(lower_names == candidate)
    if (length(idx) > 0) return(names(x)[idx[1]])
  }
  NA_character_
}

select_tumana_geometry <- function(barangays) {
  # Prefer a normalized name match, but tolerate alternate field names and
  # multiple rows representing the same barangay. If multiple matching rows
  # exist, they are unioned rather than treated as an error.
  name_candidates <- c(
    "barangay_name", "name", "brgy_name", "barangay", "brgy",
    "adm4_en", "adm4_name", "shapeName"
  )
  name_col <- first_existing_column(barangays, name_candidates)

  idx <- integer(0)
  matched_by <- NA_character_

  if (!is.na(name_col)) {
    values <- normalize_name(barangays[[name_col]])
    aliases <- normalize_name(c(
      "Tumana", "Barangay Tumana", "Brgy Tumana", "Brgy. Tumana"
    ))

    idx <- which(values %in% aliases)

    # Fallback for labels such as "Tumana, Marikina".
    if (length(idx) == 0) {
      idx <- which(grepl("TUMANA", values, fixed = TRUE))
    }

    if (length(idx) > 0) matched_by <- paste0("name field '", name_col, "'")
  }

  # Fallback to known/likely barangay identifier columns when name matching fails.
  if (length(idx) == 0) {
    id_candidates <- c(
      "barangay_id", "psgc", "psgc_code", "brgy_code", "barangay_code",
      "adm4_pcode", "code"
    )
    id_col <- first_existing_column(barangays, id_candidates)

    if (!is.na(id_col)) {
      ids <- gsub("[^0-9]", "", as.character(barangays[[id_col]]))

      # Current PREP baseline uses 1380700016. The older PSGC form is kept
      # as a compatibility fallback for alternate barangay datasets.
      tumana_ids <- c("1380700016", "1374020016")
      idx <- which(ids %in% tumana_ids)
      if (length(idx) > 0) matched_by <- paste0("ID field '", id_col, "'")
    }
  }

  if (length(idx) == 0) {
    diagnostic <- ""
    if (!is.na(name_col)) {
      diagnostic <- paste0(
        "\nValues found in ", name_col, ":\n  - ",
        paste(unique(as.character(barangays[[name_col]])), collapse = "\n  - ")
      )
    }

    stop(
      "Could not identify Barangay Tumana in baseline_spatial.gpkg.",
      diagnostic,
      "\nExpected a Tumana name value or a recognized Tumana barangay ID."
    )
  }

  selected <- barangays[idx, , drop = FALSE]
  union_geom <- st_union(st_geometry(selected)) |>
    st_make_valid()

  result <- st_sf(
    barangay_name = "Tumana",
    matched_rows = length(idx),
    matched_by = matched_by,
    geometry = union_geom
  )

  cat(
    "Tumana identified using ", matched_by,
    " (", length(idx), " matching row(s)).\n",
    sep = ""
  )

  result
}

fit_affine <- function(src_x, src_y, ref_x, ref_y) {
  fx <- lm(ref_x ~ src_x + src_y)
  fy <- lm(ref_y ~ src_x + src_y)

  px <- predict(fx)
  py <- predict(fy)
  err <- sqrt((px - ref_x)^2 + (py - ref_y)^2)

  list(
    fit_x = fx,
    fit_y = fy,
    predicted_x = px,
    predicted_y = py,
    error_m = err,
    rmse_m = sqrt(mean(err^2))
  )
}

apply_affine <- function(x, y, affine) {
  d <- data.frame(src_x = x, src_y = y)
  cbind(
    x = as.numeric(predict(affine$fit_x, newdata = d)),
    y = as.numeric(predict(affine$fit_y, newdata = d))
  )
}

# Inverse affine transform used only to draw neutral UTM reference features
# (Tumana boundary / river) back on top of the source image as registration aids.
apply_inverse_affine <- function(utm_x, utm_y, affine) {
  bx <- coef(affine$fit_x)
  by <- coef(affine$fit_y)

  A <- matrix(
    c(
      unname(bx["src_x"]), unname(bx["src_y"]),
      unname(by["src_x"]), unname(by["src_y"])
    ),
    nrow = 2,
    byrow = TRUE
  )

  if (!is.finite(det(A)) || abs(det(A)) < 1e-12) {
    stop("Affine transform is singular; cannot draw registered reference guides.")
  }

  offset <- c(unname(bx["(Intercept)"]), unname(by["(Intercept)"]))
  uv <- rbind(utm_x - offset[1], utm_y - offset[2])
  src <- solve(A, uv)

  cbind(x = src[1, ], y = src[2, ])
}

loo_rmse <- function(src_x, src_y, ref_x, ref_y) {
  n <- length(src_x)
  if (n < 4) return(NA_real_)

  errs <- numeric(n)
  for (i in seq_len(n)) {
    keep <- setdiff(seq_len(n), i)
    a <- fit_affine(src_x[keep], src_y[keep], ref_x[keep], ref_y[keep])
    pred <- apply_affine(src_x[i], src_y[i], a)
    errs[i] <- sqrt(
      (pred[1, "x"] - ref_x[i])^2 +
        (pred[1, "y"] - ref_y[i])^2
    )
  }

  sqrt(mean(errs^2))
}

plot_source <- function(source_raster, main, sub = NULL) {
  plotRGB(
    source_raster,
    r = 1,
    g = min(2, nlyr(source_raster)),
    b = min(3, nlyr(source_raster)),
    stretch = "lin",
    axes = TRUE,
    main = main
  )
  if (!is.null(sub)) title(sub = sub)
}

draw_trace_grid <- function(source_raster, nx = 10, ny = 10) {
  if (!isTRUE(DRAW_TRACE_GRID)) return(invisible(NULL))

  e <- terra::ext(source_raster)
  xs <- seq(e$xmin, e$xmax, length.out = nx + 1)
  ys <- seq(e$ymin, e$ymax, length.out = ny + 1)

  grid_col <- grDevices::adjustcolor("white", alpha.f = 0.35)
  abline(v = xs, h = ys, col = grid_col, lty = 3, lwd = 0.8)

  # Labels are positioned slightly inside the image so they remain visible.
  x_centers <- (xs[-1] + xs[-length(xs)]) / 2
  y_centers <- (ys[-1] + ys[-length(ys)]) / 2

  col_labels <- if (nx <= 26) LETTERS[seq_len(nx)] else as.character(seq_len(nx))
  row_labels <- as.character(seq_len(ny))

  text(
    x_centers,
    rep(e$ymax - 0.015 * (e$ymax - e$ymin), length(x_centers)),
    labels = col_labels,
    col = "white",
    cex = 0.7,
    font = 2
  )
  text(
    rep(e$xmin + 0.012 * (e$xmax - e$xmin), length(y_centers)),
    y_centers,
    labels = row_labels,
    col = "white",
    cex = 0.7,
    font = 2
  )

  invisible(NULL)
}

plot_geometry_on_source <- function(geometry, affine, col, lwd = 2, lty = 1) {
  if (is.null(geometry) || length(geometry) == 0) return(invisible(NULL))

  coords <- tryCatch(
    st_coordinates(geometry),
    error = function(e) NULL
  )
  if (is.null(coords) || nrow(coords) == 0) return(invisible(NULL))

  group_cols <- grep("^L[0-9]+$", colnames(coords), value = TRUE)

  if (length(group_cols) == 0) {
    groups <- list(seq_len(nrow(coords)))
  } else {
    grouping <- interaction(
      as.data.frame(coords[, group_cols, drop = FALSE]),
      drop = TRUE,
      lex.order = TRUE
    )
    groups <- split(seq_len(nrow(coords)), grouping)
  }

  for (idx in groups) {
    if (length(idx) < 2) next
    src <- apply_inverse_affine(coords[idx, "X"], coords[idx, "Y"], affine)
    lines(src[, "x"], src[, "y"], col = col, lwd = lwd, lty = lty)
  }

  invisible(NULL)
}

draw_registered_reference_guides <- function(tumana, river, affine) {
  if (!isTRUE(DRAW_REGISTERED_REFERENCE_GUIDES)) return(invisible(NULL))

  # Boundary / river only. No LiPAD hazard geometry is used here.
  tumana_boundary <- st_boundary(st_union(st_geometry(tumana)))
  plot_geometry_on_source(
    tumana_boundary,
    affine,
    col = "cyan",
    lwd = 2.2,
    lty = 2
  )

  if (!is.null(river) && nrow(river) > 0) {
    plot_geometry_on_source(
      st_geometry(river),
      affine,
      col = "deepskyblue",
      lwd = 1.8,
      lty = 1
    )
  }

  invisible(NULL)
}

snap_trace_to_markers <- function(coords, markers, source_raster) {
  if (!isTRUE(SNAP_TRACE_TO_BOUNDARY_MARKERS)) return(coords)
  if (is.null(markers) || nrow(markers) == 0 || nrow(coords) == 0) return(coords)

  e <- terra::ext(source_raster)
  diagonal <- sqrt((e$xmax - e$xmin)^2 + (e$ymax - e$ymin)^2)
  max_dist <- diagonal * SNAP_DISTANCE_FRACTION_OF_SOURCE_DIAGONAL

  result <- coords

  for (i in seq_len(nrow(result))) {
    d <- sqrt(
      (markers$source_x - result[i, 1])^2 +
        (markers$source_y - result[i, 2])^2
    )
    j <- which.min(d)
    if (length(j) == 1 && is.finite(d[j]) && d[j] <= max_dist) {
      result[i, 1] <- markers$source_x[j]
      result[i, 2] <- markers$source_y[j]
    }
  }

  result
}

trace_one_polygon <- function(
    source_raster,
    affine,
    tumana,
    river,
    src_clicks,
    boundary_markers,
    title,
    subtitle,
    trace_col = "magenta"
) {
  plot_source(source_raster, title, subtitle)
  draw_trace_grid(source_raster, TRACE_GRID_COLUMNS, TRACE_GRID_ROWS)
  draw_registered_reference_guides(tumana, river, affine)

  # GCPs
  points(
    src_clicks$x, src_clicks$y,
    pch = 4, col = "cyan", cex = 1.0, lwd = 2
  )
  text(
    src_clicks$x, src_clicks$y,
    labels = paste0("G", seq_along(src_clicks$x)),
    pos = 3, col = "cyan", cex = 0.7
  )

  # Flood-edge markers
  if (nrow(boundary_markers) > 0) {
    points(
      boundary_markers$source_x,
      boundary_markers$source_y,
      pch = 4, col = "yellow", cex = 1.2, lwd = 2
    )
    text(
      boundary_markers$source_x,
      boundary_markers$source_y,
      labels = paste0("B", boundary_markers$marker_id),
      pos = 3, col = "yellow", cex = 0.75
    )
  }

  clicks <- locator(type = "l", lwd = 2, col = trace_col)
  if (is.null(clicks) || length(clicks$x) < 3) {
    stop("Each traced polygon requires at least 3 vertices.")
  }

  coords <- cbind(clicks$x, clicks$y)
  coords <- snap_trace_to_markers(coords, boundary_markers, source_raster)

  if (!isTRUE(all.equal(coords[1, ], coords[nrow(coords), ]))) {
    coords <- rbind(coords, coords[1, ])
  }

  coords
}

safe_union <- function(x) {
  if (length(x) == 0) return(NULL)
  st_union(do.call(c, x)) |>
    st_make_valid()
}

# ============================================================
# 2. LOCATE PROJECT / REGISTERED EVENT / SOURCE MAP
# ============================================================

project_root <- find_project_root()
prep_root <- file.path(project_root, "prep_api")
validation_dir <- file.path(prep_root, "validation")
events_csv <- file.path(validation_dir, "events", "events.csv")
spatial_gpkg <- file.path(prep_root, "data", "spatial", "baseline_spatial.gpkg")

if (!file.exists(events_csv)) stop("Missing event registry: ", events_csv)
if (!file.exists(spatial_gpkg)) stop("Missing baseline spatial GPKG: ", spatial_gpkg)

events <- readr::read_csv(
  events_csv,
  show_col_types = FALSE,
  progress = FALSE
)

if (!"source_map_path" %in% names(events)) {
  stop(
    "events.csv does not contain source_map_path. ",
    "Install the Espina/Ecological event-data patch first."
  )
}

row_index <- which(events$event_id == EVENT_ID)
if (length(row_index) != 1) {
  stop("EVENT_ID not found exactly once in events.csv: ", EVENT_ID)
}

event <- events[row_index, , drop = FALSE]
source_path <- resolve_path(event$source_map_path[[1]], prep_root)

if (is.na(source_path) || !file.exists(source_path)) {
  stop(
    "Missing source map for event: ", source_path,
    "\nCheck source_map_path in validation/events/events.csv."
  )
}

event_dir <- dirname(source_path)
dir.create(event_dir, recursive = TRUE, showWarnings = FALSE)

cat(
  "\nPreparing historical reference event:\n",
  "  event_id: ", EVENT_ID, "\n",
  "  source:   ", source_path, "\n",
  sep = ""
)

# ============================================================
# 3. LOAD REFERENCE SPATIAL DATA + SOURCE IMAGE
# ============================================================

barangays <- st_read(
  spatial_gpkg,
  layer = "barangays",
  quiet = TRUE
) |>
  st_make_valid() |>
  st_transform(32651)

tumana <- select_tumana_geometry(barangays) |>
  st_transform(32651)

river <- NULL
layers <- st_layers(spatial_gpkg)$name
if ("marikina_river" %in% layers) {
  river <- st_read(
    spatial_gpkg,
    layer = "marikina_river",
    quiet = TRUE
  ) |>
    st_make_valid() |>
    st_transform(32651)
}

source_raster <- terra::rast(source_path)
if (nlyr(source_raster) < 3) {
  stop("Historical source map must be an RGB image with at least 3 bands.")
}

# ============================================================
# 4. PAIRED CONTROL-POINT MARKERS
# ============================================================

graphics.off()
windows(width = 12, height = 10)
plot_source(
  source_raster,
  paste0(EVENT_ID, " — SOURCE control points"),
  paste0(
    "Click exactly ", NUMBER_OF_CONTROL_POINTS,
    " recognizable landmarks distributed around the map"
  )
)
draw_trace_grid(source_raster, TRACE_GRID_COLUMNS, TRACE_GRID_ROWS)

cat(
  "\nSOURCE MAP CONTROL POINTS\n",
  "Click well-separated, unambiguous landmarks that are visible on BOTH maps.\n",
  "Preferred examples: Tumana boundary corners/intersections, river bends, bridge/road\n",
  "intersections, or other stable features. Avoid flood-color edges as GCPs.\n",
  sep = ""
)

src_clicks <- locator(
  n = NUMBER_OF_CONTROL_POINTS,
  type = "p",
  pch = 4,
  col = "yellow",
  cex = 1.2,
  lwd = 2
)

if (is.null(src_clicks) || length(src_clicks$x) < 3) {
  stop("At least 3 source control points are required.")
}

source_n <- length(src_clicks$x)
text(
  src_clicks$x, src_clicks$y,
  labels = seq_len(source_n),
  pos = 3,
  col = "yellow",
  cex = 0.8
)


invisible(readline(
  "Press ENTER to select the matching UTM reference points..."
))

# Keep the historical source map open.
# Do not call graphics.off() here.

windows(width = 12, height = 10)

ref_x <- numeric(0)
ref_y <- numeric(0)

redraw_reference <- function(next_id) {
  
  plot(
    st_geometry(barangays),
    border = "grey65",
    col = NA,
    main = paste0(EVENT_ID, " — UTM landmark matching")
  )
  
  plot(
    st_geometry(tumana),
    add = TRUE,
    border = "red",
    lwd = 3
  )
  
  if (!is.null(river)) {
    plot(
      st_geometry(river),
      add = TRUE,
      col = "deepskyblue3",
      lwd = 2
    )
  }
  
  n_matched <- length(ref_x)
  
  # Display confirmed landmark matches.
  if (n_matched > 0) {
    points(ref_x, ref_y,
           pch = 4, col = "gold", lwd = 2)
    
    text(ref_x, ref_y,
         labels = seq_len(n_matched),
         pos = 3, col = "goldenrod4")
  }
  
  # Estimate remaining locations after 3 matches.
  if (n_matched >= 3 && next_id <= source_n) {
    
    preview <- fit_affine(
      src_clicks$x[seq_len(n_matched)],
      src_clicks$y[seq_len(n_matched)],
      ref_x, ref_y
    )
    
    coefficients <- c(
      coef(preview$fit_x),
      coef(preview$fit_y)
    )
    
    if (all(is.finite(coefficients))) {
      
      remaining <- seq.int(next_id, source_n)
      
      predicted <- apply_affine(
        src_clicks$x[remaining],
        src_clicks$y[remaining],
        preview
      )
      
      # Blue circles are approximate locations only.
      points(
        predicted[, "x"],
        predicted[, "y"],
        pch = 1,
        col = "dodgerblue3",
        cex = 1.8,
        lwd = 2
      )
      
      text(
        predicted[, "x"],
        predicted[, "y"],
        labels = paste0("~", remaining),
        pos = 3,
        col = "dodgerblue3"
      )
    }
  }
  
  legend(
    "bottomleft",
    legend = c("Matched", "Estimated"),
    col = c("goldenrod4", "dodgerblue3"),
    pch = c(4, 1),
    bty = "n"
  )
}

for (i in seq_len(source_n)) {
  
  redraw_reference(i)
  
  cat(
    "\nMatch landmark ", i, " of ", source_n,
    if (i <= 3) " (manual anchor)"
    else " (blue estimate available)",
    "\n",
    sep = ""
  )
  
  clicked <- locator(
    n = 1,
    type = "p",
    pch = 4,
    col = "gold",
    lwd = 2
  )
  
  if (is.null(clicked) || length(clicked$x) != 1) {
    stop("Landmark matching was interrupted.")
  }
  
  ref_x <- c(ref_x, clicked$x)
  ref_y <- c(ref_y, clicked$y)
}

redraw_reference(source_n + 1)

ref_clicks <- list(
  x = ref_x,
  y = ref_y
)


affine <- fit_affine(
  control_points$source_x,
  control_points$source_y,
  control_points$utm_x,
  control_points$utm_y
)

control_points$predicted_utm_x <- affine$predicted_x
control_points$predicted_utm_y <- affine$predicted_y
control_points$residual_m <- affine$error_m

loo <- loo_rmse(
  control_points$source_x,
  control_points$source_y,
  control_points$utm_x,
  control_points$utm_y
)

control_csv <- file.path(event_dir, "georeference_control_points.csv")
write.csv(control_points, control_csv, row.names = FALSE)

cat(
  "\nAffine GCP QC:\n",
  "  RMSE:               ", round(affine$rmse_m, 2), " m\n",
  "  max residual:       ", round(max(affine$error_m), 2), " m\n",
  "  leave-one-out RMSE: ", round(loo, 2), " m\n",
  sep = ""
)

qc_pass <-
  is.finite(affine$rmse_m) &&
  affine$rmse_m <= MAX_AFFINE_RMSE_M &&
  (is.na(loo) || loo <= MAX_LOO_RMSE_M)

if (!qc_pass) {
  stop(
    "GCP QC did not pass the project thresholds.\n",
    "Re-run with better-distributed, clearer control points.\n",
    "Do not continue tracing from a poorly registered historical map."
  )
}

graphics.off()

# ============================================================
# 5. REGISTERED GUIDE PREVIEW
# ============================================================

if (isTRUE(DRAW_REGISTERED_REFERENCE_GUIDES)) {
  windows(width = 12, height = 10)
  plot_source(
    source_raster,
    paste0(EVENT_ID, " — registration QC preview"),
    "cyan dashed = Tumana boundary | blue = Marikina River | yellow = GCPs"
  )
  draw_trace_grid(source_raster, TRACE_GRID_COLUMNS, TRACE_GRID_ROWS)
  draw_registered_reference_guides(tumana, river, affine)
  points(
    src_clicks$x, src_clicks$y,
    pch = 4, col = "yellow", cex = 1.1, lwd = 2
  )
  text(
    src_clicks$x, src_clicks$y,
    labels = paste0("G", seq_len(source_n)),
    pos = 3,
    col = "yellow",
    cex = 0.7
  )

  cat(
    "\nREGISTRATION QC PREVIEW\n",
    "Check whether the cyan Tumana outline and blue river align with the source map.\n",
    "If they are visibly shifted/rotated, stop and redo the GCPs even if numeric QC passed.\n",
    sep = ""
  )

  answer <- toupper(trimws(readline(
    "If the registration looks acceptable, type YES to continue: "
  )))

  if (!identical(answer, "YES")) {
    stop("Registration was not confirmed. Re-run with improved control points.")
  }
  graphics.off()
}

# ============================================================
# 6. HIGH-CONFIDENCE FLOOD-BOUNDARY MARKERS
# ============================================================

boundary_markers <- data.frame(
  marker_id = integer(),
  source_x = numeric(),
  source_y = numeric(),
  utm_x = numeric(),
  utm_y = numeric()
)

if (NUMBER_OF_BOUNDARY_MARKERS > 0) {
  windows(width = 12, height = 10)
  plot_source(
    source_raster,
    paste0(EVENT_ID, " — high-confidence flood-boundary markers"),
    paste0(
      "Click up to ", NUMBER_OF_BOUNDARY_MARKERS,
      " clear flood-edge anchors"
    )
  )
  draw_trace_grid(source_raster, TRACE_GRID_COLUMNS, TRACE_GRID_ROWS)
  draw_registered_reference_guides(tumana, river, affine)

  points(
    src_clicks$x,
    src_clicks$y,
    pch = 4,
    col = "cyan",
    cex = 1.0,
    lwd = 2
  )
  text(
    src_clicks$x,
    src_clicks$y,
    labels = paste0("G", seq_len(source_n)),
    pos = 3,
    col = "cyan",
    cex = 0.7
  )

  cat(
    "\nBOUNDARY MARKERS\n",
    "Click unambiguous flood-color edge points on the historical source map.\n",
    "Distribute markers around turns, narrow sections, and visually difficult edges.\n",
    "These markers remain visible during tracing. Right-click/Esc to finish early.\n",
    sep = ""
  )

  b <- locator(
    n = NUMBER_OF_BOUNDARY_MARKERS,
    type = "p",
    pch = 4,
    col = "yellow",
    cex = 1.2,
    lwd = 2
  )

  if (!is.null(b) && length(b$x) > 0) {
    transformed_b <- apply_affine(b$x, b$y, affine)

    boundary_markers <- data.frame(
      marker_id = seq_along(b$x),
      source_x = b$x,
      source_y = b$y,
      utm_x = transformed_b[, "x"],
      utm_y = transformed_b[, "y"]
    )

    text(
      b$x,
      b$y,
      labels = paste0("B", boundary_markers$marker_id),
      pos = 3,
      col = "yellow",
      cex = 0.75
    )

    write.csv(
      boundary_markers,
      file.path(event_dir, "trace_boundary_markers.csv"),
      row.names = FALSE
    )
  }

  invisible(readline(
    "Press ENTER to begin the flood-boundary trace..."
  ))
  graphics.off()
}

# ============================================================
# 7. TRACE EVENT FLOOD EXTENT ON SOURCE IMAGE
# ============================================================

outer_source_polygons <- vector("list", NUMBER_OF_OUTER_POLYGONS)

for (i in seq_len(NUMBER_OF_OUTER_POLYGONS)) {
  windows(width = 12, height = 10)

  cat(
    "\nTrace OUTER flood polygon ", i, " of ", NUMBER_OF_OUTER_POLYGONS, ".\n",
    "Follow only the historical source-map flood boundary.\n",
    "Right-click or press ESC to finish the polygon.\n",
    sep = ""
  )

  outer_source_polygons[[i]] <- trace_one_polygon(
    source_raster = source_raster,
    affine = affine,
    tumana = tumana,
    river = river,
    src_clicks = src_clicks,
    boundary_markers = boundary_markers,
    title = paste0(
      EVENT_ID,
      " — trace outer flood polygon ", i, "/", NUMBER_OF_OUTER_POLYGONS
    ),
    subtitle = paste0(
      "magenta = trace | yellow = flood-edge markers | cyan = GCPs; ",
      "cyan dashed / blue = neutral reference guides"
    ),
    trace_col = "magenta"
  )

  graphics.off()
}

hole_source_polygons <- vector("list", NUMBER_OF_EXCLUSION_HOLES)

if (NUMBER_OF_EXCLUSION_HOLES > 0) {
  for (i in seq_len(NUMBER_OF_EXCLUSION_HOLES)) {
    windows(width = 12, height = 10)

    cat(
      "\nTrace EXCLUSION HOLE ", i, " of ", NUMBER_OF_EXCLUSION_HOLES, ".\n",
      "Use this only for a clearly NOT-FLOODED area enclosed by the outer trace.\n",
      sep = ""
    )

    hole_source_polygons[[i]] <- trace_one_polygon(
      source_raster = source_raster,
      affine = affine,
      tumana = tumana,
      river = river,
      src_clicks = src_clicks,
      boundary_markers = boundary_markers,
      title = paste0(
        EVENT_ID,
        " — trace exclusion hole ", i, "/", NUMBER_OF_EXCLUSION_HOLES
      ),
      subtitle = "orange = exclusion hole",
      trace_col = "orange"
    )

    graphics.off()
  }
}

source_polygon_to_utm_sfc <- function(coords) {
  utm_coords <- apply_affine(coords[, 1], coords[, 2], affine)
  st_sfc(st_polygon(list(utm_coords)), crs = 32651) |>
    st_make_valid()
}

outer_geoms <- lapply(outer_source_polygons, source_polygon_to_utm_sfc)
outer_union <- safe_union(outer_geoms)

if (is.null(outer_union) || length(outer_union) == 0 || all(st_is_empty(outer_union))) {
  stop("Outer flood trace produced an empty geometry.")
}

raw_geom <- outer_union

if (NUMBER_OF_EXCLUSION_HOLES > 0) {
  hole_geoms <- lapply(hole_source_polygons, source_polygon_to_utm_sfc)
  hole_union <- safe_union(hole_geoms)

  if (!is.null(hole_union) && length(hole_union) > 0 && !all(st_is_empty(hole_union))) {
    raw_geom <- suppressWarnings(st_difference(raw_geom, hole_union)) |>
      st_make_valid()
  }
}

raw_sf <- st_sf(event_id = EVENT_ID, geometry = raw_geom)

# Clip to the documented Tumana map scope.
observed_geom <- suppressWarnings(
  st_intersection(
    st_union(st_geometry(raw_sf)),
    st_union(st_geometry(tumana))
  )
) |>
  st_make_valid()

if (length(observed_geom) == 0 || all(st_is_empty(observed_geom))) {
  stop("Clipped observed geometry is empty.")
}

observed <- st_sf(
  event_id = EVENT_ID,
  geometry = observed_geom
)

# ============================================================
# 8. SAVE OBSERVED GEOMETRY + OBSERVATION DOMAIN
# ============================================================

raw_gpkg <- file.path(event_dir, "observed_flood_raw_transformed.gpkg")
obs_gpkg <- file.path(event_dir, "observed_flood.gpkg")
obs_geojson <- file.path(event_dir, "observed_flood.geojson")

for (p in c(raw_gpkg, obs_gpkg, obs_geojson)) {
  if (file.exists(p)) file.remove(p)
}

st_write(
  raw_sf,
  raw_gpkg,
  layer = "raw_transformed_trace",
  quiet = TRUE
)
st_write(
  observed,
  obs_gpkg,
  layer = "observed_flood",
  quiet = TRUE
)
st_write(
  observed,
  obs_geojson,
  quiet = TRUE
)

obs_domain_gpkg <- NA_character_
if (isTRUE(CREATE_TUMANA_OBSERVATION_DOMAIN)) {
  obs_domain_gpkg <- file.path(event_dir, "observation_domain.gpkg")
  if (file.exists(obs_domain_gpkg)) file.remove(obs_domain_gpkg)

  domain <- st_sf(
    event_id = EVENT_ID,
    geometry = st_union(st_geometry(tumana))
  )

  st_write(
    domain,
    obs_domain_gpkg,
    layer = "observation_domain",
    quiet = TRUE
  )
}

# ============================================================
# 9. FINAL QC + OPTIONAL REGISTRY UPDATE
# ============================================================

graphics.off()
windows(width = 12, height = 10)
plot(
  st_geometry(barangays),
  border = "grey70",
  col = NA,
  main = paste0(
    EVENT_ID,
    " — final transformed historical reference"
  )
)
plot(st_geometry(tumana), add = TRUE, border = "red", lwd = 2)
if (!is.null(river)) {
  plot(st_geometry(river), add = TRUE, col = "deepskyblue3", lwd = 2)
}
plot(
  st_geometry(observed),
  add = TRUE,
  border = "magenta",
  col = grDevices::adjustcolor("magenta", alpha.f = 0.18),
  lwd = 3
)

observed_area_km2 <-
  sum(as.numeric(st_area(observed)), na.rm = TRUE) / 1e6

qc <- data.frame(
  event_id = EVENT_ID,
  completed_at = format(Sys.time(), "%Y-%m-%dT%H:%M:%S%z"),
  source_map = basename(source_path),
  control_point_count = source_n,
  affine_rmse_m = affine$rmse_m,
  max_control_residual_m = max(affine$error_m),
  leave_one_out_rmse_m = loo,
  boundary_marker_count = nrow(boundary_markers),
  outer_polygon_count = NUMBER_OF_OUTER_POLYGONS,
  exclusion_hole_count = NUMBER_OF_EXCLUSION_HOLES,
  registered_reference_guides = DRAW_REGISTERED_REFERENCE_GUIDES,
  trace_grid_enabled = DRAW_TRACE_GRID,
  trace_marker_snapping_enabled = SNAP_TRACE_TO_BOUNDARY_MARKERS,
  observed_area_km2 = observed_area_km2,
  project_qc_pass = qc_pass,
  stringsAsFactors = FALSE
)

write.csv(
  qc,
  file.path(event_dir, "historical_trace_qc.csv"),
  row.names = FALSE
)

cat(
  "\nHistorical reference created:\n",
  "  ", obs_gpkg, "\n",
  "  observed area:        ", round(observed_area_km2, 4), " km2\n",
  "  affine RMSE:          ", round(affine$rmse_m, 2), " m\n",
  "  LOO RMSE:             ", round(loo, 2), " m\n",
  "  boundary markers:     ", nrow(boundary_markers), "\n",
  "  outer polygons:       ", NUMBER_OF_OUTER_POLYGONS, "\n",
  "  exclusion holes:      ", NUMBER_OF_EXCLUSION_HOLES, "\n",
  sep = ""
)

if (isTRUE(UPDATE_EVENT_REGISTRY_AFTER_CONFIRMATION)) {
  answer <- toupper(trimws(readline(
    paste0(
      "If the final magenta geometry correctly represents the HISTORICAL SOURCE MAP, ",
      "type YES to mark this event geometry ready: "
    )
  )))

  if (identical(answer, "YES")) {
    rel_obs <- file.path(
      "validation", "events", EVENT_ID, "observed_flood.gpkg"
    )

    rel_domain <- if (!is.na(obs_domain_gpkg)) {
      file.path(
        "validation", "events", EVENT_ID, "observation_domain.gpkg"
      )
    } else {
      ""
    }

    events$observed_flood_path[row_index] <- gsub("\\\\", "/", rel_obs)
    events$observation_domain_path[row_index] <- gsub("\\\\", "/", rel_domain)

    if ("event_status" %in% names(events)) {
      events$event_status[row_index] <- "ready"
    }

    readr::write_csv(events, events_csv, na = "")

    role_value <- if ("role" %in% names(events)) {
      as.character(events$role[row_index])
    } else {
      "reference_only"
    }

    cat(
      "events.csv updated. Event role remains '", role_value, "'.\n",
      sep = ""
    )
  } else {
    cat(
      "Registry not changed. Review/retrace the historical geometry before activation.\n"
    )
  }
}

cat(
  "\nNEXT STEPS:\n",
  "  1. Repeat this script for the other registered Espina event.\n",
  "  2. Review historical_trace_qc.csv and the final geometry for each event.\n",
  "  3. Keep these events as reference_only until their geometry/QC and intended role are defensible.\n",
  "  4. Only then use the multi-event calibration workflow (04_calibrate_scenario_rules.R).\n",
  "\nDo not proceed to calibration from an event whose 00 preparation step failed.\n",
  sep = ""
)
