# Contributing

Small, independent changes with measured benefit and preserved behavior are the
goal. Discuss a large subsystem change in an issue first; focused fixes and
documentation corrections can go straight to a pull request.

Use the real dedicated-server assemblies locally, never commit them. Include the
tested Steam build and assembly SHA256. Compare against Harmony reverse patches
of the shipped method, covering order, duplicates, nulls, exceptions, reentrancy,
thread fallbacks, state and RNG as applicable. Measure in the game's Mono runtime.
Synthetic method timings and whole-server results must be reported separately.

Do not change simulation rates, physics settings, network messages, gameplay
choices or client requirements in the name of performance. Keep speculative
optimizers optional and disabled. A new allowlisted hash needs fresh parity,
independent-loading, disabled-mode and unknown-build tests.

Never run the runtime harness on a production server. Use a disposable server
with a copied configuration and explicit workload identity. Include reproducible
commands and limitations; assertions are not independent confidence samples.

For documentation, use real data, working links and readable mobile layouts.
Motion must be controllable and respect reduced-motion preferences. Keep the
vanilla-client testing gap and inconclusive server gains clear.

Contributions are under the MIT license. Proprietary inputs, credentials and
private raw logs are not accepted. Pull requests are reviewed before merge;
prefer rebase merging.
