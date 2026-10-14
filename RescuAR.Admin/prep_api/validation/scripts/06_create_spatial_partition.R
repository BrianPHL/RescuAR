# RESCUAR / PREP — FROZEN ULYSSES SPATIAL PARTITION
# Run ONCE before 02_rescuar_validation_steps_3_to_5.R.
# Block assignment is determined only by coordinates + a predeclared seed,
# NEVER by observed flooding, LiPAD hazard classes, or validation scores.
# It is a retrospective within-Ulysses robustness assessment, NOT a blind
# independent event test: Ulysses already informed scenario comparisons.

suppressPackageStartupMessages(library(sf))

BLOCK_SIZE_M <- 1000
HOLDOUT_FRACTION <- 0.20
PARTITION_SEED <- 20261014
ALLOW_REPLACEMENT <- FALSE    # intentional manual gate; never silently resplit

find_root <- function(start = getwd()) {
  current <- normalizePath(start, winslash = '/', mustWork = TRUE)
  repeat {
    if (file.exists(file.path(current, 'prep_api', 'data', 'spatial', 'baseline_spatial.gpkg'))) return(current)
    if (file.exists(file.path(current, 'data', 'spatial', 'baseline_spatial.gpkg'))) return(dirname(current))
    parent <- dirname(current)
    if (identical(parent, current)) stop('Cannot locate RescuAR.Admin project root.')
    current <- parent
  }
}

root <- find_root()
base <- file.path(root, 'prep_api')
spatial_file <- file.path(base, 'data', 'spatial', 'baseline_spatial.gpkg')
in_dir <- file.path(base, 'validation', 'input')
out_dir <- file.path(base, 'validation', 'partitions')
dir.create(out_dir, recursive = TRUE, showWarnings = FALSE)
out_gpkg <- file.path(out_dir, 'ulysses_spatial_partition.gpkg')
out_manifest <- file.path(out_dir, 'ulysses_spatial_partition_manifest.csv')
if ((file.exists(out_gpkg) || file.exists(out_manifest)) && !ALLOW_REPLACEMENT) {
  stop('Frozen partition exists. Do not regenerate to chase a better score. Review existing partition and manifest.')
}

b <- st_read(spatial_file, layer = 'barangays', quiet = TRUE) |>
  st_make_valid() |> st_transform(32651)
city <- st_make_valid(st_union(st_geometry(b)))
mask_candidates <- c(file.path(in_dir, 'observation_domain.gpkg'), file.path(in_dir, 'observation_domain.geojson'))
mask_path <- mask_candidates[file.exists(mask_candidates)][1]
if (length(mask_path) && !is.na(mask_path)) {
  if (grepl('\\.gpkg$', mask_path, ignore.case = TRUE)) {
    layers <- st_layers(mask_path)$name
    lyr <- if ('observation_domain' %in% layers) 'observation_domain' else layers[1]
    mask <- st_read(mask_path, layer = lyr, quiet = TRUE)
  } else mask <- st_read(mask_path, quiet = TRUE)
  if (nrow(mask) == 0 || is.na(st_crs(mask))) stop('Invalid observation-domain mask.')
  domain <- suppressWarnings(st_intersection(city, st_union(st_geometry(st_transform(st_make_valid(mask), 32651)))))
  domain_source <- basename(mask_path)
} else {
  domain <- city
  domain_source <- 'full_city_UNCONFIRMED_SURVEY_COVERAGE'
  warning('No independent observation-domain mask: outside mapped flooding is NOT necessarily confirmed dry.')
}
domain <- st_make_valid(domain)
if (as.numeric(st_area(domain)) <= 0) stop('Empty analysis domain.')

# Grid origin aligned to UTM kilometres, rather than depending on current bbox.
bb <- st_bbox(domain)
ox <- floor(as.numeric(bb['xmin']) / BLOCK_SIZE_M) * BLOCK_SIZE_M
oy <- floor(as.numeric(bb['ymin']) / BLOCK_SIZE_M) * BLOCK_SIZE_M
square_grid <- st_make_grid(st_sf(geometry = domain), cellsize = BLOCK_SIZE_M,
                            offset = c(ox, oy), square = TRUE)
