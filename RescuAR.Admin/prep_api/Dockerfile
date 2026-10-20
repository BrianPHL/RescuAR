FROM rocker/geospatial:4.4.1

ENV DEBIAN_FRONTEND=noninteractive \
    LANG=C.UTF-8 \
    LC_ALL=C.UTF-8

WORKDIR /app

COPY . /app

RUN R -q -e "options(repos=c(CRAN='https://cloud.r-project.org')); pkgs <- c('plumber','jsonlite','dplyr','readr'); missing <- setdiff(pkgs, rownames(installed.packages())); if (length(missing)) install.packages(missing)"

# Railway injects PORT automatically. run_api.R reads it at runtime.
CMD ["Rscript", "run_api.R"]
