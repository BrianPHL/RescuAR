if (!requireNamespace("plumber", quietly = TRUE)) {
  stop("Run source('install_packages.R') first.")
}
api <- plumber::plumb("plumber.R")
api$run(host = "0.0.0.0", port = as.integer(Sys.getenv("PORT", "8000")))
