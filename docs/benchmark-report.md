# Nuclear Option server performance mods

Completed overnight test batch — 1 October 2026.

Four independent BepInEx 5 plugins target the Linux dedicated server, Nuclear
Option 0.34.1, Steam build 24724541, game build `cb745d6c44f1`. The runtime guard
requires the exact tested `Assembly-CSharp.dll` SHA256. Install only wanted DLLs;
each has its own enable setting. Spatial defaults enabled; Wrecks, Navigation and
Diagnostics default disabled. Production deployment was not performed.

## Measured optimizations

| Plugin | Change | Evidence and limits |
|---|---|---|
| Spatial | Fuses the unit candidate grid traversal into the output list. | Initial game-Mono benchmarks reduced method time about 28–31%; later larger-range repeats about 31–33%. Original cell bounds, traversal, reverse unit order, duplicates and null entries are preserved. Four actual Escalation runs did not establish a clear whole-server speedup. |
| Wrecks | Reuses a collector's temporary candidate snapshot during a guarded search scope. | Full-method 128-candidate ABBA tests improved about 0.9–3.5% across repeats, with fewer GC API0 collections. Empty-search overhead varied from roughly 0.006 to 0.65 microseconds. Experimental; leave disabled unless your workload benefits. |
| Navigation | Caches dense road-node membership while retaining the original sort and route decisions. | Runtime6 synthetic graphs improved about 6–13%; later repeats also improved. Measured steady Escalation pathfinding calls were sparse, so whole-server benefit is unproven. Experimental and disabled. |
| Diagnostics | Reports existing game timing, heap and separate GC counters; additional methods/phases are opt-in. | Helps investigate bottlenecks; instrumentation has overhead and is not a speed optimization. Inclusive nested method timings must not be summed. |

No optimization changes simulation rates, RNG calls, physics parameters, AI update
cadence, network formats or mission content. A candidate wreck-grid fusion was
removed after regression. A smaller empty-only snapshot experiment saved about
0.11–0.14 microseconds per empty call but remains lab-only; it did not establish a
useful nonempty or whole-server gain.

## Validation

The baseline consists of Harmony reverse patches of the actual shipped methods.
Runtime6 passed 1,843,424 assertions covering candidate identity/order, target/state
and road route/status/parent/bitwise float parity, nested and off-thread fallbacks,
exception cleanup, capacity trimming and Unity RNG state. Two runtime10 processes
repeated those checks and added 1,488 fractional/nonfinite spatial cases and null
cell/output exception parity. Each complete lab run passed 2,458,591 assertions;
that count includes the separate empty-only lab experiment.

Current DLLs also passed independent loading, all-disabled mode and rejection of
an unknown game assembly. Both baseline and all-optimizer configurations passed
three mission transitions across four populated Terminal Control/Escalation
phases, with 106,391 and 106,423 live-query/state assertions respectively. The
release manifest records DLL and validation-file hashes; packaging refuses
reports that do not match the current DLLs. ZIP inventory and archived byte hashes
were verified in a private packaging smoke test.

Wreck collector parity uses an inactive attached Unit; it does not exercise every
live ground-vehicle depot/navigation callback. Long mission tests exercise live
simulation but are not deterministic gameplay equivalence replays.

## Real workloads

Four ten-minute Escalation comparisons, with additional instrumentation off,
gave baseline/spatial loop means of 14.044/14.026 ms and 14.430/13.999 ms after
excluding four startup windows. Pair differences of about 0.1% and 3.0% fall within
baseline variation. These are elapsed game-loop timing measurements, not claims
of an equivalent FPS or player-capacity improvement.

A combined-optimizer hour-long Escalation mission completed without a crash.
The workload aged: its first steady 20-window segment averaged 14.77 ms per loop,
while its final 15-window segment averaged 37.15 ms. Managed heap grew from about
81.8 to 206.5 MB. This alone neither proves an optimizer regression nor a leak.
The unoptimized hour-long Escalation run also passed without a crash, using the
same aggregate diagnostics and frame limit. Both excluded four startup windows:

| Run | Mean loop ms | First steady 20 windows ms | Final 15 windows ms | Unit range | Final managed heap MB |
|---|---:|---:|---:|---:|---:|
| All optimizers disabled | 20.05 | 14.28 | 38.80 | 891–1,164 | 189.1 |
| All three enabled | 21.68 | 14.77 | 37.15 | 1,006–1,187 | 206.5 |

The combined run's overall mean was about 8.1% higher, while its final segment
was lower. This single pair did not replay the same combat workload and does
not establish a consistent benefit or a causal regression. Both runs aged
substantially, including increasing accumulated fixed-update time. The baseline
used about 175% of one CPU, reached 1,598,604 KiB peak RSS, and had no swap or
major page faults. Its separate GC API0 counter reported 15 steady collections,
versus 14 in the combined run. These facts do not identify a memory leak.
The separate aging observation below also completed.

![Timing and unit populations in the two completed hour-long runs](assets/Mission-aging.png)

Balanced worker-pool maximum trials in 3/2/2/3 order averaged
14.201/14.018/14.028/14.357 ms. All settled to two active job workers. No worker
tuning recommendation follows from that small difference. Physics callback reuse
was already enabled and transform auto-synchronization disabled; neither changed.

Historical filenames `terminal-*-06` actually represent **Free Flight**. The
vendor BepInEx launcher split the spaced mission argument and the game fell back.
Their 3.266/3.302 ms spatial/all measurements apply only to Free Flight. The new
authored lab launcher preserves arguments; subsequent runs verify the mission
from game logs or runtime state. Original filenames and the identity correction
are retained in evidence.

