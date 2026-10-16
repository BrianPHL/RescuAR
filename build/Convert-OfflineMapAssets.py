"""Convert the supplied custom MAUI raster map to an indexed, dependency-free PNG package.

Usage: python build/Convert-OfflineMapAssets.py path/to/custom-branch.zip
TMS rows are converted to XYZ. The original vector PBF map is never substituted.
"""
import hashlib
from contextlib import closing
import json
from pathlib import Path
import sqlite3
import sys
import tempfile
import zipfile

root = Path(__file__).resolve().parent.parent
target = root / "RescuAR.MAUI/Resources/Raw"
(root / 'artifacts').mkdir(exist_ok=True)
with zipfile.ZipFile(sys.argv[1]) as source, tempfile.TemporaryDirectory(dir=root / 'artifacts') as temporary:
    name, = [n for n in source.namelist() if n.endswith('/RescuAR.MAUI/Resources/Raw/MAP_V2.mbtiles')]
    database = Path(temporary) / 'map.mbtiles'
    database.write_bytes(source.read(name))
    with closing(sqlite3.connect(database)) as db:
        metadata = dict(db.execute('SELECT name, value FROM metadata'))
        assert metadata['format'] == 'png'
        tiles = db.execute('SELECT zoom_level,tile_column,tile_row,tile_data FROM tiles ORDER BY zoom_level,tile_column,tile_row').fetchall()
        manifest = dict(Version=1, Format='png', Scheme='xyz', MinZoom=min(t[0] for t in tiles),
                        MaxZoom=max(t[0] for t in tiles), TileCount=len(tiles),
                        Bounds=[float(v) for v in metadata['bounds'].split(',')], Source=metadata['description'])
        with zipfile.ZipFile(target / 'offline-map-tiles.zip', 'w', compression=zipfile.ZIP_STORED) as output:
            def write(path, data):
                info = zipfile.ZipInfo(path, date_time=(2026, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_STORED
                output.writestr(info, data)
            write('manifest.json', json.dumps(manifest, separators=(',', ':')).encode())
            for zoom, column, row, png in tiles:
                assert png.startswith(b'\x89PNG\r\n\x1a\n')
                write(f'tiles/{zoom}/{column}/{(1 << zoom) - 1 - row}.png', png)
    for context in ['BUILDINGS', 'PARKS', 'WATER']:
        name, = [n for n in source.namelist() if n.endswith(f'/RescuAR.MAUI/Resources/Raw/{context}.geojson')]
        data = source.read(name)
        assert json.loads(data)['type'] == 'FeatureCollection'
        (target / f'{context}.geojson').write_bytes(data)
    pack = target / 'offline-map-tiles.zip'
    print(json.dumps(dict(manifest=manifest, bytes=pack.stat().st_size, sha256=hashlib.sha256(pack.read_bytes()).hexdigest())))
