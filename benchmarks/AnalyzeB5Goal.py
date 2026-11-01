#!/usr/bin/env python3
"""Exploratory b5 analysis: separate rendering streams, ATS timing and matched intervals.

Not proof of causality. PresentMon CPUStartQPC is CPU start, not display time.
Background intervals matched by 30-second bins, fixed deterministic seed.
"""
from __future__ import annotations

import argparse
import bisect
import csv
import math
import random
import statistics
from pathlib import Path

from CorrelateBveStalls import read_events, read_frames


def percentile(values: list[float], pct: float) -> float:
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * pct) - 1)]


def get_busy(csv_path: Path) -> dict[str, dict[str, list[float]]]:
    series: dict[str, dict[str, list[float]]] = {}
    with csv_path.open(encoding="utf-8-sig", newline="") as handle:
        for row in csv.DictReader(handle):
            chain = row.get("SwapChainAddress")
            if not chain:
                continue
            fields = series.setdefault(chain, {"cpu": [], "gpu": []})
            for label, col in (("cpu", "MsCPUBusy"), ("gpu", "MsGPUBusy")):
                try:
                    value = float(row[col])
                except (ValueError, KeyError, TypeError):
                    continue
                if 0 <= value < 10000:
                    fields[label].append(value)
    return series


def is_slow_near(timestamps: list[int], milliseconds: list[float],
                 start: int, duration: int, freq: int,
                 threshold: float, before_ms: float, after_ms: float) -> bool:
    begin = start - round(before_ms / 1000 * freq)
    end = start + duration + round(after_ms / 1000 * freq)
    left = bisect.bisect_left(timestamps, begin)
    right = bisect.bisect_right(timestamps, end)
    return any(value > threshold for value in milliseconds[left:right])


def assess(csv_path: Path, log_path: Path, repeats: int = 500,
           threshold_ms: float = 50.0, before_ms: float = 50.0,
           after_ms: float = 100.0, bin_seconds: float = 30.0) -> str:
    if repeats <= 0:
        raise ValueError("Repetition count must be positive")
    groups = read_frames(csv_path)
    events, overflow = read_events(log_path, allow_empty=True)
    host = [event for event in events if event["stage"] == "host_ats"]
    first = min(rows[0][0] for rows in groups.values())
    last = max(rows[-1][0] for rows in groups.values())
    host = [event for event in host if first <= event["start"] <= event["end"] <= last]
    # A run with no >5ms host ATS events is a valid measurement, not an error.
    lines = [
        "# b5 ATS/FPS matched-window analysis",
        "",
        "Exploratory results; not causal evidence or proof of displayed FPS.",
        "The operating-window swap chains have not been mapped to a specific BVE window.",
        "The test compares ATS stalls against random equal-length intervals sampled",
        "from the same 30-second time bins. Render activity and ATS timing can",
        "share causes (scene complexity, CPU scheduling, etc.).",
        f"ATS host events >5ms inside FPS capture: {len(host)}",
        f"Profiled ATS events dropped: {overflow if overflow is not None else 'unknown'}",
        f"Window: {before_ms:g}ms before / {after_ms:g}ms after each event; "
        f"slow present interval threshold: {threshold_ms:g}ms.",
        f"Matched random comparisons: {repeats} (deterministic seed).",
        "",
    ]
    busy = get_busy(csv_path)
    rng = random.Random(101)
    for (pid, chain), samples in sorted(groups.items()):
        qpc = [value[0] for value in samples]
        ms = [value[1] for value in samples]
        fields = busy.get(chain, {})
        cpus = fields.get("cpu", [])
        gpus = fields.get("gpu", [])
        if not host:
            lines.extend([
                f"## Swap chain {chain} (PID {pid})",
                f"- Frames submitted: {len(ms)}",
                f"- Mean interval: {statistics.mean(ms):.2f}ms",
                f"- 95th/99th interval: {percentile(ms, .95):.2f}/"
                f"{percentile(ms, .99):.2f}ms",
                f"- Max interval: {max(ms):.2f}ms",
                "- No host ATS events >5ms occurred during the FPS capture.",
                "- Matched-window comparison: not applicable.",
                "",
            ])
            continue
        freq = host[0]["freq"]
        actual = sum(is_slow_near(
            qpc, ms, e["start"], e["end"] - e["start"], freq,
            threshold_ms, before_ms, after_ms) for e in host)
        background = []
        for _ in range(repeats):
            hits = 0
            for event in host:
                index = math.floor((event["start"] - first) / (freq * bin_seconds))
                lower = first + round(index * bin_seconds * freq)
                upper = min(last, lower + round(bin_seconds * freq))
                duration = event["end"] - event["start"]
                if upper - duration <= lower:
                    continue
                anchor = rng.randrange(lower, upper - duration)
                hits += is_slow_near(qpc, ms, anchor, duration, freq,
                                     threshold_ms, before_ms, after_ms)
            background.append(hits)
        background.sort()
        lines.extend([
            f"## Swap chain {chain} (PID {pid})",
            f"- Frames submitted: {len(ms)}",
            f"- Mean interval: {statistics.mean(ms):.2f}ms "
            f"(rate equivalent: {1000 / statistics.mean(ms):.1f}/s)",
            f"- 95th/99th interval: {percentile(ms, .95):.2f}/"
            f"{percentile(ms, .99):.2f}ms",
            f"- Max interval: {max(ms):.2f}ms",
            f"- Intervals >33.33ms: {sum(v > 33.33 for v in ms)}",
            f"- Intervals >50ms: {sum(v > 50 for v in ms)}",
            f"- Mean CPU/GPU busy reported by PresentMon: "
            f"{statistics.mean(cpus):.2f}/{statistics.mean(gpus):.2f}ms"
            if cpus and gpus else "- CPU/GPU busy metrics unavailable",
            f"- ATS-event windows with >{threshold_ms:g}ms interval: "
            f"{actual}/{len(host)}",
            f"- Same-time-bin random windows: mean {statistics.mean(background):.1f}/"
            f"{len(host)}; 10-90% range "
            f"{background[math.floor(repeats*.1)]}-{background[min(repeats-1, math.ceil(repeats*.9))]}",
            f"- Fraction of random replicates >= observed "
            f"(plus-one estimate): {(1 + sum(v >= actual for v in background))/(repeats + 1):.3f}",
            "",
        ])
    lines.extend([
        "## Limits",
        "- A render swap chain has not been proven to represent the BVE driving window.",
        "- Close temporal overlap is not proof that ATS caused the long frame interval.",
        "- Overlapping windows from clustered ATS events are not independent observations.",
        "- CPUStartQPC is a CPU-start timestamp; the associated MsBetweenPresents",
        "  interval can relate to other work preceding that timestamp.",
        "- A 30-second-bin-matched exploratory comparison does not control",
        "  instantaneous scene complexity, changes in view, or CPU scheduling.",
        "- One route, one vehicle and one session cannot establish performance gains.",
    ])
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--csv", type=Path, required=True)
    parser.add_argument("--bridge-log", type=Path, required=True)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--repeats", type=int, default=500)
    args = parser.parse_args()
    report = assess(args.csv, args.bridge_log, args.repeats)
    if args.out:
        if args.out.exists():
            parser.error("Report path already exists; refusing overwrite")
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(report, encoding="utf-8")
        print(f"Report saved: {args.out}")
    print(report)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError) as error:
        raise SystemExit(f"Analysis error: {error}")