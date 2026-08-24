# ==========================================
# STAGE 1: Download Map Data
# ==========================================
FROM alpine:latest AS downloader

RUN apk add --no-cache wget
WORKDIR /data
RUN wget http://download.geofabrik.de/asia/philippines-latest.osm.pbf -O map.osm.pbf

# ==========================================
# STAGE 2: Extract and Process Map Graph
# ==========================================
FROM osrm/osrm-backend:latest AS builder

WORKDIR /data
COPY --from=downloader /data/map.osm.pbf /data/map.osm.pbf

# Process the map using the car profile (generates routing graph)
RUN osrm-extract -p /opt/car.lua map.osm.pbf
RUN osrm-partition map.osrm
RUN osrm-customize map.osrm

# ==========================================
# STAGE 3: Lightweight Production Runtime
# ==========================================
FROM osrm/osrm-backend:latest AS runner

WORKDIR /data

# Copy pre-processed map data from builder stage
COPY --from=builder /data/map.osrm* /data/

EXPOSE 5000

# Start OSRM routing daemon using Multi-Level Dijkstra (MLD) engine
CMD ["osrm-routed", "--algorithm", "mld", "/data/map.osrm", "--port", "5000"]
