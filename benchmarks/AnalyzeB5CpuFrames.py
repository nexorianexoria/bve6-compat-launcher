#!/usr/bin/env python3
"""Summarize CPU/GPU busy and frame intervals for each distinct PresentMon swapchain.

The CPU/GPU busy values are PresentMon estimates. They do not establish which
swapchain is the BVE cab window or prove which CPU subsystem caused a stall.
"""
from __future__ import annotations
import argparse
import csv
import math
import statistics
from collections import defaultdict
from pathlib import Path


def percentile(values: list[float], q: float) -> float:
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, math.ceil(len(ordered) * q) - 1)]


def parse_csv(csv_path: Path) -> dict[tuple[str, str], list[dict]]:
    streams: dict[tuple[str, str], list[dict]] = defaultdict(list)
    with csv_path.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        needed = {"ProcessID", "SwapChainAddress", "CPUStartQPC",
                  "MsBetweenPresents", "MsCPUBusy", "MsGPUBusy"}
        if not needed.issubset(set(reader.fieldnames or ())):
            raise ValueError("Missing required PresentMon columns: " +
                             ", ".join(sorted(needed - set(reader.fieldnames or ()))))
        for row in reader:
            try:
                timestamp = int(row["CPUStartQPC"])
                interval, cpu, gpu = (
                    float(row[name]) for name in
                    ("MsBetweenPresents", "MsCPUBusy", "MsGPUBusy")
                )
            except (ValueError, TypeError):
                continue
            if not (timestamp > 0 and 0 < interval < 10000 and
                    0 <= cpu < 10000 and 0 <= gpu < 10000 and
                    math.isfinite(interval) and math.isfinite(cpu) and math.isfinite(gpu)):
                continue
            streams[(row["ProcessID"], row["SwapChainAddress"])].append({
                "qpc": timestamp, "interval": interval, "cpu": cpu, "gpu": gpu,
            })
    if not streams:
        raise ValueError("No valid PresentMon CPU/GPU frames")
    for frames in streams.values():
        frames.sort(key=lambda x: x["qpc"])
    return dict(streams)


def report(csv_path: Path, qpc_begin: int = 0, qpc_frequency: int = 10000000) -> str:
    if qpc_frequency <= 0:
        raise ValueError("QPC frequency must be positive")
    streams = parse_csv(csv_path)
    lines = [
        "# b5 PresentMon CPU/GPU frame-busy analysis", "",
        "Each swap chain is treated separately; none has been mapped to the "
        "BVE driving window.",
        "MsCPUBusy and MsGPUBusy are PresentMon observations, not proof "
        "that the CPU or GPU caused an individual long interval.",
        "",
    ]
    for (pid, chain), frames in sorted(streams.items()):
        intervals = [f["interval"] for f in frames]
        cpus = [f["cpu"] for f in frames]
        gpus = [f["gpu"] for f in frames]
        slow = [f for f in frames if f["interval"] > 50.0]
        lines += [
            f"## PID {pid} / swap chain {chain}", "",
            f"- Frames: {len(frames)}",
            f"- Captured swap-chain QPC span: "
            f"{(frames[-1]['qpc'] - frames[0]['qpc']) / qpc_frequency:.2f}s",
            f"- Mean interval / rate equivalent: "
            f"{statistics.mean(intervals):.2f}ms / "
            f"{1000 / statistics.mean(intervals):.1f} presents/s",
            f"- Interval P95/P99/max: {percentile(intervals,.95):.2f}/"
            f"{percentile(intervals,.99):.2f}/{max(intervals):.2f}ms",
            f"- CPU busy mean/P95/max: {statistics.mean(cpus):.2f}/"
            f"{percentile(cpus,.95):.2f}/{max(cpus):.2f}ms",
            f"- GPU busy mean/P95/max: {statistics.mean(gpus):.2f}/"
            f"{percentile(gpus,.95):.2f}/{max(gpus):.2f}ms",
            f"- Frames with interval >33.33ms: "
            f"{sum(f['interval'] > 33.33 for f in frames)}",
            f"- Frames with interval >50ms: {len(slow)}",
        ]
        if qpc_begin:
            lead = (frames[0]["qpc"] - qpc_begin) / qpc_frequency
            lines.append(f"- First recorded frame after capture start: {lead:.2f}s")
        if slow:
            lines.extend(["", "Long-interval frames (>50ms), largest first "
                          "(not necessarily displayed frames):", ""])
            for f in sorted(slow, key=lambda x: x["interval"], reverse=True)[:10]:
                elapsed = ((f["qpc"] - qpc_begin) / qpc_frequency
                           if qpc_begin else None)
                location = (f"elapsed={elapsed:.2f}s, " if elapsed is not None else "")
                lines.append(
                    f"- {location}interval={f['interval']:.2f}ms, "
                    f"CPU busy={f['cpu']:.2f}ms, GPU busy={f['gpu']:.2f}ms")
        lines.append("")
    lines.extend([
        "## Limitations", "",
        "- CPU/GPU busy is not a stack trace. Which BVE, ATS or OS function "
        "consumed the time cannot be identified from this CSV alone.",
        "- CPUStartQPC and MsBetweenPresents are not the exact moment "
        "the monitor displayed an image.",
        "- CPU and GPU busy may overlap; do not add them as independent delays.",
        "- The script does not modify BVE, the ATS DLL or Windows settings.",
        "",
    ])
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--csv", required=True, type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--qpc-start", type=int, default=0)
    parser.add_argument("--qpc-frequency", type=int, default=10000000)
    args = parser.parse_args()
    result = report(args.csv, args.qpc_start, args.qpc_frequency)
    if args.out:
        if args.out.exists():
            parser.error("Output exists: refusing overwrite")
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(result, encoding="utf-8")
        print("CPU_GPU_ANALYSIS_OK=" + str(args.out))
    else:
        print(result)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError) as exc:
        raise SystemExit("CPU/GPU analysis error: " + str(exc))