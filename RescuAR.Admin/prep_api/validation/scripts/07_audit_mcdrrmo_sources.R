# RESCUAR / PREP: MCDRRMO source-only audit.
# This script NEVER promotes events or generates flood geometry.
suppressPackageStartupMessages({
  library(readr)
})
find_project_root <- function(start=getwd()) {
  p <- normalizePath(start,winslash="/",mustWork=TRUE)
  repeat {
    if(file.exists(file.path(p,"prep_api","data","spatial","baseline_spatial.gpkg"))) return(p)
    np <- dirname(p)
    if(identical(np,p)) stop("Could not locate RescuAR.Admin root")
    p <- np
  }
}
root <- find_project_root()
preproot <- file.path(root,"prep_api")
validation <- file.path(preproot,"validation")
registry <- read_csv(file.path(validation,"events","events.csv"),show_col_types=FALSE,progress=FALSE)
rows <- registry[startsWith(registry$event_id,"mcdrrmo_"),,drop=FALSE]
if (nrow(rows) != 5) warning("Expected 5 distinct MCDRRMO source events, found ",nrow(rows))
get_source_path <- function(p) {
  if(is.na(p) || !nzchar(trimws(p))) return(NA_character_)
  file.path(preproot,p)
}
report <- data.frame(
  event_id=rows$event_id,
  event_date=rows$event_date,
  role=rows$role,
  event_status=rows$event_status,
  source_file_exists=vapply(rows$source_map_path,function(p) file.exists(get_source_path(p)),logical(1)),
  observed_geometry_registered=(!is.na(rows$observed_flood_path) & nzchar(trimws(rows$observed_flood_path))),
  observation_domain_registered=(!is.na(rows$observation_domain_path) & nzchar(trimws(rows$observation_domain_path))),
  approved_qc_sidecar_exists=vapply(rows$event_id,function(id) file.exists(file.path(validation,"events",id,"approved_reference_qc.json")),logical(1)),
  stringsAsFactors=FALSE
)
report$ready_for_quantitative_evaluation <- report$source_file_exists &
  report$observed_geometry_registered & report$observation_domain_registered &
  report$approved_qc_sidecar_exists & report$event_status=="ready"
stopifnot(!anyDuplicated(report$event_id))
outdir <- file.path(validation,"output")
dir.create(outdir,recursive=TRUE,showWarnings=FALSE)
outfile <- file.path(outdir,"mcdrrmo_event_readiness.csv")
write.csv(report,outfile,row.names=FALSE,na="")
print(report,row.names=FALSE)
cat("\nSaved source readiness report: ",outfile,"\n",sep="")
cat("Pending sources remain reference_only; do not include them in F1/WAPE reports.\n")
