"""Reconcile published resource charts with reviewed aggregate observations."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
html = (root / 'docs/resource-report.html').read_text(encoding='utf-8')
data = json.loads((root / 'evidence/ryzen-resources-20261001.json').read_text(encoding='utf-8'))
runs = {run['id']: run for run in data['runs']}
body = re.search(r'var PHASES = (\{.*?\n  \});', html, re.S).group(1)
phases = json.loads(re.sub(r'(\b\w+)(?=\s*:)', r'"\1"', body))
for key, seconds in [('takeoff', 180), ('flight', 600)]:
    window = next(w for w in runs['ryzen-four-half-baseline-01']['observations']
                  if w['requested_seconds'] == seconds)
    chart = phases[key]
    server = window['processes']['server']
    external = window['external_approximate']
    assert abs(chart['server'] - server['cpu_cores']) < .00005
    assert chart['frames'] == server['frame_bucket_counts']
    assert chart['mem']['server'] == server['resident_peak_bytes']
    assert chart['mem']['minAvail'] == external['guest_min_available_memory_bytes']
    assert abs(chart['ups'] - server['updates_per_second']) < .05
    for i in range(4):
        client = window['processes'][f'client{i+1}']
        assert abs(chart['clients'][i] - client['cpu_cores']) < .00005
        assert chart['mem']['clients'][i] == client['resident_peak_bytes']
    assert abs(chart['labBusy'] - external['guest_cpu_busy_percent']) < .05
    assert abs(chart['waitPct'] - external['guest_pressure']['cpu_some_stall_percent']) < .05
activity = json.loads(re.search(r'var ACTIVITY = (\[.*?\n\]);', html, re.S).group(1))
for bar, provenance in zip(activity, data['activity_provenance'], strict=True):
    windows = [w for id in provenance['ids'] for w in runs[id]['observations']
               if w['requested_seconds'] == provenance['seconds'] and w['passed']]
    values = [w['processes']['server']['cpu_cores'] for w in windows]
    assert len(windows) == len(provenance['ids'])
    assert bar['name'] == provenance['name'] and bar['sub'] == provenance['sub']
    assert bar['runs'] == len(windows)
    assert abs(bar['lo'] - min(values)) < .0005
    assert abs(bar['hi'] - max(values)) < .0005
assert data['whole_server_gain_established'] is False
assert len(runs) == len(data['runs'])
assert all(profile['instrumented'] for profile in data['job_profiles'])
assert not re.search(r'<(?:script|iframe)[^>]+src=', html, re.I)
ground = json.loads((root / 'evidence/GroundInputValidation.json').read_text(encoding='utf-8'))
assert ground['source_only_experimental'] and ground['disabled_by_default']
assert not ground['whole_server_gain_established']
assert ground['game_assembly_sha256'] == runs['ryzen-four-half-baseline-01']['input_sha256']['gameAssembly']
assert [run['assertions'] for run in ground['native_runs']] == [3248, 6360]
assert all(len(run['benchmarks']) == 5 for run in ground['native_runs'])
assert all(run['passed'] and run['checks'] == 30 for run in ground['workloads'])
assert all(not failure['game_launched'] for failure in ground['setup_failures'])
print('RESOURCES_PASS two CPU/RSS/frame/pressure phases, five workload bars, explicit provenance and no external runtime')
