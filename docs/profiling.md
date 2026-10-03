# Finding useful performance targets

Diagnostics measures where work happens. It does not make the server faster.
These additional modes are in current source, outside the v0.1.0 download.
Build against the exact supported Linux game assemblies before using them.

In `BepInEx/config/agent.noperf.diagnostics.cfg`, enable `[General] Enabled`
and choose **one investigation at a time** under `[Profiling]`. Restart to apply.
Leave these modes off for normal baseline-versus-mod comparisons.

## Network work

`NetworkMethods = true` measures transform eligibility, writing, batching,
visual updates and transport separately. Two independent two-client Escalation
runs passed their 30 checks. Each ran three minutes of taxi/takeoff followed by
three minutes of sustained flight and gun bursts.

The observed pointer-copy work was about **0.0013 ms per game frame**;
visual updates were about **0.24–0.25 ms**. Copying packets is a poor target in
that workload. Visual updates still contain gameplay-relevant state, so skipping
them on a headless server needs separate correctness evidence.

These are sampled, inclusive measurements with hook overhead. Nested values
overlap; do not add them together. Fixed-stride sampling can favor particular
units. Tiny methods can be inlined: verify pointer-copy counts against concrete
transform writes before using their timing. The retained windows passed that
lower-bound check, which does not prove every call was observed.

Detailed results: [network profiling evidence](../evidence/NetworkProfilingValidation.json).

## Scalar memory counters

`MemoryCounters = true` enables optional scalar observations in current source;
it is disabled by default. Build Diagnostics against the supported game assemblies,
set `[General] Enabled = true` and `[Profiling] MemoryCounters = true` in
`BepInEx/config/agent.noperf.diagnostics.cfg`, and restart the server. Leave the
other investigation modes disabled when collecting these counters.

The first report waits at least 120 seconds after Diagnostics initializes.
Reports occur at aggregation boundaries at least 30 seconds apart. The BepInEx
log separates managed GC bytes, Unity allocated/reserved/unused-reserved bytes,
and Mono used/heap-capacity bytes. These scopes overlap: do not sum them into
process RSS, retained object classes or a reclaimable-memory budget. Unsupported
calls, errors and nonpositive values report `UNKNOWN` with an availability reason.
The GC query does not request a collection. Observation adds overhead and is not
a memory optimization.

A separate Linux Escalation run with four headless UDP clients completed an
eight-minute flight task and produced 14 reports. All six counters returned
positive values on the supported build. Managed GC memory ranged from 86–102 MiB;
Unity allocated memory rose from 637 to 682 MiB. The server had Diagnostics
installed, so this establishes counter availability, not a stock comparison,
memory saving or production-capacity recommendation.

The standalone output checks need .NET 8 and no game or vendor assemblies:

```sh
dotnet run --project tests/NOPerf.MemoryCounterTests -c Release
```

They compile the actual counter source against test doubles and check unavailable
values, exceptions, 64-bit values, separate scopes and log context. They do not
verify native Unity API availability or the plugin's runtime sampling schedule.

## Asset memory

`AssetMemory = true` inventories loaded meshes, textures and other selected
asset types, plus mesh references from colliders and renderers. The first scan
starts after 120 seconds in multiplayer. `AssetMemoryIntervalSeconds` sets the
pause between completed scans (default 120; range 30–600 seconds).

Queries run on separate frames, then objects are processed in small slices.
Native enumeration and individual lookups cannot be interrupted. The log reports
their measured overhead as well as completed inventories; an unfinished scan
is not a complete result.

Unity's per-object byte reports may overlap or return zero. **Zero does not mean
the object uses no RAM.** Mesh readability and observed collider references help
assess candidates, but these numbers are neither total process RAM nor a promise
that memory can be reclaimed. Reference sets are taken at different moments and
do not cover all game fields, native systems or future spawns.

One two-client flight, rocket and aircraft-replacement run passed 51 checks and
completed three inventories without lookup failures. It reported about **317 MiB
on meshes**, of which **308 MiB was already unreadable**; meshes referenced by
colliders accounted for about **307 MiB**. Simply disabling readability will not
recover hundreds of megabytes in this workload. We have not removed these assets
or established a memory saving.

Each typed inventory took about ten seconds to process, spread across frames.
Its largest individual query took 16 ms. The previous broad all-object query took
172 ms and held more than 224,000 references while processing them. This is an
improvement to the measuring tool, not a game optimization. See the
[completed asset inventory evidence](../evidence/AssetMemoryValidation.json).

Use process RSS and CPU measurements separately. Our current clients are
headless dedicated-build UDP adapters; retail Steam clients remain untested.
The comparison baseline includes the observation bridge and BepInEx.