The ten-minute Carrier Duel profile passed with Spatial enabled, Wrecks and
Navigation disabled, and sampled methods/phase instrumentation enabled. After
four startup windows, its loop mean was 10.96 ms, accumulated fixed-update time
8.48 ms per game frame, and unit population 133–187. Observed phase means were physics 3.83 ms per fixed
step, fixed scripts 2.96 ms, update scripts 0.77 ms, and late scripts 0.17 ms.
These instrumented phases provide context, not a controlled speedup comparison.
The fifteen-minute Escalation weapons profile also passed, with unchanged
production DLLs. Its 25 steady reports included 1,092,019 trajectory calls,
365,889 bullet-simulator fixed updates, 1,914 blast calls and 1,472 penetration
calls. Sampled trajectory time averaged about 2.8 microseconds; fully timed
blast and penetration averages were 0.227 and 0.199 ms, respectively. Those
damage timings include nested work and observer overhead. Physics averaged
4.48 ms per fixed step; fixed scripts 2.05 ms. No large, safe weapons patch was
justified by these observations.

The existing game tracker accumulates **all fixed steps within each game frame**.
Its fixed-update timing therefore differs from a phase probe's time per fixed
step. The weapons observation recorded about 1.066 physics steps per game frame.
The game's loop total covers update, accumulated fixed update and late update;
it is not total wall-clock frame latency including frame-rate pacing.

The final **unoptimized** Escalation profile passed its full hour, with sampled
methods and four phase probes enabled. Its 115 steady reports recorded 141,754
game frames and 206,105 physics steps. Overall loop work averaged 20.54 ms per
game frame; this instrumented run is not a controlled optimizer comparison.

| Segment | Loop ms/game frame | Accumulated fixed ms/game frame | Physics steps/game frame | Physics ms/step | Fixed scripts ms/step |
|---|---:|---:|---:|---:|---:|
| First steady 20 windows | 13.85 | 9.32 | 1.05 | 4.37 | 1.85 |
| Windows 85–104 | 37.86 | 30.94 | 2.51 | 6.19 | 3.02 |
| Final 15 windows | 48.08 | 36.85 | 3.37 | 5.54 | 2.87 |

Both increased per-step cost and additional fixed steps per game frame accompany
the late slowdown. This explains part of the accumulated fixed-update increase;
it does not establish the underlying cause or a behavior-preserving way to remove
that work. The selected phase probes cover only part of the loop. No physics or
AI cadence was changed. This run used about 172% of one CPU, reached 1,646,644 KiB
peak RSS, and had no swap or major page faults. Managed heap finished at 189.3 MB,
with 15 steady GC API0 collections; those numbers alone do not prove a leak.

The unoptimized unit-grid query cost was roughly 0.031 ms per game frame in this
profile. Fully timed road pathfinding totaled about 0.075 ms per game frame over
1,299 calls. These small observed costs help explain why method benchmark gains
do not imply a large server-wide speedup. Estimates include instrumentation;
sampled methods can alias call order and inclusive costs must not be summed.
The source archive includes raw aggregates and reproducible run/phase summary
scripts. Untimed method windows retain their call counts and unavailable timing.

Other audits covered objective checks, network writer reuse and send cadence,
batched detector raycasts, headless visual effects, debris/wreck lifetimes and
historical unit references. Existing batching/pooling was already present;
observed objective and network costs did not justify a large new patch. Effects
and debris paths also consume RNG or apply callbacks/forces, so skipping them
would require additional behavioral proof. The registry retains historical unit
references used by later tracking/message code: pruning them on Unity's
"destroyed object" null comparison is not demonstrated safe. Growing managed
heap alone does not identify an actionable leak.

The job manager schedules dependent native calculations, applies pilot inputs,
and completes/applies results in a defined sequence. Inclusive timing does not
justify moving or skipping those synchronization points. Native physics cost
cannot be safely removed by reducing fixed steps, sensors, units or collision
work while meeting this task's gameplay constraint.

## Install and compatibility

Use BepInEx 5 for the Linux server. Set `[Chainloader] HideManagerGameObject = true`
in `BepInEx/config/BepInEx.cfg`; the test build otherwise failed to run later
plugin lifecycle callbacks. Copy selected DLLs into `BepInEx/plugins/`, restart,
then use each plugin's `agent.noperf.*.cfg` for enablement. Restart after changing
patch enablement. Remove DLLs while stopped to uninstall. See the included README
for diagnostic settings and building from source.

Clients do not install these plugins. Static inspection found no new network
messages or plugin-list requirement, but **a live unmodded-client connection has
not been tested**. Tests used isolated offline hosting without human players;
Steamworks initialization reported a startup exception, so joining/rejoining,
authentication and client-controlled aircraft workloads are outside the measured
coverage. Existing ModdedServer policy is left to the server owner.

Unknown game assemblies leave original game methods active. Revalidate after an
update; merely changing the hash allowlist is insufficient. Targets already
modified by another prefix/transpiler are skipped at startup. Arbitrary third-party
patches installed later are not covered by that check.

The source ZIP contains authored code, lab scripts, documentation and aggregate
evidence. Game/Unity/BepInEx/Harmony runtime binaries, decompiled game files and
raw native traces are excluded. Mono's allocated-byte API returned a stub zero;
calibrated reports use `-1` for unavailable data. Reported `gen0` denotes
`GC.CollectionCount(0)`; do not sum its three generation counters.

The isolated lab server exited normally before the deadline. Its configuration
was restored, its game assembly and all production DLL hashes remained unchanged,
and no lab game process or test timer remained active. Production servers were
not changed. The overnight heartbeat was deleted after testing ended.
