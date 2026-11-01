#!/usr/bin/env python3
"""Find time overlap between b5 ATS stalls and PresentMon CPU starts in QPC ticks.

This detects timing coincidence, never proves causes or actual displayed FPS.
PresentMon MUST be captured with --qpc_time.
"""
import argparse
import bisect
import csv
from datetime import datetime, timedelta, timezone
from pathlib import Path
import re

EVENT = re.compile(
    r"^SLOW_PHASE frame=(\d+) stage=(encode|send|receive|decode|frame_log|sound_log|post|full)"
    r" duration_ns=(\d+) qpc_start=(-?\d+) qpc_end=(-?\d+)"
    r" qpc_freq=(\d+) utc_sample_filetime100ns=(\d+)$", re.MULTILINE)
HOST_EVENT = re.compile(
    r"^SLOW_HOST_PHASE frame=(\d+) stage=(parse|ats|diagnostics|encode|send)"
    r" duration_ns=(\d+) qpc_start=(-?\d+) qpc_end=(-?\d+)"
    r" qpc_freq=(\d+)$", re.MULTILINE)
FILETIME_EPOCH = datetime(1601, 1, 1, tzinfo=timezone.utc)


def read_events(path, allow_empty=False):
    contents = path.read_text(encoding="utf-8", errors="replace")
    events = []
    for m in EVENT.finditer(contents):
        frame, stage, duration, start, end, freq, ft = m.groups()
        frame, duration, start, end, freq, ft = map(
            int, (frame, duration, start, end, freq, ft))
        if start > end or end < 0 or freq <= 0:
            raise ValueError("Invalid QPC timestamps in ATS log")
        events.append(dict(frame=frame, stage=stage, duration_ns=duration,
                           start=start, end=end, freq=freq, filetime=ft))
    for m in HOST_EVENT.finditer(contents):
        frame, stage, duration, start, end, freq = m.groups()
        frame, duration, start, end, freq = map(int, (frame, duration, start, end, freq))
        if start > end or end < 0 or freq <= 0:
            raise ValueError("Invalid QPC timestamps in backend ATS log")
        events.append(dict(frame=frame, stage="host_" + stage, duration_ns=duration,
                           start=start, end=end, freq=freq, filetime=0))
    match = re.search(r"^SLOW_PHASE_SUMMARY .*?dropped=(\d+)", contents, re.MULTILINE)
    host_match = re.search(r"^SLOW_HOST_PHASE_SUMMARY .*?dropped=(\d+)", contents, re.MULTILINE)
    if not events and not (allow_empty and (match or host_match)):
        raise ValueError("No slow ATS phase events (threshold: 5ms).")
    if len({e["freq"] for e in events}) > 1:
        raise ValueError("Inconsistent QPC frequencies")
    if match or host_match:
        dropped = (int(match.group(1)) if match else 0) + (
            int(host_match.group(1)) if host_match else 0)
    else:
        dropped = None
    return events, dropped


def read_frames(path):
    groups = {}
    with path.open(encoding="utf-8-sig", newline="") as file:
        rows = csv.DictReader(file)
        if not rows.fieldnames or "CPUStartQPC" not in rows.fieldnames:
            raise ValueError("CPUStartQPC missing: run PresentMon with --qpc_time.")
        if "MsBetweenPresents" not in rows.fieldnames:
            raise ValueError("MsBetweenPresents missing from CSV.")
        for row in rows:
            try:
                qpc = int(row["CPUStartQPC"])
                ms = float(row["MsBetweenPresents"])
            except (ValueError, TypeError):
                continue
            if qpc <= 0 or not (0 < ms < 10000):
                continue
            key = (row.get("ProcessID", "?"), row.get("SwapChainAddress", "?"))
            groups.setdefault(key, []).append((qpc, ms))
    for group in groups.values():
        group.sort()
    if not groups:
        raise ValueError("No valid PresentMon frame samples.")
    return groups


