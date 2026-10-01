"""Summarize saved diagnostics; startup windows excluded, no FPS-gain claims."""
from pathlib import Path
import argparse
import json
import re
import statistics

p = argparse.ArgumentParser()
p.add_argument("directory", type=Path)
p.add_argument("--discard-windows", type=int, default=4)
p.add_argument("--segment-windows", type=int, default=0,
               help="Also summarize consecutive steady windows to inspect workload aging")
args = p.parse_args()
identity_path = args.directory / "mission-identities.json"
identities = json.loads(identity_path.read_text(encoding="utf-8-sig")) if identity_path.exists() else {}
summary = []
for path in sorted(args.directory.glob("*-bepinex.txt")):
    # These logs are lifecycle/guard/microbenchmark validation, and reload logs
    # mix missions and menu loading. They are not single-workload timing runs.
    if path.name.startswith(("reload-", "standalone-", "runtime-", "empty-", "disabled-", "unknown-hash-", "inventory-")):
        continue
    rows = []
    contents = path.read_text(encoding="utf-8-sig", errors="replace")
    startup_worker_info = re.search(r"Runtime workers=(\d+) worker_max=(\d+) logical_cpus=(\d+) fixed_delta_seconds=([\d.]+)", contents)
    worker_info = re.search(r"Runtime settled workers=(\d+) worker_max=(\d+) logical_cpus=(\d+) fixed_delta_seconds=([\d.]+)", contents)
    for line in contents.splitlines():
        if "frames=" not in line or "loop_mean_ms=" not in line:
            continue
        rows.append({k: float(v) for k, v in re.findall(r"(\w+)=(-?\d+(?:\.\d+)?)", line)})
    steady = rows[args.discard_windows:]
    if not steady:
        continue
    frames = sum(r["frames"] for r in steady)
    item = {
        "run": path.stem,
        "verified_mission": identities.get(path.name.removesuffix("-bepinex.txt")),
        "instrumented_methods_or_phases": "Installing sampled profile:" in contents or "PHASE " in contents or "WEAPONS_PROFILE_READY" in contents,
        "windows": len(steady),
        "frames": frames,
        "weighted_loop_mean_ms": sum(r["frames"] * r["loop_mean_ms"] for r in steady) / frames,
        "median_window_loop_mean_ms": statistics.median(r["loop_mean_ms"] for r in steady),
        "weighted_fixed_mean_ms": sum(r["frames"] * r["fixed_mean_ms"] for r in steady) / frames,
        "max_loop_ms": max(r["loop_max_ms"] for r in steady),
        "units_min": min(r["units"] for r in steady),
        "units_max": max(r["units"] for r in steady),
        "heap_final_bytes": steady[-1]["heap_bytes"],
        "gc0": sum(r.get("gc0", 0) for r in steady) if "gc0" in steady[0] else None,
        "workers": int(worker_info[1]) if worker_info else None,
        "worker_max": int(worker_info[2]) if worker_info else None,
        "fixed_delta_seconds": float(worker_info[4]) if worker_info else None,
        "startup_workers": int(startup_worker_info[1]) if startup_worker_info else None,
        "startup_worker_max": int(startup_worker_info[2]) if startup_worker_info else None,
    }
    if args.segment_windows > 0:
        segments = []
        for start in range(0, len(steady), args.segment_windows):
            segment = steady[start:start + args.segment_windows]
            segment_frames = sum(r["frames"] for r in segment)
            segments.append({
                "first_window": start + args.discard_windows + 1,
                "last_window": start + args.discard_windows + len(segment),
                "weighted_loop_mean_ms": sum(r["frames"] * r["loop_mean_ms"] for r in segment) / segment_frames,
                "weighted_fixed_mean_ms": sum(r["frames"] * r["fixed_mean_ms"] for r in segment) / segment_frames,
                "units_min": min(r["units"] for r in segment),
                "units_max": max(r["units"] for r in segment),
                "heap_first_bytes": segment[0]["heap_bytes"],
                "heap_last_bytes": segment[-1]["heap_bytes"],
                "gc0": sum(r.get("gc0", 0) for r in segment) if "gc0" in segment[0] else None,
            })
        item["steady_segments"] = segments
    summary.append(item)
print(json.dumps({"discarded_initial_windows": args.discard_windows, "runs": summary}, indent=2))
