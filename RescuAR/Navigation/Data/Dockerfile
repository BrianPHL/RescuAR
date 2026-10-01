ARG OSRM_IMAGE=osrm/osrm-backend:latest

FROM debian:bookworm-slim AS merger

RUN apt-get update \
    && apt-get install -y --no-install-recommends osmium-tool \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /data
COPY routing-inputs/ /input/

# Each extract keeps its original OSM IDs and complete ways. Disconnected
# regions do not require roads between them.
RUN set -eu; \
    mkdir /sorted; \
    for file in /input/*.osm.pbf; do \
        if [ ! -f "$file" ]; then \
            echo "Add original regional .osm.pbf extracts to routing-inputs/." >&2; \
            exit 1; \
        fi; \
        osmium sort "$file" -o "/sorted/$(basename "$file")"; \
    done; \
    osmium merge /sorted/*.osm.pbf -o /data/map.osm.pbf; \
    if [ "$(osmium fileinfo -e -g data.multiple_versions /data/map.osm.pbf)" != "no" ]; then \
        echo "Conflicting OSM versions: use extracts from the same source snapshot." >&2; \
        exit 1; \
    fi; \
    osmium check-refs /data/map.osm.pbf

FROM ${OSRM_IMAGE} AS builder

WORKDIR /data
COPY --from=merger /data/map.osm.pbf /data/map.osm.pbf

RUN osrm-extract -p /opt/foot.lua /data/map.osm.pbf \
    && osrm-partition /data/map.osrm \
    && osrm-customize /data/map.osrm

FROM ${OSRM_IMAGE} AS runner

WORKDIR /data
COPY --from=builder /data/map.osrm* /data/

EXPOSE 5000

CMD ["sh", "-c", "exec osrm-routed --algorithm mld /data/map.osrm --port ${PORT:-5000} -t 1"]
