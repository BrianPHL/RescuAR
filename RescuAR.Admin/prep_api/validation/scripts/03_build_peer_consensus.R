# ============================================================
# RESCUAR / PREP
# PEER TRACE CONSENSUS BUILDER
#
# Purpose:
#   Combine independently produced peer flood traces into a
#   reproducible majority-agreement reference without changing
#   any LiPAD/PREP model data.
#
# Input:
#   prep_api/validation/peer_traces/*.gpkg or *.geojson
#
# Output:
#   validation/output/peer_trace_summary.csv
#   validation/output/peer_trace_pairwise_iou.csv
#   validation/output/peer_consensus_summary.csv
#   validation/work/observed_flood_peer_consensus.gpkg
#
# Optional:
#   Set PROMOTE_CONSENSUS_TO_ACTIVE <- TRUE to explicitly copy
#   the consensus to validation/input/observed_flood.gpkg.
# ============================================================

suppressPackageStartupMessages({
  library(sf)
  library(terra)
  library(dplyr)
})

# ------------------------------------------------------------
# SETTINGS
# ------------------------------------------------------------

CONSENSUS_GRID_RESOLUTION_M <- 20
PROMOTE_CONSENSUS_TO_ACTIVE <- FALSE

# ------------------------------------------------------------
# PROJECT ROOT
# ------------------------------------------------------------

find_project_root <- function(start = getwd()) {
  current <- normalizePath(start, winslash = "/", mustWork = TRUE)
  repeat {
    marker <- file.path(current, "prep_api", "data", "spatial", "baseline_spatial.gpkg")
    if (file.exists(marker)) return(current)

    local_marker <- file.path(current, "data", "spatial", "baseline_spatial.gpkg")
    if (file.exists(local_marker)) return(dirname(current))

    parent <- dirname(current)
    if (identical(parent, current)) {
      stop("Could not locate RescuAR.Admin. Run from the project or a subfolder.")
    }
    current <- parent
  }
}

project_root <- find_project_root()
prep_root <- file.path(project_root, "prep_api")
validation_dir <- file.path(prep_root, "validation")
peer_dir <- file.path(validation_dir, "peer_traces")
work_dir <- file.path(validation_dir, "work")
input_dir <- file.path(validation_dir, "input")
output_dir <- file.path(validation_dir, "output")
spatial_gpkg <- file.path(prep_root, "data", "spatial", "baseline_spatial.gpkg")

dir.create(peer_dir, recursive = TRUE, showWarnings = FALSE)
dir.create(work_dir, recursive = TRUE, showWarnings = FALSE)
dir.create(input_dir, recursive = TRUE, showWarnings = FALSE)
dir.create(output_dir, recursive = TRUE, showWarnings = FALSE)

# ------------------------------------------------------------
# HELPERS
# ------------------------------------------------------------

area_m2 <- function(x) {
  if (length(x) == 0 || all(st_is_empty(x))) return(0)
  sum(as.numeric(st_area(x)), na.rm = TRUE)
}

read_trace <- function(path) {
  if (grepl("\\.gpkg$", path, ignore.case = TRUE)) {
    layers <- st_layers(path)$name
    layer <- if ("observed_flood" %in% layers) "observed_flood" else layers[1]
    x <- st_read(path, layer = layer, quiet = TRUE)
  } else {
    x <- st_read(path, quiet = TRUE)
  }

  if (nrow(x) == 0 || is.na(st_crs(x))) {
    stop("Peer trace is empty or has no CRS: ", path)
  }

  x <- st_make_valid(x)
  x <- st_transform(x, 32651)
  st_union(st_geometry(x))
}

safe_iou <- function(a, b) {
  intersection <- suppressWarnings(st_intersection(a, b))
  union <- suppressWarnings(st_union(a, b))
  union_area <- area_m2(union)
  if (union_area <= 0) return(NA_real_)
  area_m2(intersection) / union_area
}

# ------------------------------------------------------------
# LOAD ANALYSIS BOUNDARY
# ------------------------------------------------------------

barangays <- st_read(spatial_gpkg, layer = "barangays", quiet = TRUE) |>
  st_make_valid() |>
  st_transform(32651)

marikina_boundary <- st_union(st_geometry(barangays))

# ------------------------------------------------------------
# LOAD INDEPENDENT PEER TRACES
# ------------------------------------------------------------

trace_files <- list.files(
  peer_dir,
  pattern = "\\.(gpkg|geojson)$",
  full.names = TRUE,
  ignore.case = TRUE
)

if (length(trace_files) < 2) {
  stop(
    paste0(
      "At least two independent peer traces are required.\n",
      "Place *.gpkg or *.geojson files in:\n",
      peer_dir
    )
  )
}

trace_ids <- tools::file_path_sans_ext(basename(trace_files))
traces <- lapply(trace_files, function(path) {
  geom <- read_trace(path)
  geom <- suppressWarnings(st_intersection(geom, marikina_boundary))
  st_make_valid(geom)
})
names(traces) <- trace_ids

# ------------------------------------------------------------
# PAIRWISE REPRODUCIBILITY METRICS
# ------------------------------------------------------------

trace_summary <- data.frame(
  evaluator_trace = trace_ids,
  area_km2 = vapply(traces, function(x) area_m2(x) / 1e6, numeric(1)),
  stringsAsFactors = FALSE
)

