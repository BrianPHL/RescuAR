"""Reject a central OSRM input that differs from its app bundle or policy."""
import hashlib
import json
from pathlib import Path
import sys


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate(directory, policy):
    central = directory / "central.osm.pbf"
    manifest_path = directory / "routing-manifest.json"
    if not central.exists():
        if manifest_path.exists():
            raise ValueError("Routing manifest present without central.osm.pbf")
        return None
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    policy_hash, source_hash = sha256(policy), sha256(central)
    expected_id = "central-" + hashlib.sha256((source_hash + policy_hash).encode("ascii")).hexdigest()
    if (manifest["schemaVersion"] != 1 or manifest["policySha256"] != policy_hash or
            manifest["files"]["central.osm.pbf"] != source_hash or manifest["datasetId"] != expected_id):
        raise ValueError("Central input/manifest/policy mismatch; regenerate app and server from the same snapshot")
    for name in ("CENTRAL_ROADS.geojson", "CENTRAL_POINTS.geojson"):
        if len(manifest["files"][name]) != 64:
            raise ValueError("Missing app output hash: " + name)
    print("MLD routing dataset: " + manifest["datasetId"])
    return manifest


if __name__ == "__main__":
    validate(Path(sys.argv[1]), Path(sys.argv[2]))