tiles <- st_sf(geometry = square_grid)
# IDs of full tiles are assigned BEFORE clipping; deterministic and auditable.
centers <- st_coordinates(st_centroid(square_grid))
tiles$grid_col <- as.integer(round((centers[,1] - ox) / BLOCK_SIZE_M - 0.5))
tiles$grid_row <- as.integer(round((centers[,2] - oy) / BLOCK_SIZE_M - 0.5))
tiles$block_id <- sprintf('UTM_%d_%d', tiles$grid_col, tiles$grid_row)
tiles <- suppressWarnings(st_intersection(tiles, st_sf(geometry = domain)))
tiles <- st_collection_extract(st_make_valid(tiles), 'POLYGON', warn = FALSE)
tiles$area_m2 <- as.numeric(st_area(tiles))
tiles <- tiles[tiles$area_m2 > 0.01, ]
tiles <- tiles[order(tiles$block_id), ]
if (!nrow(tiles)) stop('No spatial blocks created.')

# Deterministic coordinate-based ranking. Mirrors the frozen supplied example.
tiles$priority <- (as.numeric(tiles$grid_col) * 73856093 +
                   as.numeric(tiles$grid_row) * 19349663 + PARTITION_SEED) %% 104729
ord <- order(tiles$priority, tiles$block_id)
target_m2 <- as.numeric(st_area(domain)) * HOLDOUT_FRACTION
selected <- rep(FALSE, nrow(tiles))
covered_m2 <- 0
for (k in ord) {
  candidate_m2 <- covered_m2 + tiles$area_m2[k]
  if (abs(target_m2 - candidate_m2) < abs(target_m2 - covered_m2)) {
    selected[k] <- TRUE
    covered_m2 <- candidate_m2
  }
}
if (!any(selected) || all(selected)) stop('Invalid partition allocation.')
tiles$subset <- ifelse(selected, 'holdout', 'development')
tiles$area_km2 <- tiles$area_m2 / 1e6
tiles$priority <- NULL
tiles$area_m2 <- NULL

# No gaps, no positive-area overlaps. This is essential for a valid matrix.
merged <- st_union(st_geometry(tiles))
coverage_difference <- as.numeric(st_area(st_sym_difference(merged, domain)))
if (!is.finite(coverage_difference) || coverage_difference > 10) {
  stop('Spatial partition does not cover analysis domain exactly: discrepancy=', coverage_difference, ' m2')
}
st_write(tiles, out_gpkg, layer = 'spatial_partition', quiet = TRUE,
         delete_dsn = ALLOW_REPLACEMENT)
summary <- aggregate(area_km2 ~ subset, data = st_drop_geometry(tiles), sum)
manifest <- data.frame(
  event_id = 'ulysses_2020',
  method = 'fixed_1000m_UTM_grid_coordinate_priority_v1',
  block_size_m = BLOCK_SIZE_M,
  partition_seed = PARTITION_SEED,
  target_holdout_fraction = HOLDOUT_FRACTION,
  achieved_holdout_fraction = sum(tiles$area_km2[tiles$subset == 'holdout']) / sum(tiles$area_km2),
  analysis_domain_area_km2 = as.numeric(st_area(domain)) / 1e6,
  observation_domain_source = domain_source,
  baseline_spatial_md5 = unname(tools::md5sum(spatial_file)),
  observation_domain_md5 = if (length(mask_path) && !is.na(mask_path)) unname(tools::md5sum(mask_path)) else NA_character_,
  partition_gpkg_md5 = unname(tools::md5sum(out_gpkg)),
  prior_Ulysses_comparisons = TRUE,
  independent_blind_holdout = FALSE,
  notes = 'Frozen retrospective spatial partition; do not use holdout metrics to select a scenario; no event-level independence',
  stringsAsFactors = FALSE
)
write.csv(manifest, out_manifest, row.names = FALSE)
print(summary)
cat('Spatial partition saved:', out_gpkg, '\nManifest:', out_manifest, '\n')
