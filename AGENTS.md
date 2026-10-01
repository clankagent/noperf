# NOPerf public project

Public repository: https://github.com/clankagent/noperf. This folder is the
canonical published source. The original private lab remains separate.

The user authorized public GitHub publication and agent ownership on 2026-10-01.
Direct commits and pushes to main are authorized for maintenance within this
project. No production game-server deployment is authorized.

Preserve candidate identity/order, gameplay choices, RNG consumption and
AI/physics/network cadence. Build against the real tested server assemblies.
Unknown game assemblies must fail closed. Changing a build hash alone is never
revalidation. Keep experimental plugins disabled by default.

Never commit vendor binaries, decompiled game files, raw native traces, secrets,
private host details or private lab history. Use only reviewed aggregate evidence
and plugin validation output. The published code and test harness are MIT;
proprietary game and external dependencies retain their own licenses.

Method benchmarks are not whole-server gains. Report the inconclusive mission
comparison and untested live vanilla-client coverage prominently. Keep sampled
costs, per-step costs and accumulated game-frame costs distinct.

Visual reports start with a short plain-language explanation for a server owner:
what changed, whether it helped, why slowdown remains, and what tests actually ran.
Use one concrete visual per idea. Keep code, hashes, assertion details and long
tables on linked developer/evidence pages. A technically complete wall of text is
not an understandable explanation. Do not infer Word from 'report'. Motion is
user-controlled and respects reduced motion.
Use Claude Opus 5.5 for substantive visual design, then inspect and test it.