def utc_sample(filetime):
    if filetime <= 0:
        return "not-recorded"
    try:
        return (FILETIME_EPOCH + timedelta(microseconds=filetime // 10)
                ).astimezone().isoformat(timespec="milliseconds")
    except (ValueError, OverflowError):
        return "unknown"


def correlate(events, groups, before_ms, after_ms, threshold_ms, swapchain):
    results = []
    for event in sorted(events, key=lambda e: e["duration_ns"], reverse=True):
        freq = event["freq"]
        lower = event["start"] - int(before_ms * freq / 1000)
        upper = event["end"] + int(after_ms * freq / 1000)
        overlaps = []
        for (pid, chain), frames in sorted(groups.items()):
            if swapchain and chain.lower() != swapchain.lower():
                continue
            qpcs = [f[0] for f in frames]
            a = bisect.bisect_left(qpcs, lower)
            b = bisect.bisect_right(qpcs, upper)
            nearby = frames[a:b]
            if nearby:
                overlaps.append(dict(
                    pid=pid, chain=chain, count=len(nearby),
                    slow=sum(ms > threshold_ms for _, ms in nearby),
                    max_ms=max(ms for _, ms in nearby)))
        results.append((event, overlaps))
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--csv", type=Path, required=True)
    parser.add_argument("--bridge-log", type=Path, required=True)
    parser.add_argument("--swapchain", help="Use only a confirmed driving-window swap chain")
    parser.add_argument("--before-ms", type=float, default=50)
    parser.add_argument("--after-ms", type=float, default=100)
    parser.add_argument("--frame-threshold-ms", type=float, default=33.33)
    parser.add_argument("--event-limit", type=int, default=20)
    args = parser.parse_args()
    if args.before_ms < 0 or args.after_ms < 0 or args.event_limit < 1:
        parser.error("Time search windows and event limit must be nonnegative/positive.")
    events, dropped = read_events(args.bridge_log)
    groups = read_frames(args.csv)
    print(f"ATS slow events (frontend and backend combined): {len(events)}; dropped: {dropped if dropped is not None else 'unknown'}")
    print("CPUStartQPC is a CPU frame-start, not a display timestamp.")
    print("MsBetweenPresents is presentation spacing, not necessarily displayed FPS.")
    print(f"Search interval: event -{args.before_ms}ms / +{args.after_ms}ms; "
          f"slow presentation spacing >{args.frame_threshold_ms}ms.")
    print("QPC chains (keep separate):")
    for (pid, chain), frames in sorted(groups.items()):
        print(f"  process={pid}, chain={chain}, samples={len(frames)}")
    capture_first = min(group[0][0] for group in groups.values())
    capture_last = max(group[-1][0] for group in groups.values())
    matches = 0
    for event, overlaps in correlate(
            events, groups, args.before_ms, args.after_ms,
            args.frame_threshold_ms, args.swapchain)[:args.event_limit]:
        print(f"\nATS frame {event['frame']} stage={event['stage']} "
              f"duration={event['duration_ns']/1e6:.2f}ms "
              f"sampled_at={utc_sample(event['filetime'])}")
        if not overlaps:
            if event["end"] < capture_first:
                print("  Outside FPS capture: this ATS event occurred earlier.")
            elif event["start"] > capture_last:
                print("  Outside FPS capture: this ATS event occurred later.")
            else:
                print("  Inside the capture's overall time range, but no matching CPU start.")
        else:
            matches += 1
            for g in overlaps:
                print(f"  process={g['pid']} chain={g['chain']} "
                      f"nearby={g['count']} slow_spacing={g['slow']} "
                      f"max_spacing_ms={g['max_ms']:.2f}")
    if matches == 0:
        print("NO MATCHES: captures may not overlap in time, or be from different boots.")
    if dropped:
        print(f"WARNING: {dropped} ATS slow events overflowed the bounded in-memory list.")
    print("NOTE: temporal coincidence does not establish that ATS caused stutter.")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError) as ex:
        raise SystemExit(f"ERROR: {ex}")