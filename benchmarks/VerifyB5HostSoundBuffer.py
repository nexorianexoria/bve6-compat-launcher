#!/usr/bin/env python3
"""No-BVE regression for bounded x86 host BC_SOUND diagnostics.

The mock ATS cycles brake sound 150 between states. Verify correct original
BC_SOUND data, no on-drive logging, and explicit bounded-buffer drops.
"""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess

from AnalyzeBridgeMetrics import parse_log

EVENT = re.compile(r"^BC_SOUND frame=(\d+) index=(\d+) state=(-?\d+) "
                   r"time=(-?\d+) bc=.* speed=.*$", re.MULTILINE)
SUMMARY = re.compile(r"^BC_SOUND_BUFFER_SUMMARY stored=(\d+) "
                     r"dropped=(\d+) capacity=(\d+)$", re.MULTILINE)


def expected_events(frames: int, cycle: int) -> list[tuple[int, int, int, int]]:
    initial = [(1, index, 0, 0) for index in range(150, 168)]
    changes = [(tick + 1, 150, (tick // cycle) % 2, tick * 16)
               for tick in range(cycle, frames, cycle)]
    return initial + changes


def validate_host_sound_log(text: str, frames: int, cycle: int,
                            expected_profile: bool, capacity: int = 4096) -> dict:
    expected = expected_events(frames, cycle)
    match = SUMMARY.search(text)
    if not match:
        raise ValueError("BC_SOUND_BUFFER_SUMMARY missing")
    stored, dropped, limit = (int(value) for value in match.groups())
    if limit != capacity or stored != min(capacity, len(expected)) or \
            dropped != max(0, len(expected) - capacity):
        raise ValueError(
            f"Incorrect host buffer summary stored={stored} dropped={dropped} "
            f"capacity={limit}, expected={len(expected)}")
    observed = [tuple(int(g) for g in event.groups()) for event in EVENT.finditer(text)]
    if len(observed) != stored or observed != expected[:stored]:
        raise ValueError("BC_SOUND event order, state, time or count changed")
    if text.count("HOST_FINISHED") != 1:
        raise ValueError("Host did not exit cleanly")
    if expected_profile and len(re.findall(r"^HOST_FIRST_ELAPSE ", text, re.M)) != 1:
        raise ValueError("ATS first-Elapse synchronization marker missing")
    if not expected_profile and re.search(r"^HOST_FIRST_ELAPSE ", text, re.M):
        raise ValueError("Unexpected ATS first-Elapse marker with profile off")
    if re.search(r"^(ERROR |UNHANDLED_EXCEPTION)", text, re.M):
        raise ValueError("ATS host error found")
    return {"stored": stored, "dropped": dropped, "events": len(expected)}


def run_case(name: str, frontend: Path, backend: Path, mock_ats: Path,
             harness: Path, folder: Path, frames: int, cycle: int,
             profile: bool, timeout: int) -> dict:
    folder.mkdir(parents=True, exist_ok=False)
    front = folder / "Bve5VehicleCompatibility_.dll"
    host = folder / "Bve5VehicleCompatibility.exe"
    ats = folder / "mock_ats.dll"
    shutil.copyfile(frontend, front)
    shutil.copyfile(backend, host)
    shutil.copyfile(mock_ats, ats)
    logfile = folder / "bridge.log"
    env = os.environ.copy()
    env.update({
        "BVE5COMPAT_ATS": str(ats),
        "BVE5COMPAT_WORKDIR": str(folder),
        "BVE5COMPAT_LOG": str(logfile),
        "BVE5COMPAT_PROFILE": "1" if profile else "0",
        "BVE5COMPAT_TEST_ASSERT_BUFFERED": "1",
        "BVE5COMPAT_TEST_ASSERT_HOST_BC_BUFFERED": "1",
        "BVE5COMPAT_TEST_MOCK_SOUND_CYCLE": str(cycle),
        "BVE5COMPAT_TEST_HARNESS_PAUSE_MS": "",
        "BVE5COMPAT_TEST_HARNESS_PAUSE_FRAME": "",
        "BVE5COMPAT_TEST_HARNESS_CADENCE_US": "",
        "BVE5COMPAT_TEST_MOCK_ATS_SLOW_FRAME": "",
        "BVE5COMPAT_TEST_MOCK_ATS_SLOW_MS": "",
    })
    try:
        proc = subprocess.run(
            [str(harness), str(front), str(frames)],
            cwd=folder, env=env, capture_output=True, text=True,
            errors="replace", timeout=timeout)
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(f"{name}: timeout after {timeout}s") from exc
    (folder / "harness-result.txt").write_text(
        f"exit={proc.returncode}\n{proc.stdout}\n{proc.stderr}\n", encoding="utf-8")
    if (proc.returncode or "FAST_ARRAY_CODEC_OK" not in proc.stdout
            or f"MOCK_ATS_BRIDGE_OK frames={frames}" not in proc.stdout):
        raise RuntimeError(f"{name}: mock bridge failure exit={proc.returncode}")
    text = logfile.read_text(encoding="utf-8", errors="replace")
    data = validate_host_sound_log(text, frames, cycle, profile)
    metrics = parse_log(logfile)
    if metrics is None or metrics["round_frames"] != frames or \
            metrics["host_frames"] != frames or not metrics["valid_pair"]:
        raise RuntimeError(f"{name}: missing ATS roundtrip metrics")
    if profile:
        host_diag = metrics["host_phases"].get("diagnostics")
        if host_diag is None or host_diag["count"] != frames:
            raise RuntimeError(f"{name}: host diagnostic-stage counter mismatch")
    return {"name": name, "frames": frames, "cycle": cycle,
            "profile": profile, **data, "log_path": str(logfile)}


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    for name in ("frontend", "backend", "mock-ats", "harness"):
        p.add_argument("--" + name, required=True, type=Path)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--timeout", type=int, default=90)
    args = p.parse_args()
    for path in (args.frontend, args.backend, args.mock_ats, args.harness):
        if not path.is_file():
            p.error(f"Missing test binary: {path}")
    if args.output.exists():
        p.error("Output exists: refusing overwrite")
    args.output.mkdir(parents=True)
    cases = [("sound-changing-profile", 3000, 7, True),
             ("sound-changing-profile-off", 3000, 7, False),
             ("sound-capacity-overflow", 5000, 1, True)]
    rows = []
    for name, frames, cycle, profile in cases:
        try:
            row = run_case(
                name, args.frontend, args.backend, args.mock_ats, args.harness,
                args.output / name, frames, cycle, profile, args.timeout)
        except (RuntimeError, ValueError, OSError) as exc:
            (args.output / "INCOMPLETE.txt").write_text(
                "Mock sound regression failed. No compatibility conclusion.\n"
                + str(exc), encoding="utf-8")
            raise SystemExit(f"HOST_SOUND_BUFFER_FAILED={name}: {exc}")
        rows.append(row)
        print(f"HOST_SOUND_BUFFER_PASS={name} stored={row['stored']} "
              f"dropped={row['dropped']} expected_events={row['events']}", flush=True)
    lines = [
        "# b5 x86 host brake-sound diagnostics buffer regression",
        "",
        "BVE6 was not launched. The x86 ATS is a synthetic test DLL, not a train vehicle.",
        "",
        "| Case | Synthetic frames | Stored BC_SOUND events | Dropped events |",
        "|---|---:|---:|---:|",
    ]
    for row in rows:
        lines.append(f"| {row['name']} | {row['frames']:,} | "
                     f"{row['stored']:,} | {row['dropped']:,} |")
    lines.extend([
        "",
        "The test harness checked that BC_SOUND lines were absent from the log "
        "while ATS Elapse callbacks were still running; they were written at Dispose.",
        "Sound values were checked on every simulated ATS frame. Every retained "
        "event was compared to the original frame/index/state/time sequence.",
        "The first host Elapse synchronization marker is preserved when profiling is enabled.",
        "Buffer overflow deliberately loses optional diagnostics only and reports "
        "the precise number dropped; ATS output and communication remain intact.",
        "",
        "No real BVE6 FPS, audio playback or original third-party ATS plugin has "
        "been tested by this regression. A gameplay stall reduction is not established.",
        "",
    ])
    report = args.output / "host-sound-buffer-report.md"
    report.write_text("\n".join(lines), encoding="utf-8")
    print(f"HOST_SOUND_BUFFER_OK={report}", flush=True)


if __name__ == "__main__":
    main()