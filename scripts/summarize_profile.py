"""Summarize complete diagnostics windows; observer and sampling limits apply."""
import argparse
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("log", type=Path)
parser.add_argument("--discard-windows", type=int, default=4)
parser.add_argument("--segment-windows", type=int, default=20)
args = parser.parse_args()
if args.discard_windows < 0 or args.segment_windows < 1:
    parser.error("Discard count must be nonnegative and segment length positive")

blocks = []
current = {"phases": {}, "methods": {}}
for line in args.log.read_text(encoding="utf-8-sig").splitlines():
    phase = re.search(r"PHASE (\S+) calls=(\d+) mean_ms=([\d.]+) max_ms=([\d.]+)", line)
    if phase:
        current["phases"][phase[1]] = {
            "calls": int(phase[2]), "mean_ms": float(phase[3]), "max_ms": float(phase[4])}
    method = re.search(r"PROFILE (\S+) calls=(\d+) sampled=(\d+)(.*)", line)
    if method:
        metrics = {key: float(value) for key, value in re.findall(r"(\w+)=([\d.]+)", method[4])}
        current["methods"][method[1]] = {
            "calls": int(method[2]), "samples": int(method[3]), **metrics}
    frame = re.search(r"frames=(\d+) loop_mean_ms=([\d.]+).*fixed_mean_ms=([\d.]+)", line)
    if frame:
        current.update(frames=int(frame[1]), loop_mean_ms=float(frame[2]),
                       fixed_mean_ms=float(frame[3]), window=len(blocks) + 1)
        blocks.append(current)
        current = {"phases": {}, "methods": {}}


def summarize(rows):
    frames = sum(row["frames"] for row in rows)
    if frames == 0:
        raise ValueError("No complete steady telemetry frames")
    result = {"first_window": rows[0]["window"], "last_window": rows[-1]["window"],
              "windows": len(rows), "game_frames": frames,
              "loop_mean_ms_per_game_frame": sum(row["frames"] * row["loop_mean_ms"] for row in rows) / frames,
              "accumulated_fixed_mean_ms_per_game_frame": sum(row["frames"] * row["fixed_mean_ms"] for row in rows) / frames,
              "phases": {}, "methods": {}}
    for label in sorted({key for row in rows for key in row["phases"]}):
        values = [row["phases"][label] for row in rows if label in row["phases"]]
        calls = sum(value["calls"] for value in values)
        elapsed = sum(value["calls"] * value["mean_ms"] for value in values)
        result["phases"][label] = {
            "reported_windows": len(values), "calls": calls,
            "calls_per_game_frame": calls / frames,
            "weighted_mean_ms_per_invocation": elapsed / calls if calls else None,
            "observed_ms_per_game_frame": elapsed / frames,
            "max_ms": max(value["max_ms"] for value in values)}
    for label in sorted({key for row in rows for key in row["methods"]}):
        values = [row["methods"][label] for row in rows if label in row["methods"]]
        timed = [value for value in values if "estimated_inclusive_ms" in value]
        result["methods"][label] = {
            "reported_windows": len(values), "timed_windows": len(timed),
            "calls": sum(value["calls"] for value in values),
            "samples": sum(value["samples"] for value in values),
            "calls_without_window_estimate": sum(value["calls"] for value in values if "estimated_inclusive_ms" not in value),
            "rough_observed_inclusive_ms_per_game_frame": sum(value["estimated_inclusive_ms"] for value in timed) / frames if timed else None}
    return result


steady = blocks[args.discard_windows:]
if not steady:
    parser.error("No complete steady telemetry windows after exclusions")
summary = {"input": args.log.name, "discarded_initial_windows": args.discard_windows,
           "limits": "Instrumented observations, not an optimizer comparison. Inclusive method estimates overlap. Fixed-stride sampling can alias workload order. Missing window estimates are unknown, not measured zero. Phase timings cover selected phases, not the full loop. Window numbers are nominal intervals, not exact elapsed time.",
           "steady": summarize(steady),
           "segments": [summarize(steady[start:start + args.segment_windows])
                        for start in range(0, len(steady), args.segment_windows)]}
print(json.dumps(summary, indent=2))
