ARG OSRM_IMAGE=ghcr.io/project-osrm/osrm-backend:v5.27.1

FROM debian:bookworm-slim AS merger

RUN apt-get update \
    && apt-get install -y --no-install-recommends osmium-tool python3 lua5.2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /data
COPY routing-inputs/ /input/
COPY evacuation-policy.json /policy/evacuation-policy.json
COPY profiles/ /policy/profiles/
RUN python3 /policy/profiles/build-evacuation-policy.py /policy/evacuation-policy.json /policy/profiles \
        --cases /policy/profiles/evacuation-policy-cases.json \
    && cd /policy/profiles \
    && luac5.2 -p evacuation-foot.lua evacuation-access.lua \
    && lua5.2 test-evacuation-policy.lua

# Each extract keeps its original OSM IDs and complete ways. Disconnected
# regions do not require roads between them.
# The walking profile does not use route-group relations. Exclude those
# before checking versions; keep all nodes, ways, and other relation types.
# Overlapping extracts can come from different dates. Merge their history,
# then select the latest available version of each object for routing.
RUN set -eu; \
    mkdir /filtered /sorted; \
    for file in /input/*.osm.pbf; do \
        if [ ! -f "$file" ]; then \
            echo "Add original regional .osm.pbf extracts to routing-inputs/." >&2; \
            exit 1; \
        fi; \
        osmium tags-filter "$file" -i -R \
            'r/type=route,route_master' \
            -o "/filtered/$(basename "$file")"; \
        osmium sort "/filtered/$(basename "$file")" -o "/sorted/$(basename "$file")"; \
    done; \
    osmium merge --with-history /sorted/*.osm.pbf -o /data/merged.osh.pbf; \
    osmium time-filter /data/merged.osh.pbf -o /data/map.osm.pbf; \
    if [ "$(osmium fileinfo -e -g data.multiple_versions /data/map.osm.pbf)" != "no" ]; then \
        echo "OSM version normalization failed: multiple object versions remain." >&2; \
        exit 1; \
    fi; \
    osmium check-refs /data/map.osm.pbf

FROM ${OSRM_IMAGE} AS builder

WORKDIR /data
COPY --from=merger /data/map.osm.pbf /data/map.osm.pbf
COPY profiles/evacuation-foot.lua /opt/evacuation-foot.lua
COPY profiles/evacuation-access.lua /opt/evacuation-access.lua
COPY --from=merger /policy/profiles/evacuation-policy.lua /opt/evacuation-policy.lua
ENV LUA_PATH="/opt/?.lua;/opt/?/init.lua;;"

RUN osrm-extract -p /opt/evacuation-foot.lua --small-component-size 1 /data/map.osm.pbf \
    && osrm-partition /data/map.osrm \
    && osrm-customize /data/map.osrm

FROM ${OSRM_IMAGE} AS runner

WORKDIR /data
COPY --from=builder /data/map.osrm* /data/

EXPOSE 5000

CMD ["sh", "-c", "exec osrm-routed --algorithm mld /data/map.osrm --port ${PORT:-5000} -t 1"]
