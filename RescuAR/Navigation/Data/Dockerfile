FROM osrm/osrm-backend:latest AS builder

WORKDIR /data

COPY marikina-routing.osm.pbf /data/map.osm.pbf

RUN osrm-extract -p /opt/foot.lua /data/map.osm.pbf
RUN osrm-partition /data/map.osrm
RUN osrm-customize /data/map.osrm


FROM osrm/osrm-backend:latest AS runner

WORKDIR /data

COPY --from=builder /data/map.osrm* /data/

EXPOSE 5000

CMD sh -c "osrm-routed --algorithm mld /data/map.osrm --port ${PORT:-5000} -t 1"