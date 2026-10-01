# Evidence included in the public repository

These are authored aggregate measurements and plugin validation output, from
the completed 2026-10-01 test batch. Game startup logs, decompiled inputs and native
traces are not included. The tested build was Nuclear Option 0.34.1 / Steam
24724541; its assembly SHA256 is recorded in Manifest.json and BuildGuard.cs.

| File | Purpose |
| --- | --- |
| RuntimeValidation.txt | Full runtime10 differential pass, including the separate lab-only empty snapshot experiment; production DLL hashes |
| StartupValidation.txt | Independent plugin loading, disabled mode and unknown-assembly rejection |
| ReloadValidation.txt | Both baseline and all-optimizer mission-transition/state passes |
| Manifest.json | Original tested plugin and validation hashes; original private-lab checkpoint provenance |
| run-summary-final-28.json | Frame-weighted mission summaries with startup windows excluded and verified workload identities |
| aging-phases-summary-28.json | Unoptimized aging profile: phase invocation costs, calls/frame and six steady segments |

The public source snapshot has identical src/ and tests/ contents to the final
tested lab source. The public release manifest records its public source commit;
the original lab manifest here remains unchanged as historical measurement
provenance. Public documentation and packaging have been improved since testing.

`gen0` in historical harness output means `GC.CollectionCount(0)`, not allocated
bytes. The Mono allocation-byte counter was unavailable; later harness output
marks this as -1. Assertion totals include repeated cases and are not independent
statistical samples. Sampled methods are inclusive and may overlap; phase cost
per invocation is distinct from fixed time accumulated per game frame.

The historical terminal-*-06 summary entries ran Free Flight because the stock
launcher split the spaced mission argument. Their verified_mission values correct
that identity. The later reload validation really exercised Terminal Control and
Escalation. No live unmodded-client join/rejoin test is included.

See [the report](../docs/benchmark-report.md) for the results and limits.
