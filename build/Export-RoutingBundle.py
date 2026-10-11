"""Export one reference-complete OSM snapshot for both routing engines.

Requires Python 3 and osmium-tool. Writes a new staging directory; activation
is a separate copy after reviewing coverage and facility reachability.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

SCHEMA_VERSION = 1


def sha256(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def command(osmium, *args):
    return subprocess.run([osmium, *map(str, args)], check=True,
                          capture_output=True, text=True).stdout.strip()


def export_xml(source, output):
    """Keep every tagged node and highway way, including restrictions and IDs."""
    nodes, points, roads = {}, [], []
    seen = {"node": set(), "way": set(), "relation": set()}
    context = ET.iterparse(source, events=("start", "end"))
    _, root = next(context)
    for event, element in context:
        kind = element.tag
        if event != "end" or kind not in seen:
            continue
        identifier = element.attrib["id"]
        if identifier in seen[kind] or element.get("visible") == "false":
            raise ValueError(f"Duplicate/deleted {kind} {identifier}: use one current snapshot")
        seen[kind].add(identifier)
        tags = {child.attrib["k"]: child.attrib["v"] for child in element.findall("tag")}
        properties = dict(tags)
        properties["osm_id"] = identifier
        properties["osm_version"] = element.get("version", "")
        properties["osm_timestamp"] = element.get("timestamp", "")
        if kind == "node":
            coordinate = [float(element.attrib["lon"]), float(element.attrib["lat"])]
            if not all(math.isfinite(v) for v in coordinate) or not (-180 <= coordinate[0] <= 180 and -90 <= coordinate[1] <= 90):
                raise ValueError(f"Invalid node coordinates: {identifier}")
            nodes[identifier] = coordinate
            if tags:
                points.append({"type": "Feature", "properties": properties,
                               "geometry": {"type": "Point", "coordinates": coordinate}})
        elif kind == "way":
            references = [child.attrib["ref"] for child in element.findall("nd")]
            if any(ref not in nodes for ref in references):
                raise ValueError(f"Incomplete or unordered way {identifier}: missing referenced node")
            if tags.get("highway"):
                if len(references) < 2:
                    raise ValueError(f"Highway way {identifier} has fewer than two nodes")
                properties["osm_node_ids"] = references
                roads.append({"type": "Feature", "properties": properties,
                              "geometry": {"type": "LineString", "coordinates": [nodes[ref] for ref in references]}})
        root.clear()
    if not roads:
        raise ValueError("Source has no highway geometry")
    counts = {}
    for kind, features in (("ROADS", roads), ("POINTS", points)):
        filename = f"CENTRAL_{kind}.geojson"
        (output / filename).write_text(json.dumps({"type": "FeatureCollection", "features": features},
            ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
        counts[kind] = len(features)
    return counts


def verify_bundle(directory, policy):
    manifest = json.loads((directory / "routing-manifest.json").read_text(encoding="utf-8"))
    if manifest["schemaVersion"] != SCHEMA_VERSION or manifest["policySha256"] != sha256(policy):
        raise ValueError("Bundle schema/policy mismatch; regenerate both app and MLD outputs")
    for name in ("central.osm.pbf", "CENTRAL_ROADS.geojson", "CENTRAL_POINTS.geojson"):
        if sha256(directory / name) != manifest["files"][name]:
            raise ValueError(f"Bundle hash mismatch: {name}")
    expected = "central-" + hashlib.sha256((manifest["files"]["central.osm.pbf"] +
        manifest["policySha256"]).encode("ascii")).hexdigest()
    if manifest["datasetId"] != expected:
        raise ValueError("Bundle identity mismatch")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, help="Single coherent .osm.pbf snapshot")
    parser.add_argument("--output", type=Path, required=True, help="New staging directory")
    parser.add_argument("--bounds", help="Buffered west,south,east,north; complete ways are retained")
    parser.add_argument("--source-url", help="Original source download URL/provenance")
    parser.add_argument("--snapshot", help="UTC snapshot time if absent from the source header")
    parser.add_argument("--osmium", default="osmium")
    parser.add_argument("--policy", type=Path, default=Path(__file__).resolve().parents[1] /
                        "RescuAR/Navigation/Data/evacuation-policy.json")
    parser.add_argument("--verify", action="store_true", help="Verify an existing complete bundle only")
    args = parser.parse_args()
    if args.verify:
        print(verify_bundle(args.output, args.policy)["datasetId"])
        return
    if args.source is None or not args.source_url:
        parser.error("--source and --source-url are required for export")
    source = args.source.resolve(strict=True)
    if args.output.exists():
        parser.error("Output already exists; use a new directory so active data cannot be overwritten")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    # A temporary sibling is published only after every consistency check passes.
    with tempfile.TemporaryDirectory(prefix="routing-bundle-", dir=args.output.parent) as work:
        staging = Path(work) / "bundle"
        staging.mkdir()
        if command(args.osmium, "fileinfo", "-e", "-g", "data.multiple_versions", source) != "no":
            raise ValueError("Source contains multiple/unknown object versions; use one current snapshot")
        snapshot = command(args.osmium, "fileinfo", "-g", "header.option.osmosis_replication_timestamp", source)
        snapshot = snapshot or args.snapshot
        if not snapshot:
            raise ValueError("Source has no replication timestamp; provide its documented --snapshot UTC time")
        central = staging / "central.osm.pbf"
        if args.bounds:
            bounds = [float(value) for value in args.bounds.split(",")]
            if len(bounds) != 4 or not all(math.isfinite(v) for v in bounds) or not (
                    -180 <= bounds[0] < bounds[2] <= 180 and -90 <= bounds[1] < bounds[3] <= 90):
                raise ValueError("Invalid west,south,east,north bounds")
            command(args.osmium, "extract", "--bbox", args.bounds, "--strategy", "complete_ways",
                    "--output-header", "osmosis_replication_timestamp=" + snapshot, "-o", central, source)
        else:
            shutil.copyfile(source, central)
        command(args.osmium, "check-refs", central)
        xml = Path(work) / "source.osm"
        command(args.osmium, "cat", central, "-f", "osm", "-o", xml)
        counts = export_xml(xml, staging)
        files = {name: sha256(staging / name) for name in
                 ("central.osm.pbf", "CENTRAL_ROADS.geojson", "CENTRAL_POINTS.geojson")}
        policy_hash = sha256(args.policy)
        manifest = {"schemaVersion": SCHEMA_VERSION,
            "datasetId": "central-" + hashlib.sha256((files["central.osm.pbf"] + policy_hash).encode("ascii")).hexdigest(),
            "sourceUrl": args.source_url, "snapshotUtc": snapshot,
            "sourceSha256": sha256(source), "requestedBounds": args.bounds,
            "actualBounds": command(args.osmium, "fileinfo", "-e", "-g", "data.bbox", central),
            "exporter": "Export-RoutingBundle.py/v1", "exporterSha256": sha256(__file__),
            "osmiumVersion": command(args.osmium, "--version"),
            "policySha256": policy_hash, "files": files, "featureCounts": counts,
            "license": "OpenStreetMap contributors; ODbL 1.0"}
        (staging / "routing-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        verify_bundle(staging, args.policy)
        staging.rename(args.output)
    print(json.dumps({"datasetId": manifest["datasetId"], "featureCounts": counts, "output": str(args.output)}))


if __name__ == "__main__":
    main()
