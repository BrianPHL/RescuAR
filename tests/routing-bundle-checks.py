"""Focused source/export consistency checks; no external tools or network."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


exporter = module("bundle_exporter", ROOT / "build/Export-RoutingBundle.py")
validator = module("bundle_validator", ROOT / "RescuAR/Navigation/Data/profiles/validate-routing-input.py")
SOURCE = '''<osm version="0.6">
<node id="1" lat="14" lon="121" version="2"><tag k="barrier" v="gate"/><tag k="locked" v="yes"/><tag k="foot" v="no"/></node>
<node id="2" lat="14" lon="121.001"/>
<node id="3" lat="14.001" lon="121"><tag k="access" v="no"/></node>
<way id="9" version="4"><nd ref="1"/><nd ref="2"/><tag k="highway" v="footway"/><tag k="oneway:foot" v="-1"/><tag k="foot:forward" v="no"/><tag k="layer" v="1"/></way>
</osm>'''


class RoutingBundleChecks(unittest.TestCase):
    def test_restrictions_geometry_and_source_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / "source.osm").write_text(SOURCE, encoding="utf-8")
            self.assertEqual(exporter.export_xml(path / "source.osm", path), {"ROADS": 1, "POINTS": 2})
            road = json.loads((path / "CENTRAL_ROADS.geojson").read_text())["features"][0]
            self.assertEqual(road["geometry"]["coordinates"], [[121.0, 14.0], [121.001, 14.0]])
            self.assertEqual(road["properties"]["osm_node_ids"], ["1", "2"])
            self.assertEqual(road["properties"]["oneway:foot"], "-1")
            self.assertEqual(road["properties"]["foot:forward"], "no")
            points = json.loads((path / "CENTRAL_POINTS.geojson").read_text())["features"]
            self.assertEqual([p["properties"]["osm_id"] for p in points], ["1", "3"])
            self.assertEqual(points[0]["properties"]["locked"], "yes")
            self.assertEqual(points[1]["properties"]["access"], "no")

    def test_incomplete_and_conflicting_sources_rejected(self):
        for invalid in (SOURCE.replace('ref="2"', 'ref="404"'),
                        SOURCE.replace('</osm>', '<node id="1" lat="14" lon="121"/></osm>'),
                        SOURCE.replace('version="2"', 'visible="false"')):
            with self.subTest(source=invalid), tempfile.TemporaryDirectory() as directory:
                path = Path(directory)
                (path / "source.osm").write_text(invalid, encoding="utf-8")
                with self.assertRaises(ValueError):
                    exporter.export_xml(path / "source.osm", path)
                self.assertFalse((path / "CENTRAL_ROADS.geojson").exists())

    def test_app_and_server_reject_mismatched_bundles(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            policy = path / "policy.json"
            policy.write_text('{"version":1}', encoding="utf-8")
            for name in ("central.osm.pbf", "CENTRAL_ROADS.geojson", "CENTRAL_POINTS.geojson"):
                (path / name).write_bytes(name.encode("ascii"))
            files = {name: exporter.sha256(path / name) for name in
                     ("central.osm.pbf", "CENTRAL_ROADS.geojson", "CENTRAL_POINTS.geojson")}
            policy_hash = exporter.sha256(policy)
            manifest = {"schemaVersion": 1, "files": files, "policySha256": policy_hash,
                "datasetId": "central-" + hashlib.sha256((files["central.osm.pbf"] + policy_hash).encode("ascii")).hexdigest()}
            (path / "routing-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            self.assertEqual(exporter.verify_bundle(path, policy)["datasetId"], validator.validate(path, policy)["datasetId"])
            (path / "CENTRAL_POINTS.geojson").write_bytes(b"wrong region")
            with self.assertRaises(ValueError):
                exporter.verify_bundle(path, policy)
            (path / "central.osm.pbf").write_bytes(b"other snapshot")
            with self.assertRaises(ValueError):
                validator.validate(path, policy)
            policy.write_bytes(b"changed policy")
            with self.assertRaises(ValueError):
                exporter.verify_bundle(path, policy)


if __name__ == "__main__":
    unittest.main()
