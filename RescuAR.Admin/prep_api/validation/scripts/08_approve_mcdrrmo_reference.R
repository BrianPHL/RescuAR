# RESCUAR / PREP: Controlled registration of INDEPENDENTLY VERIFIED
# MCDRRMO historical observed flood geometry. No LiPAD/model prediction
# layers are accessed. Never create estimated ground truth from this script.
#
# Input per event folder:
#   candidate_observed_flood.gpkg       layer: observed_flood
#   candidate_observation_domain.gpkg   layer: observation_domain
#   registration_checkpoints.csv        independent check points (image method)
#   qc_review.json                      signed verification
#
# Run: Sys.setenv(PREP_EVENT_ID="mcdrrmo_carina_2024");
#      source("08_approve_mcdrrmo_reference.R")
# All promotions are explicitly confirmed; registry backed up first.
suppressPackageStartupMessages({
  library(sf)
  library(readr)
  library(jsonlite)
})
EVENT_ID <- trimws(Sys.getenv("PREP_EVENT_ID",""))
if (!grepl("^mcdrrmo_[a-z0-9_]+$",EVENT_ID)) {
  stop("Set PREP_EVENT_ID to one of the registered mcdrrmo_* IDs before sourcing.")
}
MAX_CHECKPOINT_RMSE_M <- 50
MAX_CHECKPOINT_ERROR_M <- 80
MIN_INDEPENDENT_CHECKPOINTS <- 6
MAX_OUTSIDE_DOMAIN_M2 <- 1  # numeric sliver tolerance only

find_project_root <- function(start=getwd()) {
  p <- normalizePath(start,winslash="/",mustWork=TRUE)
  repeat {
    if (file.exists(file.path(p,"prep_api","data","spatial","baseline_spatial.gpkg"))) return(p)
    np <- dirname(p)
    if(identical(np,p)) stop("Cannot locate RescuAR.Admin root")
    p <- np
  }
}
root <- find_project_root()
preproot <- file.path(root,"prep_api")
validation <- file.path(preproot,"validation")
folder <- file.path(validation,"events",EVENT_ID)
eventsfile <- file.path(validation,"events","events.csv")
if(!dir.exists(folder)) stop("Unknown event directory: ",folder)
events <- read_csv(eventsfile,show_col_types=FALSE,progress=FALSE,na=character())
idx <- which(events$event_id == EVENT_ID)
if (length(idx)!=1) stop("Event ID missing or duplicated in events.csv")
if (events$role[idx] != "reference_only") stop("Event role must remain reference_only for this intake")
if (events$event_status[idx] == "ready") stop("Event already approved; no implicit overwrite allowed")
if (nzchar(events$observed_flood_path[idx])) stop("Observed geometry path already registered; investigate manually")

review_file <- file.path(folder,"qc_review.json")
if(!file.exists(review_file)) stop("Missing qc_review.json; consult intake templates")
qc <- fromJSON(review_file)
if (!identical(as.character(qc$event_id), EVENT_ID)) stop("QC event ID mismatch")
required_true <- c("source_is_observed_flood","reference_independent_of_lipad",
 "coverage_verified","trace_reviewed","reviewer_approved")
for (field in required_true) {
  if(!isTRUE(qc[[field]])) stop("QC has not approved ",field,". Rejecting event.")
}
if (is.null(qc$reviewer_name) || !nzchar(trimws(qc$reviewer_name)) ||
    is.null(qc$source_method) || !qc$source_method %in% c("manual_georeferencing","native_gis")) {
  stop("QC requires reviewer_name and source_method=manual_georeferencing/native_gis")
}
if (is.null(qc$observation_definition) || !nzchar(trimws(qc$observation_definition))) {
  stop("State which areas were actually observed and how confirmed dry areas were assessed")
}
if (is.null(qc$evidence_provenance) || !nzchar(trimws(qc$evidence_provenance))) {
  stop("Missing verification provenance for observed (not merely predicted) extent")
}

checkpoint_rmse_m <- NA_real_
checkpoint_max_m <- NA_real_
checkpoint_count <- 0L
if(qc$source_method == "manual_georeferencing") {
  cpath <- file.path(folder,"registration_checkpoints.csv")
  if(!file.exists(cpath)) stop("Missing independent registration_checkpoints.csv")
  cp <- read_csv(cpath,show_col_types=FALSE,progress=FALSE)
  required <- c("check_id","reference_x_utm","reference_y_utm","registered_x_utm","registered_y_utm")
  if(!all(required %in% names(cp))) stop("Checkpoint fields missing: ",paste(setdiff(required,names(cp)),collapse=", "))
  cp <- cp[,required]
  if(anyDuplicated(cp$check_id)) stop("Duplicate checkpoint identifiers")
  checkpoint_count <- nrow(cp)
  if(checkpoint_count < MIN_INDEPENDENT_CHECKPOINTS) stop("Need at least ",MIN_INDEPENDENT_CHECKPOINTS," withheld independent checkpoints")
  nums <- as.matrix(cp[,required[-1]])
  if(!all(is.finite(nums))) stop("All checkpoint coordinates must be finite")
  error <- sqrt((cp$reference_x_utm-cp$registered_x_utm)^2+
                (cp$reference_y_utm-cp$registered_y_utm)^2)
  checkpoint_rmse_m <- sqrt(mean(error^2))
  checkpoint_max_m <- max(error)
  if(checkpoint_rmse_m>MAX_CHECKPOINT_RMSE_M || checkpoint_max_m>MAX_CHECKPOINT_ERROR_M) {
    stop("Registration QC failed: RMSE=",round(checkpoint_rmse_m,2),
         "m / max=",round(checkpoint_max_m,2),"m. Improve registration, not reference extent")
  }
} else {
  if (!isTRUE(qc$native_crs_verified)) stop("Native GIS source CRS must be verified")
}

