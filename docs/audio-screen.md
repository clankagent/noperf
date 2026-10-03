# Did muting server audio help?

**No CPU saving was observed, so this experiment is not recommended or released.**

Four automated pilots flew the same stock Escalation task for four minutes in
each run. Both runs passed their checks, including the same 48 flight actions
and no aircraft losses. The second run changed only the server's listener volume
to zero. The game still performed its normal audio calls.

| Flight-window measurement | Normal volume | Muted |
| --- | ---: | ---: |
| Server CPU, average fraction of one core | 0.713 | 0.732 |
| Server resident RAM, observed range | 1.11–1.16 GB | 1.09–1.14 GB |

The clients also used slightly more CPU in the second run. One pair cannot tell
us whether the small differences came from the evolving battle, shared-machine
contention or the volume change. It proves neither a slowdown nor a RAM saving.

This was an experimental BepInEx server compared against its volume-one case,
with four headless UDP clients on the same machine. It was not a comparison
against a server without BepInEx, retail Steam coverage, or a capacity test.
CPU was measured separately for each process during seconds 60–220 of flight.

Separate generated playback checks covered completion, replay, pitch, looping
and overlapping sounds at a coarse sampling resolution. They do not establish
unchanged game-wide audio state or random-number consumption. With no observed
CPU saving, broader validation is not justified for this candidate.

The next investigation measures the central method that finishes jobs and
applies their results. That can distinguish main-thread work from waiting for
worker jobs; a successful profiler run alone will not count as an optimization.