pairs <- combn(seq_along(traces), 2, simplify = FALSE)
pairwise <- bind_rows(lapply(pairs, function(pair) {
  i <- pair[1]
  j <- pair[2]
  data.frame(
    trace_a = trace_ids[i],
    trace_b = trace_ids[j],
    iou = safe_iou(traces[[i]], traces[[j]]),
    stringsAsFactors = FALSE
  )
}))

write.csv(
  trace_summary,
  file.path(output_dir, "peer_trace_summary.csv"),
  row.names = FALSE
)

write.csv(
  pairwise,
  file.path(output_dir, "peer_trace_pairwise_iou.csv"),
  row.names = FALSE
)

# ------------------------------------------------------------
# MAJORITY-VOTE RASTER CONSENSUS
# ------------------------------------------------------------

city_vect <- terra::vect(st_sf(id = 1, geometry = marikina_boundary))
template <- terra::rast(
  terra::ext(city_vect),
  resolution = CONSENSUS_GRID_RESOLUTION_M,
  crs = "EPSG:32651"
)

template <- terra::mask(
  terra::rasterize(city_vect, template, field = 1, background = NA),
  city_vect
)

vote_layers <- lapply(seq_along(traces), function(i) {
  trace_sf <- st_sf(id = 1, geometry = traces[[i]])
  r <- terra::rasterize(
    terra::vect(trace_sf),
    template,
    field = 1,
    background = 0,
    touches = TRUE
  )
  names(r) <- paste0("peer_", i)
  r
})

votes <- do.call(c, vote_layers)
vote_count <- terra::app(votes, sum, na.rm = TRUE)

# Strict majority: 2 peers -> both must agree; 3 peers -> 2+; 4 -> 3+.
required_votes <- floor(length(traces) / 2) + 1
consensus_mask <- terra::ifel(vote_count >= required_votes, 1, NA)
consensus_mask <- terra::mask(consensus_mask, city_vect)

consensus_vect <- terra::as.polygons(
  consensus_mask,
  dissolve = TRUE,
  na.rm = TRUE
)

if (nrow(consensus_vect) == 0) {
  stop("Peer traces produced no strict-majority consensus area.")
}

consensus_sf <- st_as_sf(consensus_vect)
consensus_sf <- st_make_valid(consensus_sf)
consensus_sf <- st_transform(consensus_sf, 32651)
consensus_geom <- st_union(st_geometry(consensus_sf))

consensus <- st_sf(
  reference_type = "peer_majority_consensus",
  peer_count = length(traces),
  required_votes = required_votes,
  grid_resolution_m = CONSENSUS_GRID_RESOLUTION_M,
  geometry = consensus_geom
)

consensus_gpkg <- file.path(work_dir, "observed_flood_peer_consensus.gpkg")
if (file.exists(consensus_gpkg)) file.remove(consensus_gpkg)
st_write(consensus, consensus_gpkg, layer = "observed_flood_consensus", quiet = TRUE)

consensus_summary <- data.frame(
  peer_count = length(traces),
  required_votes = required_votes,
  grid_resolution_m = CONSENSUS_GRID_RESOLUTION_M,
  consensus_area_km2 = area_m2(consensus_geom) / 1e6,
  mean_pairwise_iou = mean(pairwise$iou, na.rm = TRUE),
  minimum_pairwise_iou = min(pairwise$iou, na.rm = TRUE),
  maximum_pairwise_iou = max(pairwise$iou, na.rm = TRUE),
  stringsAsFactors = FALSE
)

write.csv(
  consensus_summary,
  file.path(output_dir, "peer_consensus_summary.csv"),
  row.names = FALSE
)

# ------------------------------------------------------------
# OPTIONAL EXPLICIT PROMOTION
# ------------------------------------------------------------

if (PROMOTE_CONSENSUS_TO_ACTIVE) {
  active_gpkg <- file.path(input_dir, "observed_flood.gpkg")
  active_geojson <- file.path(input_dir, "observed_flood.geojson")

  active <- st_sf(
    event_name = "Typhoon Ulysses (Vamco)",
    reference_source = "Independent peer majority consensus from NAMRIA-derived traces",
    derivation_method = paste0(
      "strict-majority raster consensus at ",
      CONSENSUS_GRID_RESOLUTION_M,
      " m resolution"
    ),
    peer_count = length(traces),
    geometry = consensus_geom
  )

  if (file.exists(active_gpkg)) file.remove(active_gpkg)
  if (file.exists(active_geojson)) file.remove(active_geojson)

  st_write(active, active_gpkg, layer = "observed_flood", quiet = TRUE)
  st_write(st_transform(active, 4326), active_geojson, quiet = TRUE)

  cat("\nConsensus was explicitly promoted to validation/input/observed_flood.gpkg.\n")
} else {
  cat(
    "\nConsensus built but NOT promoted to the active observed reference.\n",
    "Review peer_trace_pairwise_iou.csv and peer_consensus_summary.csv first.\n",
    "Set PROMOTE_CONSENSUS_TO_ACTIVE <- TRUE only after review.\n",
    sep = ""
  )
}

cat(
  "\nPeer consensus complete.\n",
  "Consensus GPKG: ", consensus_gpkg, "\n",
  "Required votes: ", required_votes, " of ", length(traces), "\n",
  "Consensus area: ", round(consensus_summary$consensus_area_km2, 4), " km²\n",
  "Mean pairwise IoU: ", round(consensus_summary$mean_pairwise_iou, 3), "\n",
  sep = ""
)
