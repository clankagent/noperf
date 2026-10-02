"""Validate consumer profiling evidence and its stated limits; no game execution."""
import json
from pathlib import Path

root=Path(__file__).resolve().parents[1]
network=json.loads((root/'evidence/NetworkProfilingValidation.json').read_text(encoding='utf-8'))
memory=json.loads((root/'evidence/AssetMemoryValidation.json').read_text(encoding='utf-8'))
assert network['game_assembly_sha256']==memory['game_assembly_sha256']
assert network['steam_build']==memory['steam_build']==24724541
assert network['installed_network_targets']==21
assert len(network['runs'])==2
assert not network['whole_server_speedup_established']
for run in network['runs']:
    assert run['passed'] and run['checks']==30
    profile=run['profile']
    assert profile['windowBoundary']=='closing frames row'
    assert profile['steady_windows']==run['copy_coverage_check']['complete_windows']==8
    assert run['copy_coverage_check']['minimum_pointer_copies_over_observed_transform_writes']>=1
    methods=profile['methods']
    copies=methods['NetworkWriter.CopyFromPointer']
    assert copies['calls']>=sum(v['calls'] for k,v in methods.items()
        if k.endswith('NetworkTransform.Write'))
    assert 0<copies['rough_inclusive_ms_per_game_frame']<.002
    assert .23<methods['SendTransformBatcher.VisualUpdate']['rough_inclusive_ms_per_game_frame']<.26
assert memory['run']['passed'] and memory['run']['checks']==51
assert not memory['whole_server_gain_established'] and not memory['memory_saving_established']
assert len(memory['completed_inventories'])==3 and memory['unfinished_inventory_count']==0
for scan in memory['completed_inventories']:
    done=scan['completion'];mesh=scan['groups']['meshes'];refs=scan['mesh_references']
    assert done['lookup_failures']==0 and len(scan['queries'])==11
    assert done['discovered']==sum(query['count'] for query in scan['queries'])
    assert done['max_slice_ms']>=max(query['enumeration_ms'] for query in scan['queries'])
    assert done['max_slice_ms']<17 and 10<done['elapsed_seconds']<11
    assert refs['readable_reported_bytes']+refs['unreadable_reported_bytes']==mesh['unity_reported_bytes']
    assert refs['readable_collider_count']<=min(refs['readable_count'],refs['collider_count'])
    assert refs['collider_count']<=mesh['count'] and mesh['zero_reports']<=mesh['count']
    assert refs['collider_reported_bytes']>mesh['unity_reported_bytes']*.95
    assert refs['readable_reported_bytes']<10*1024**2
print('PROFILING_PASS network coverage/timing bounds, three complete typed inventories, overhead and explicit no-gain limits')
