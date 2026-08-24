# ==========================================
# STAGE 1: Download and Clip Map Data
# ==========================================
FROM alpine:latest AS downloader

RUN apk add --no-cache wget osmium-tool
WORKDIR /data

# Download the full Philippines PBF
RUN wget http://download.geofabrik.de/asia/philippines-latest.osm.pbf -O philippines.osm.pbf

# Clip to Marikina City bounding box (W,S,E,N: 121.08,14.60,121.13,14.67)
RUN osmium extract --bbox 121.08,14.60,121.13,14.67 philippines.osm.pbf -o map.osm.pbf \
    && rm philippines.osm.pbf

# ==========================================
# STAGE 2: Extract and Process Map Graph
# ==========================================
FROM osrm/osrm-backend:latest AS builder

WORKDIR /data
COPY --from=downloader /data/map.osm.pbf /data/map.osm.pbf

# Process the map using the car profile (generates routing graph for CH)
RUN osrm-extract -p /opt/car.lua map.osm.pbf
RUN osrm-contract map.osrm

# ==========================================
# STAGE 3: Lightweight Production Runtime
# ==========================================
FROM osrm/osrm-backend:latest AS runner

WORKDIR /data

# Copy pre-processed map data from builder stage
COPY --from=builder /data/map.osrm* /data/

EXPOSE 5000

# Start OSRM routing daemon using Contraction Hierarchies (CH) engine, limited to 1 thread
CMD sh -c "osrm-routed --algorithm ch /data/map.osrm --port ${PORT:-5000} -t 1"
