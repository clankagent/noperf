"""Check the published report against reviewed evidence, without game inputs."""
import json
import re
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit
from xml.etree import ElementTree

root = Path(__file__).resolve().parents[1]
h = (root/'docs/detailed-report.html').read_text(encoding='utf-8')
runs = json.loads((root/'evidence/run-summary-final-28.json').read_text(encoding='utf-8-sig'))
phases = json.loads((root/'evidence/aging-phases-summary-28.json').read_text(encoding='utf-8-sig'))
def array(body, key):
    match = re.search(r'\b' + re.escape(key) + r'\s*:\s*(\[)', body)
    assert match, key
    return json.JSONDecoder().raw_decode(body[match.start(1):])[0]

mis = re.search(r'const MIS=(.*?);', h, re.S).group(1)
age = re.search(r'const AGE=(.*?);', h, re.S).group(1)
for key, run in [('off','escalation-baseline-long-13-bepinex'), ('on','escalation-all-long-06-bepinex')]:
    body = mis.split(key + ':', 1)[1].split('on:', 1)[0]
    record = next(r for r in runs['runs'] if r['run'] == run)
    loops, units = array(body,'loop'), array(body,'units')
    assert len(loops) == len(units) == len(record['steady_segments']) == 6
    for loop, unit, segment in zip(loops, units, record['steady_segments']):
        assert abs(loop-segment['weighted_loop_mean_ms']) < 0.000006
        assert unit == [segment['units_min'],segment['units_max']]

arrays = {k: array(age,k) for k in ['loop','fixed','steps','phys','scr']}
assert all(len(v) == len(phases['segments']) == 6 for v in arrays.values())
for i, s in enumerate(phases['segments']):
    expected = [s['loop_mean_ms_per_game_frame'], s['accumulated_fixed_mean_ms_per_game_frame'],
                s['phases']['PhysicsFixedUpdate']['calls_per_game_frame'],
                s['phases']['PhysicsFixedUpdate']['weighted_mean_ms_per_invocation'],
                s['phases']['ScriptRunBehaviourFixedUpdate']['weighted_mean_ms_per_invocation']]
    for key, value in zip(arrays,expected):
        assert abs(arrays[key][i]-value) < 0.000006, f'{key} segment {i}'

class Links(HTMLParser):
    def __init__(self):
        super().__init__(); self.links=[]
    def handle_starttag(self,tag,attrs):
        self.links += [v for k,v in attrs if k in {'href','src'} and v]

for path in [root/'README.md', *sorted((root/'docs').glob('*.html')), *sorted((root/'docs').glob('*.md'))]:
    content = path.read_text(encoding='utf-8')
    parser = Links(); parser.feed(content)
    links = parser.links + (re.findall(r'\]\(([^)\s]+)\)', content) if path.suffix == '.md' else [])
    for link in links:
        url = urlsplit(link)
        if url.scheme or url.netloc or not url.path:
            continue
        target = (path.parent/unquote(url.path)).resolve()
        assert target.is_relative_to(root) and target.exists(), f'{path.name}: broken local link {link}'
    if path.suffix == '.html':
        assert not re.search(r'<(?:script|iframe)[^>]+src=',content,re.I), 'External runtime dependency'
ElementTree.parse(root/'docs/assets/spatial-flow.svg')
for name in ['LICENSE','CONTRIBUTING.md','docs/testing.md','evidence/RuntimeValidation.txt','evidence/StartupValidation.txt','evidence/ReloadValidation.txt']:
    assert (root/name).is_file(), name
print('DOCS_PASS 12 mission means, 12 population ranges, 30 phase values, local links and SVG')
