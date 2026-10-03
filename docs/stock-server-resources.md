# Stock server: two 20-minute flight tests

Two separate 20-minute flight tasks completed with an unmodified stock server,
without BepInEx or NOPerf on the server, and four instrumented headless test clients connected
over UDP. Both passed all 38 task checks. These measurements describe this
workload, rather than a player limit or a recommended server specification.

| Observed run | Average server CPU | Highest sampled server RSS | Client 2 replacements |
|---|---:|---:|---:|
| First 20-minute task | 0.7534 cores | 1.2283 GB | 1; flight resumed |
| Second 20-minute task | 0.7604 cores | 1.2122 GB | 2; flight resumed after each |

CPU is process CPU time divided by elapsed time: **0.76 cores means about 76%
of one core**, not 76% of the machine or a capacity estimate. RSS is resident
process memory; the table uses decimal GB and sampled peaks. It is not an exact
heap budget, and a peak could occur between samples.

In the second run, average server CPU rose from 0.717 cores in the first third
to 0.771 in the middle and 0.792 in the last third. Each client separately used
another 0.665-0.668 cores on average and reached roughly 1.25-1.30 GB RSS.
Those client costs are additional to the server measurement.

The tests shared a four-vCPU, 8 GiB virtual machine on Ryzen hardware. Clients
therefore competed with the server for resources. Swap counters were nonzero;
that observation does not establish the cause of the CPU trend. Process RSS
and overlapping cgroup memory categories should not be added into a physical
memory budget. Stock server frame and fixed-step counters were unavailable.

Client 2 received client-witnessed incoming destructive damage RPCs, replaced its aircraft through
ordinary controls, and resumed flight: once in the first run and twice in the
second. The other three clients had no observed losses. This demonstrates the
tested recovery path, but does not establish coverage of every combat loss or
prove survival throughout the task.

Two completed runs strengthen repeatability for this particular flight task.
The cause of an earlier startup failure remains unknown. Targeted combat,
retail clients and map rotation remain untested. These are stock-server
observations; they demonstrate no NOPerf performance gain and provide no
server-capacity claim.
