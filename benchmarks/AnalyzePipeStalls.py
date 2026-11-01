#!/usr/bin/env python3
"""Compare x64 BVE WriteFile stalls and x86 host stdin read waits in Windows QPC.

Stdin read wait INCLUDES ordinary inter-frame idle time. Matching QPC windows
does not prove a pipe buffer stall or process scheduler root cause.
"""
from __future__ import annotations
import argparse
import re
from pathlib import Path

FRONTEND_SEND = re.compile(
    r"^SLOW_PHASE frame=(\d+) stage=send duration_ns=(\d+)"
    r" qpc_start=(\d+) qpc_end=(\d+) qpc_freq=(\d+)"
    r" utc_sample_filetime100ns=\d+$", re.MULTILINE)
HOST_INPUT = re.compile(
    r"^SLOW_HOST_INPUT frame=(\d+) duration_ns=(\d+)"
    r" qpc_start=(\d+) qpc_end=(\d+) qpc_freq=(\d+)"
    r" bytes=(\d+)$", re.MULTILINE)
HOST_SUMMARY = re.compile(
    r"^HOST_INPUT_READ_SUMMARY threshold_ms=(\d+) frames=(\d+)"
    r" avg_ns=(\d+) max_ns=(\d+) stored=(\d+) dropped=(\d+)"
    r" qpc_freq=(\d+)$", re.MULTILINE)


def parse_pipe_waits(text: str) -> tuple[list[dict], list[dict], dict | None]:
    sends = []
    input_waits = []
    for pattern, target in ((FRONTEND_SEND, sends), (HOST_INPUT, input_waits)):
        for match in pattern.finditer(text):
            values = [int(x) for x in match.groups()]
            frame, duration_ns, qpc_start, qpc_end, qpc_freq = values[:5]
            if qpc_freq <= 0 or qpc_start < 0 or qpc_end < qpc_start:
                raise ValueError("Invalid QPC event")
            if abs((qpc_end - qpc_start) * 1e9 / qpc_freq - duration_ns) > 10e6:
                raise ValueError("Event duration and QPC interval disagree")
            item = dict(frame=frame, duration_ns=duration_ns, start=qpc_start,
                        end=qpc_end, freq=qpc_freq)
            if target is input_waits:
                item["bytes"] = values[5]
            target.append(item)
    match = HOST_SUMMARY.search(text)
    summary = None
    if match:
        fields = ["threshold_ms", "frames", "avg_ns", "max_ns",
                  "stored", "dropped", "qpc_freq"]
        summary = dict(zip(fields, (int(x) for x in match.groups())))
        if summary["qpc_freq"] <= 0 or summary["stored"] != len(input_waits):
            raise ValueError("Host input-wait summary does not match its events")
    if len({e["freq"] for e in sends + input_waits}) > 1:
        raise ValueError("QPC frequency mismatch across processes")
    return sends, input_waits, summary


def overlap_ms(front: dict, host: dict) -> float:
    if front["freq"] != host["freq"]:
        raise ValueError("QPC clock frequency mismatch")
    ticks = max(0, min(front["end"], host["end"]) -
                max(front["start"], host["start"]))
    return ticks * 1000.0 / front["freq"]


def compare(sends: list[dict], waits: list[dict]) -> list[dict]:
    by_frame: dict[int, list[dict]] = {}
    for event in waits:
        by_frame.setdefault(event["frame"], []).append(event)
    results = []
    for send in sorted(sends, key=lambda e: e["duration_ns"], reverse=True):
        aligned = by_frame.get(send["frame"], [])
        results.append(dict(
            send=send, matching_input_waits=aligned,
            max_overlap_ms=max((overlap_ms(send, wait) for wait in aligned),
                               default=0.0)))
    return results


def build_report(log_file: Path) -> str:
    text = log_file.read_text(encoding="utf-8", errors="replace")
    sends, waits, summary = parse_pipe_waits(text)
    lines = [
        "# b5 ATS stdin read / WriteFile comparison",
        "",
        "Diagnostic only. The x86 host's blocking std::getline includes normal "
        "time between BVE Elapse calls. It is not pure kernel pipe transport time.",
        "",
        f"Frontend WriteFile events >5ms: {len(sends)}",
        f"Host stdin read waits >=50ms: {len(waits)}",
    ]
    if summary is None:
        lines.append("Host input instrumentation: not available in this older log.")
    else:
        lines.extend([
            f"Host input threshold: {summary['threshold_ms']}ms",
            f"Host input waits measured: {summary['frames']}",
            f"Host input average/max wait: "
            f"{summary['avg_ns']/1e6:.2f}/{summary['max_ns']/1e6:.2f}ms",
            f"Host input log overflow: {summary['dropped']}",
        ])
    lines.extend(["", "## Longest frontend send events", ""])
    if not sends:
        lines.append("No frontend WriteFile event over 5ms in this run.")
    else:
        for result in compare(sends, waits)[:25]:
            send = result["send"]
            lines.append(
                f"- ATS frame {send['frame']}: frontend WriteFile "
                f"{send['duration_ns']/1e6:.2f}ms")
            for host in result["matching_input_waits"]:
                lines.append(
                    f"  - x86 stdin wait for same frame: "
                    f"{host['duration_ns']/1e6:.2f}ms; "
                    f"QPC overlap with frontend WriteFile: "
                    f"{overlap_ms(send,host):.2f}ms; "
                    f"received bytes: {host['bytes']}")
            if not result["matching_input_waits"]:
                lines.append(
                    "  - No >=50ms x86 stdin wait recorded for this frame. "
                    "This does not prove the host was active, nor a scheduling cause.")
    lines.extend([
        "",
        "## Interpretation limits",
        "",
        "- An x86 stdin wait may simply reflect time spent waiting for the "
        "next normal BVE simulation frame.",
        "- Matching frame numbers and QPC ranges can locate overlap; "
        "they do not identify why Windows paused a WriteFile call.",
        "- A missing input wait event only shows there was no recorded >=50ms "
        "getline wait for the same frame; shorter waits are omitted.",
        "- The host log stores at most 512 threshold-crossing waits. "
        "Check dropped before interpreting missing events.",
        "- If the same long stalls recur, OS-level thread scheduling "
        "and pipe tracing may be required. No BVE/ATS/game settings were modified.",
        "",
    ])
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bridge-log", type=Path, required=True)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    result = build_report(args.bridge_log)
    if args.out is not None:
        if args.out.exists():
            parser.error("Refusing to overwrite an existing report")
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(result, encoding="utf-8")
        print(f"PIPE_ANALYSIS_OK={args.out}")
    else:
        print(result)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError) as error:
        raise SystemExit(f"Pipe stall analysis error: {error}")