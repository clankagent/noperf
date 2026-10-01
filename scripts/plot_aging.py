"""Plot completed aggregate runs; no identical-workload or causal-gain claim."""
import argparse
import re
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

parser = argparse.ArgumentParser()
parser.add_argument("evidence", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()

runs = [
    ("escalation-baseline-long-13", "Optimizers disabled", "#20639b"),
    ("escalation-all-long-06", "All three enabled", "#b35c19"),
]
fig, axes = plt.subplots(2, 1, figsize=(11.5, 7.6), sharex=True,
                         gridspec_kw={"height_ratios": [1.45, 1]})
fig.patch.set_facecolor("white")
for name, label, color in runs:
    contents = (args.evidence / (name + "-bepinex.txt")).read_text(encoding="utf-8-sig")
    rows = []
    for line in contents.splitlines():
        if "frames=" in line and "loop_mean_ms=" in line:
            rows.append({key: float(value) for key, value in re.findall(r"(\w+)=(-?\d+(?:\.\d+)?)", line)})
    steady = rows[4:]
    if not steady:
        raise ValueError("No steady telemetry for " + name)
    frame_total = sum(row["frames"] for row in steady)
    mean = sum(row["frames"] * row["loop_mean_ms"] for row in steady) / frame_total
    x = range(5, len(rows) + 1)
    axes[0].plot(x, [row["loop_mean_ms"] for row in steady], color=color,
                 linewidth=1.8, label=f"{label} · frame-weighted mean {mean:.2f} ms")
    axes[1].plot(x, [row["units"] for row in steady], color=color, linewidth=1.8, label=label)

for axis in axes:
    axis.set_facecolor("#fafafa")
    axis.grid(axis="y", color="#dde2e6", linewidth=.7)
    axis.spines[["top", "right"]].set_visible(False)
    axis.spines[["left", "bottom"]].set_color("#c7cdd3")
    axis.tick_params(labelsize=10, colors="#343b43")
    axis.margins(x=.01)
axes[0].set_ylim(bottom=0)
axes[0].set_ylabel("Mean loop work per game frame (ms)", fontsize=11)
axes[0].legend(loc="upper left", frameon=True, facecolor="white", edgecolor="#dde2e6", fontsize=10)
axes[1].set_ylabel("Active registered units", fontsize=11)
axes[1].set_xlabel("Reporting window number · nominal 30-second interval", fontsize=11, labelpad=9)
fig.suptitle("Escalation aging in two completed one-hour runs", x=.085, y=.96,
             ha="left", fontsize=17, fontweight="bold", color="#182733")
fig.text(.085, .915, "Different combat workloads; this pair does not establish a causal performance change.",
         ha="left", fontsize=11, color="#4b5563")
fig.text(.085, .032,
         "Four startup windows excluded. Loop work = update + accumulated fixed steps + late update; frame pacing excluded.\n"
         "Unit counts show workload differences. Window numbering is not an exact elapsed-time axis.",
         fontsize=9, color="#4b5563", ha="left", va="bottom")
fig.subplots_adjust(left=.085, right=.975, top=.865, bottom=.16, hspace=.18)
args.output.parent.mkdir(parents=True, exist_ok=True)
fig.savefig(args.output, dpi=170, facecolor="white")
plt.close(fig)
print("AGING_PLOT_SAVED " + str(args.output))
