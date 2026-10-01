# What is automated, and what is not

NOPerf has automated differential and integration tests inside the real dedicated
server, plus completed mission profiling runs. It does not yet have unattended
game-test CI for every commit or a live vanilla-client compatibility test.

## Completed test coverage

| Test layer | What runs automatically | Completed evidence | Limit |
| --- | --- | --- | --- |
| In-game differential harness | Calls patched methods and Harmony reverse patches of the actual shipped methods over generated cases, then compares identities/order, routes/state, exceptions and RNG | 1,843,424 assertions in runtime6; two expanded runtime10 processes, each 2,458,591 | Includes repeated cases; runtime10 totals include an unshipped empty snapshot experiment |
| Plugin startup checks | Loads each plugin independently; checks all-disabled mode and rejects an unknown assembly hash | StartupValidation.txt | Not a general third-party-mod compatibility test |
| Live mission transitions | Starts populated Terminal Control, changes to Escalation, back to Terminal Control and then Escalation; checks live query/state parity | Baseline 106,391 assertions, all optimizers 106,423; three transitions/four phases in each | Not a deterministic replay of an identical full mission |
| Workload profiling | Collects steady frame timing over short and hour-long Escalation runs and additional Carrier Duel/weapons profiles | Reviewed aggregate JSON and report | No players; workloads varied; whole-server improvement remains unproven |
| Later headless multiplayer workloads | Separate dedicated-build UDP client processes taxi, fly and fire in stock Escalation; server checks ownership, flight and ammunition | Three four-client baseline passes and one completed baseline/Spatial pair, 50 checks per run; actual Spatial activation verified | Baseline includes BepInEx/test bridge; not retail Steam or human play. Loading, flight, reconnect and rotation failures retained |
| Deeper optional job profiling | Measures input collection, scheduling and results separately | All 16 job targets emitted in three completed two-client 6-minute instrumented workloads | Inclusive timings overlap/include waits; instrumentation adds overhead, so these are not mod comparisons |
| Release verification | Refuses packaging unless runtime, startup and optional reload reports match all four DLL hashes | scripts/package.ps1 and scripts/check_release.py | Passing reports apply only to their exact binaries/build |

These are more than standalone unit tests: the harness executes in the game's
Mono runtime using the current game types and methods. There is no fake game
implementation acting as the baseline.

## Repeating the real-server tests

Use a disposable Linux server installation and isolated network environment,
with your own legally obtained game/BepInEx inputs. Never place the harness on
a production server. Build the four plugins and the runtime test project using
the [build commands](developer-guide.md#build-from-source). Copy the five authored DLLs
into the disposable installation only. The normal release contains four DLLs
and intentionally excludes the harness.

Set `HideManagerGameObject = true`. For the differential suite, enable all three
optimizers and Diagnostics, with all Diagnostics profiling options false. Start
with an empty world; the harness refuses `NOPERF_TEST=1` on a populated world or
an unknown game assembly. The test output is
`BepInEx/data/noperf/runtime-tests.txt`.

| Environment mode | Purpose | Output |
| --- | --- | --- |
| `NOPERF_TEST=1` | Differential parity and warmed method benchmarks on an empty world | runtime-tests.txt; require a fresh `PASS assertions=` line and matching DLL hashes |
| `NOPERF_TEST=1 NOPERF_EMPTY_EXPERIMENT=1` | Also runs the separate lab-only empty snapshot experiment used in runtime10 | Same output; do not call this extra experiment a shipped optimizer |
| `NOPERF_RELOAD=baseline` | Live mission transition suite with optimizers disabled | mission-reload-tests.txt; `RELOAD_PASS ... optimizers=False` |
| `NOPERF_RELOAD=all` | Same suite with all three optimizers enabled | mission-reload-tests.txt; `RELOAD_PASS ... optimizers=True` |
| `NOPERF_WEAPONS_PROFILE=1` | Lab-only weapons-call profile | Optional profile output; not a parity verdict |
| `NOPERF_INVENTORY=1` | Writes available built-in mission names | builtin-mission-names.txt |

Set plugin configurations to match the mode before starting. Reload modes need
an offline auto-hosted Terminal Control mission. The completed runs used
`-batchmode -nographics -autoHost -socket Offline -state Multiplayer -mission
'Terminal Control' -limitframerate 60 -closeafter 1200`. Those are lab launch
arguments, not instructions to change production simulation settings.

Preserve the spaced mission argument as one argument; the stock BepInEx shell
launcher split it in an early run. Verify the loaded mission in startup logs and
runtime state. Use a timeout longer than the intended run, require a successful
process exit and a fresh PASS report, and compare each reported plugin hash with
the actual DLL. Move prior reports aside before starting; an old PASS is not a
successful new run. Restore copied configurations after every run. No universal
public orchestration script or cloud game-test runner is claimed here.

## Automated checks without the game

The public documentation check validates chart/evidence parity and required
local files without downloading game assemblies. GitHub Actions runs that check
on commits and pull requests. This verifies published evidence, not the behavior
of the proprietary server.

```sh
uv run scripts/check_docs.py
uv run scripts/check_release.py /absolute/path/NOPerf-0.1.0-build24724541.zip
```

## Still missing

- Live unmodded-client join, rejoin, Steam authentication and client-flown aircraft.
- Wreck parity with every live depot/navigation callback; current collector tests
  use an inactive attached Unit.
- Deterministic full mission replays and repeated order-balanced long comparisons.
- An isolated private game-test runner triggered on every source change. It would
  need the exact locally held game assemblies and explicitly managed lab state.

The [newer Ryzen resource report](resource-report.html) explains the separate
multiplayer measurements and their limits. Those runs used
[NOTestPilot](https://github.com/clankagent/notestpilot), which supplies repeatable
commands and checks. It still needs reliable occupied reconnects, rotation, longer
combat and retail-client coverage. Charts use reviewed aggregates and have their
own data check; this documentation CI does not execute the game.