read_checked <- function(path,layer) {
  if(!file.exists(path)) stop("Missing candidate GPKG: ",path)
  ly <- st_layers(path)$name
  if (!layer %in% ly) stop("Required layer '",layer,"' missing in ",path)
  a <- st_read(path,layer=layer,quiet=TRUE)
  if(nrow(a)==0 || is.na(st_crs(a))) stop("Empty geometry or missing CRS: ",path)
  if(any(st_is_empty(a))) stop("Empty feature(s) found in ",path)
  if(!all(st_is_valid(a))) stop("Invalid feature(s); fix in QGIS and document correction: ",path)
  if(!all(as.character(st_geometry_type(a)) %in% c("POLYGON","MULTIPOLYGON"))) stop("Expected polygons: ",path)
  st_transform(a,32651)
}
observed <- read_checked(file.path(folder,"candidate_observed_flood.gpkg"),"observed_flood")
domain <- read_checked(file.path(folder,"candidate_observation_domain.gpkg"),"observation_domain")
obsgeom <- st_union(st_geometry(observed))
domaingeom <- st_union(st_geometry(domain))
obs_area <- as.numeric(st_area(obsgeom))
domain_area <- as.numeric(st_area(domaingeom))
if(!is.finite(obs_area) || obs_area<=0 || !is.finite(domain_area) || domain_area<=0) stop("Zero or invalid polygon area")
outside <- suppressWarnings(st_difference(obsgeom,domaingeom))
outside_area <- if(all(st_is_empty(outside))) 0 else as.numeric(st_area(outside))
if(outside_area>MAX_OUTSIDE_DOMAIN_M2) stop("Observed area outside verified observation domain: ",round(outside_area),"m2")

base <- st_read(file.path(preproot,"data","spatial","baseline_spatial.gpkg"),layer="barangays",quiet=TRUE)
city <- st_union(st_geometry(st_transform(base,32651)))
city_outside <- suppressWarnings(st_difference(domaingeom,city))
city_outside_m2 <- if(all(st_is_empty(city_outside))) 0 else as.numeric(st_area(city_outside))
if(city_outside_m2>MAX_OUTSIDE_DOMAIN_M2) stop("Verified observation domain extends outside Marikina boundary: ",round(city_outside_m2),"m2")

cat("Event: ",EVENT_ID,"\n",sep="")
cat("Observed area: ",round(obs_area/1e6,3)," km2\n",sep="")
cat("Verified observation domain: ",round(domain_area/1e6,3)," km2\n",sep="")
if(qc$source_method=="manual_georeferencing") cat("Independent checkpoint RMSE: ",round(checkpoint_rmse_m,2)," m; max: ",round(checkpoint_max_m,2)," m\n",sep="")
answer <- trimws(readline("Review independent reference and enter exactly APPROVE to register (or press ENTER to cancel): "))
if(!identical(answer,"APPROVE")) stop("Approval cancelled; no datasets or registry changed")

# Store verified, unmodified candidate geometry as a separate reference copy.
# Do not modify the original candidate or hazard inputs.
obsout <- file.path(folder,"observed_flood.gpkg")
domout <- file.path(folder,"observation_domain.gpkg")
certfile <- file.path(folder,"approved_reference_qc.json")
if(any(file.exists(c(obsout,domout,certfile)))) stop("Approved output already exists. Refusing overwrite")
st_write(observed,obsout,layer="observed_flood",quiet=TRUE)
st_write(domain,domout,layer="observation_domain",quiet=TRUE)
certificate <- list(
  event_id=EVENT_ID,accepted=TRUE,
  source_method=qc$source_method,
  reviewer_name=qc$reviewer_name,
  observation_definition=qc$observation_definition,
  evidence_provenance=qc$evidence_provenance,
  independent_checkpoint_count=checkpoint_count,
  checkpoint_rmse_m=checkpoint_rmse_m,
  checkpoint_max_m=checkpoint_max_m,
  observed_km2=obs_area/1e6,observation_domain_km2=domain_area/1e6,
  approved_at_utc=format(Sys.time(),"%Y-%m-%dT%H:%M:%SZ",tz="UTC")
)
# Write QC certificate before registering new paths.
write_json(certificate,certfile,pretty=TRUE,auto_unbox=TRUE,na="null")
backup <- paste0(eventsfile,".pre_mcdrrmo_approval_",format(Sys.time(),"%Y%m%d_%H%M%S"),".bak")
if(!file.copy(eventsfile,backup)) stop("Cannot create registry backup; do not proceed")
events$observed_flood_path[idx] <- paste0("validation/events/",EVENT_ID,"/observed_flood.gpkg")
events$observation_domain_path[idx] <- paste0("validation/events/",EVENT_ID,"/observation_domain.gpkg")
events$event_status[idx] <- "ready"
events$role[idx] <- "reference_only"
events$calibration_eligible[idx] <- FALSE
write_csv(events,eventsfile,na="")
cat("Approved reference registered; event stays reference_only.\n")
cat("Registry backup: ",backup,"\n",sep="")
cat("Run 04_calibrate_scenario_rules.R for DESCRIPTIVE scenario comparison only.\n")
cat("Do not promote calibration rules until a separate calibration/test protocol exists.\n")
