"""Validate the public plugin ZIP against its manifest and passing game tests."""
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import PurePosixPath

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('archive')
args = parser.parse_args()
names = {'NOPerf.Spatial.dll', 'NOPerf.Wrecks.dll', 'NOPerf.Navigation.dll', 'NOPerf.Diagnostics.dll'}
sha = lambda data: hashlib.sha256(data).hexdigest()
with zipfile.ZipFile(args.archive) as z:
    entries = z.namelist()
    assert len(entries) == len(set(entries)), 'Duplicate archive entries'
    for name in entries:
        parts = PurePosixPath(name).parts
        assert '\\' not in name and not name.startswith('/') and '..' not in parts, name
        assert not any(p.lower() in {'vendor', 'decompiled', '.git', 'work', 'obj', 'bin'} for p in parts), name
        if name.lower().endswith('.dll'):
            assert name in {'BepInEx/plugins/' + n for n in names}, name
        assert not name.lower().endswith(('.pdb', '.dmp', '.trace', '.etl', '.key', '.pem')), name
    manifest = json.loads(z.read('Manifest.json').decode('utf-8-sig'))
    assert manifest['GameSteamBuild'] == '24724541'
    assert {p['Name'] for p in manifest['Plugins']} == names
    reports = {n: z.read(n) for n in ['RuntimeValidation.txt', 'StartupValidation.txt', 'ReloadValidation.txt']}
    assert re.search(rb'(?m)^PASS assertions=\d+', reports['RuntimeValidation.txt'])
    assert b'VALIDATION_PASS independent=4 disabled=true unknown_hash=true current_hashes=true' in reports['StartupValidation.txt']
    for mode in [b'False', b'True']:
        assert re.search(rb'RELOAD_PASS transitions=3 phases=4 optimizers=' + mode + rb' assertions=\d+', reports['ReloadValidation.txt'])
    for p in manifest['Plugins']:
        actual = sha(z.read('BepInEx/plugins/' + p['Name']))
        assert actual == p['SHA256'], p['Name']
        marker = f"PLUGIN_SHA256 {p['Name']}={actual}".encode()
        assert all(marker in data for data in reports.values()), p['Name']
    for filename, data in reports.items():
        key = filename.removesuffix('.txt') + 'SHA256'
        assert sha(data) == manifest[key], filename
    if 'ChartSHA256' in manifest:
        assert sha(z.read('Mission-aging.png')) == manifest['ChartSHA256']
    assert {'README.md', 'LICENSE', 'docs/index.html', 'docs/how-it-works.html', 'docs/assets/spatial-flow.svg'}.issubset(entries)
print('RELEASE_PASS four tested DLLs; matching runtime/startup/reload hashes; public docs; no vendor binaries')
