#!/usr/bin/env python3
"""Summarize existing bridge timing logs without modifying game files.

Usage:
    python AnalyzeBridgeMetrics.py --directory "<launcher working data>" --limit 10

The ATS callback and frontend roundtrip values come from different timing
scopes. Their average difference is a diagnostic estimate, not a proven
transport-only cost. A run's metrics cannot be matched to a 15-second FPS
capture unless the measurement windows are explicitly synchronized.
"""
from __future__ import annotations

import argparse
import re
from pathlib import Path

HOST = re.compile(r"METRICS host_elapse_frames=(\d+) avg_us=(\d+) max_us=(\d+)")
ROUNDTRIP = re.compile(r"METRICS bridge_elapse_frames=(\d+) avg_us=(\d+) max_us=(\d+)")
MODULE = re.compile(r"MODULE stage=(?:Load|Initialize) file=([^\r\n]+)")
PHASE = re.compile(
    r"^METRICS bridge_phase stage=(encode|send|receive|decode|core|frame_log|sound_log|post|full)"
    r" count=(\d+) avg_ns=(\d+) max_ns=(\d+)"
    r" over_1ms=(\d+) over_5ms=(\d+) over_16ms=(\d+) over_33ms=(\d+)$",
    re.MULTILINE,
)

HOST_PHASE = re.compile(
    r"^METRICS host_phase stage=(parse|ats|diagnostics|encode|send)"
    r" count=(\d+) avg_ns=(\d+) max_ns=(\d+) over_5ms=(\d+)$",
    re.MULTILINE,
)


def parse_log(path: Path) -> dict[str, object] | None:
    data = path.read_text(encoding="utf-8", errors="replace")
    host_entries = HOST.findall(data)
    round_entries = ROUNDTRIP.findall(data)
    if not host_entries or not round_entries:
        return None
    count_host, avg_host, max_host = (int(s) for s in host_entries[-1])
    count_round, avg_round, max_round = (int(s) for s in round_entries[-1])
    modules = [name for name in MODULE.findall(data) if name.lower().endswith(".dll")]
    phases = {}
    for match in PHASE.finditer(data):
        phase, count, avg_ns, max_ns, over_1ms, over_5ms, over_16ms, over_33ms = match.groups()
        phases[phase] = dict(
            count=int(count), avg_ns=int(avg_ns), max_ns=int(max_ns),
            over_1ms=int(over_1ms), over_5ms=int(over_5ms),
            over_16ms=int(over_16ms), over_33ms=int(over_33ms),
        )
    host_phases = {}
    for m in HOST_PHASE.finditer(data):
        stage, count, avg_ns, max_ns, over_5ms = m.groups()
        host_phases[stage] = {
            "count": int(count), "avg_ns": int(avg_ns),
            "max_ns": int(max_ns), "over_5ms": int(over_5ms)
        }
    return {
        "run": path.parent.name,
        "module": modules[-1] if modules else "(unknown)",
        "host_frames": count_host,
        "round_frames": count_round,
        "host_avg_us": avg_host,
        "round_avg_us": avg_round,
        "delta_avg_us": avg_round - avg_host,
        "host_max_us": max_host,
        "round_max_us": max_round,
        "valid_pair": count_host == count_round,
        "phases": phases,
        "host_phases": host_phases,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--limit", type=int, default=12)
    args = parser.parse_args()
    if not args.directory.is_dir():
        parser.error("Directory not found")
    if args.limit < 1:
        parser.error("limit must be positive")
    entries = []
    for path in args.directory.glob("run-*/bridge.log"):
        result = parse_log(path)
        if result is not None:
            entries.append(result)
    entries.sort(key=lambda item: str(item["run"]), reverse=True)
    print("Note: averages are full-session metrics, not synchronized PresentMon frames.")
    print("Difference = frontend roundtrip mean - backend ATS Elapse mean; not purely IPC.")
    print("Negative differences or mismatched frame counts should be investigated.")
    print("run | module | frames | ATS avg us | bridge avg us | difference us | max ATS/bridge us | match")
    for e in entries[:args.limit]:
        print(
            f'{e["run"]} | {e["module"]} | {e["host_frames"]} | '
            f'{e["host_avg_us"]} | {e["round_avg_us"]} | {e["delta_avg_us"]} | '
            f'{e["host_max_us"]}/{e["round_max_us"]} | '
            f'{"yes" if e["valid_pair"] else "NO"}'
        )
    print(f"Runs with both metrics: {len(entries)}")
    for entry in entries[:args.limit]:
        phases = entry["phases"]
        if not phases:
            continue
        print(f"\nPhases for {entry['run']} (means; microseconds; opt-in b5 data):")
        print("stage | count | mean us | max us | >1ms | >5ms | >16ms | >33ms")
        for stage in ("encode", "send", "receive", "decode", "core", "frame_log", "sound_log", "post", "full"):
            metric = phases.get(stage)
            if metric is None:
                print(f"{stage} | missing")
                continue
            print(
                f"{stage} | {metric['count']} | "
                f"{metric['avg_ns']/1000:.2f} | {metric['max_ns']/1000:.2f} | "
                f"{metric['over_1ms']} | {metric['over_5ms']} | "
                f"{metric['over_16ms']} | {metric['over_33ms']}"
            )
        counts = [m["count"] for m in phases.values()]
        if len(phases) not in (5, 9) or len(set(counts)) != 1 or counts[0] > entry["round_frames"]:
            print("WARNING: phase data is incomplete or inconsistent; verify run logs.")
        print("receive includes event setup, pipe read/wait and buffering; not pure IPC wait.")
        if entry["host_phases"]:
            print("Host stages (means in microseconds, host ATS and input/output stages):")
            print("stage | count | mean us | max us | >5ms")
            for stage in ("parse", "ats", "diagnostics", "encode", "send"):
                m = entry["host_phases"].get(stage)
                if m:
                    print(f"{stage} | {m['count']} | {m['avg_ns']/1000:.2f} | "
                          f"{m['max_ns']/1000:.2f} | {m['over_5ms']}")
                else:
                    print(f"{stage} | missing")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())