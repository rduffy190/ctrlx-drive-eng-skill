#!/usr/bin/env python3
"""Summarize / export / plot a DriveTool scope capture (output/scope_*.json).

  python3 -I scope_report.py CAPTURE.json                 # per-channel stats as JSON
  python3 -I scope_report.py CAPTURE.json --csv out.csv   # time_ms + one column per channel
  python3 -I scope_report.py CAPTURE.json --png out.png   # stacked plot (needs matplotlib)

Also reads the older OscopeTest files (camelCase keys).
"""
import argparse
import csv
import json
import math
import sys


def load(path):
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    time_us = d.get("time_us") or d.get("timeUs") or []
    channels = [(c.get("signal", f"ch{i + 1}"), c.get("values") or []) for i, c in enumerate(d.get("channels", []))]
    return d, time_us, channels


def stats(time_us, values):
    n = len(values)
    if n == 0:
        return {"samples": 0}
    mean = sum(values) / n
    var = sum((v - mean) ** 2 for v in values) / n
    i_min = min(range(n), key=values.__getitem__)
    i_max = max(range(n), key=values.__getitem__)
    i_trig = min(range(min(n, len(time_us))), key=lambda i: abs(time_us[i])) if time_us else None
    t = (lambda i: round(time_us[i] / 1000.0, 3) if i is not None and i < len(time_us) else None)
    return {
        "samples": n,
        "min": values[i_min], "min_at_ms": t(i_min),
        "max": values[i_max], "max_at_ms": t(i_max),
        "peak_to_peak": values[i_max] - values[i_min],
        "mean": mean,
        "std": math.sqrt(var),
        "rms": math.sqrt(sum(v * v for v in values) / n),
        "at_trigger": values[i_trig] if i_trig is not None else None,
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("capture")
    ap.add_argument("--csv")
    ap.add_argument("--png")
    a = ap.parse_args()

    d, time_us, channels = load(a.capture)
    print(json.dumps({
        "file": a.capture,
        "time_resolution_us": d.get("time_resolution_us") or d.get("timeResolutionUs"),
        "pre_trigger_samples": d.get("pre_trigger_samples") or d.get("preTriggerSamples"),
        "span_ms": [round(time_us[0] / 1000.0, 3), round(time_us[-1] / 1000.0, 3)] if time_us else None,
        "channels": {sig: stats(time_us, vals) for sig, vals in channels},
    }, indent=2))

    if a.csv:
        with open(a.csv, "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["time_ms"] + [sig for sig, _ in channels])
            for i in range(max((len(v) for _, v in channels), default=0)):
                w.writerow([time_us[i] / 1000.0 if i < len(time_us) else ""] + [v[i] if i < len(v) else "" for _, v in channels])
        print(f"wrote {a.csv}", file=sys.stderr)

    if a.png:
        try:
            import matplotlib
            matplotlib.use("Agg")
            import matplotlib.pyplot as plt
        except ImportError:
            print("matplotlib not available; use --csv instead", file=sys.stderr)
            return 1
        t_ms = [t / 1000.0 for t in time_us]
        fig, axes = plt.subplots(len(channels), 1, sharex=True, figsize=(10, 2.4 * max(1, len(channels))), squeeze=False)
        for ax, (sig, vals) in zip(axes[:, 0], channels):
            ax.plot(t_ms[:len(vals)], vals, linewidth=0.8)
            ax.axvline(0, color="grey", linewidth=0.6, linestyle="--")
            ax.set_ylabel(sig, fontsize=8)
            ax.grid(True, linewidth=0.3)
        axes[-1, 0].set_xlabel("time relative to trigger [ms]")
        fig.tight_layout()
        fig.savefig(a.png, dpi=120)
        print(f"wrote {a.png}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
