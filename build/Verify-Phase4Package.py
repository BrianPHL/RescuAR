"""Bounded local APK checks; supplements the pinned native/field validation scripts.

Usage: python build/Verify-Phase4Package.py [path/to/app-Signed.apk]
Does not replace the release native rebuild, stripping checks or device validation.
"""
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parent.parent
apk = Path(sys.argv[1]) if len(sys.argv) > 1 else root / 'RescuAR.MAUI/bin/Debug/net9.0-android/android-arm64/com.rescuar.app-Signed.apk'
raw = root / 'RescuAR.MAUI/Resources/Raw'
with zipfile.ZipFile(apk) as package:
    assert package.testzip() is None, 'APK CRC failure'
    embedded_assemblies = [n for n in package.namelist() if n.startswith('assemblies/') or 'libassemblies' in n or n.endswith('.dll.so')]
    assert embedded_assemblies, 'Use EmbedAssembliesIntoApk=true for a directly installable debug APK'
    assert 'lib/arm64-v8a/lib_RescuAR.MAUI.dll.so' in embedded_assemblies or any('assemblies' in n for n in embedded_assemblies)
    # This pinned debug profile wraps managed assemblies in ELF containers.
    # Verify the actual payload, not just the existence of an assembly-shaped entry.
    managed = package.read('lib/arm64-v8a/lib_RescuAR.MAUI.dll.so')
    assert managed[:5] == b'\x7fELF\x02'
    section_offset = struct.unpack_from('<Q', managed, 40)[0]
    section_size, section_count, names_index = struct.unpack_from('<HHH', managed, 58)
    sections = [struct.unpack_from('<IIQQQQIIQQ', managed, section_offset + i * section_size) for i in range(section_count)]
    names_section = sections[names_index]
    names = managed[names_section[4]:names_section[4] + names_section[5]]
    payloads = [section for section in sections if names[section[0]:].split(b'\x00')[0] == b'payload']
    assert len(payloads) == 1, 'Managed assembly payload missing'
    payload = payloads[0]
    dll = (root / 'RescuAR.MAUI/bin/Debug/net9.0-android/android-arm64/RescuAR.MAUI.dll').read_bytes()
    assert managed[payload[4]:payload[4] + payload[5]] == dll, 'APK contains stale app code'
    native = package.read('lib/arm64-v8a/libnative_bridge.so')
    assert native == (root / 'RescuAR.MAUI/Platforms/Android/lib/arm64-v8a/libnative_bridge.so').read_bytes()
    assert native[:4] == b'\x7fELF' and native[4] == 2 and struct.unpack_from('<H', native, 18)[0] == 183
    abis = sorted({n.split('/')[1] for n in package.namelist() if n.startswith('lib/') and n.endswith('.so')})
    assert abis == ['arm64-v8a'], abis
    profile = package.read('assets/rescuar-build-profile.txt').decode('utf-8-sig')
    assert 'diagnosticRouteOverride=false' in profile and 'correctiveBatch=ARCore-14' in profile
    map_bytes = package.read('assets/offline-map-tiles.zip')
    assert map_bytes == (raw / 'offline-map-tiles.zip').read_bytes()
    with zipfile.ZipFile(io.BytesIO(map_bytes)) as tiles:
        assert tiles.testzip() is None
        metadata = json.loads(tiles.read('manifest.json'))
        entries = [entry for entry in tiles.namelist() if entry.startswith('tiles/')]
        assert len(entries) == metadata['TileCount'] == 3555
        assert metadata['Scheme'] == 'xyz' and metadata['Format'] == 'png'
        assert metadata['MinZoom'] == 13 and metadata['MaxZoom'] == 18
        assert all(tiles.read(entry).startswith(b'\x89PNG\r\n\x1a\n') for entry in entries)
    counts = {}
    for context, expected_count in [('BUILDINGS', 1058), ('PARKS', 219), ('WATER', 465)]:
        content = package.read(f'assets/{context}.geojson')
        assert content == (raw / f'{context}.geojson').read_bytes()
        doc = json.loads(content.decode('utf-8-sig'))
        assert doc['type'] == 'FeatureCollection' and len(doc['features']) == expected_count
        counts[context] = expected_count
    assert not any(n.endswith(('MAP_V2.mbtiles', '2D_MAP.mbtiles')) for n in package.namelist()), 'Unused MBTiles must stay excluded'

manifest = ET.parse(root / 'RescuAR.MAUI/obj/Debug/net9.0-android/android-arm64/android/AndroidManifest.xml').getroot()
name = '{http://schemas.android.com/apk/res/android}name'
assert any(p.get(name) == 'android.permission.VIBRATE' for p in manifest.findall('uses-permission'))
assert any(action.get(name) == 'android.intent.action.TTS_SERVICE' for action in manifest.findall('queries/intent/action'))
for page in (root / 'RescuAR.MAUI/Views').rglob('*.xaml'):
    ET.parse(page)
result = dict(apk=str(apk), size_bytes=apk.stat().st_size, sha256=hashlib.sha256(apk.read_bytes()).hexdigest(),
              crc='passed', embedded_managed_code=True, app_assembly_matches_build=True,
              app_assembly_sha256=hashlib.sha256(dll).hexdigest(), native_abi=abis, native_bridge_matches_repository=True,
              map_tiles=len(entries), map_sha256=hashlib.sha256(map_bytes).hexdigest(),
              context_features=counts, unused_mbtiles_excluded=True, vibration_permission=True, tts_query=True)
(root / 'artifacts').mkdir(exist_ok=True)
(root / 'artifacts/phase4-package-checks.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, indent=2))
