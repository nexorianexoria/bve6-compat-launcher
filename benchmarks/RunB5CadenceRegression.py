#!/usr/bin/env python3
"""Test BVE5 ATS bridge at 60Hz-like cadence with injected offline stalls.

Only a synthetic x64 harness and x86 ATS host run. Never launches BveTs.exe.
The resulting timings are *not* actual render FPS or proof of smoother driving.
"""
from __future__ import annotations

import argparse
import csv
from dataclasses import dataclass
import os
from pathlib import Path
import re
import shutil
import statistics
import subprocess
import time

from AnalyzeBridgeMetrics import parse_log

HOST_INPUT = re.compile(
    r"^SLOW_HOST_INPUT frame=(\d+) duration_ns=(\d+) "
    r"qpc_start=(\d+) qpc_end=(\d+) qpc_freq=(\d+) bytes=(\d+)$", re.M)
HOST_ATS = re.compile(
    r"^SLOW_HOST_PHASE frame=(\d+) stage=ats duration_ns=(\d+) "
    r"qpc_start=(\d+) qpc_end=(\d+) qpc_freq=(\d+)$", re.M)
FRONTEND_PHASE = re.compile(
    r"^SLOW_PHASE frame=(\d+) stage=(receive|frame_log|full) duration_ns=(\d+) "
    r"qpc_start=(\d+) qpc_end=(\d+) qpc_freq=(\d+)", re.M)
TIMING = re.compile(
    r"HARNESS_TIMING frames=(\d+) cadence_us=(\d+) elapsed_ms=(\d+) "
    r"max_elapse_us=(\d+) pause_frame=(\d+) pause_ms=(\d+)")


def slow_ns(text: str, pattern: re.Pattern, frame: int, stage: str | None = None) -> int:
    for match in pattern.finditer(text):
        groups = match.groups()
        if int(groups[0]) != frame:
            continue
        if stage is None:
            return int(groups[1])
        if groups[1] == stage:
            return int(groups[2])
    return 0


def run_case(variant: str, frontend: Path, backend: Path, ats: Path,
             harness: Path, directory: Path, frames: int, cadence_us: int,
             pause_frame: int, pause_ms: int, slow_frame: int,
             slow_ms: int, timeout: int) -> dict:
    directory.mkdir(parents=True, exist_ok=False)
    selected_front = directory / "Bve5VehicleCompatibility_.dll"
    shutil.copyfile(frontend, selected_front)
    shutil.copyfile(backend, directory / "Bve5VehicleCompatibility.exe")
    selected_ats = directory / "mock_ats.dll"
    shutil.copyfile(ats, selected_ats)
    log = directory / "bridge.log"
    env = os.environ.copy()
    env.update({
        "BVE5COMPAT_ATS": str(selected_ats),
        "BVE5COMPAT_LOG": str(log),
        "BVE5COMPAT_WORKDIR": str(directory),
        "BVE5COMPAT_PROFILE": "1",
        "BVE5COMPAT_TEST_ASSERT_BUFFERED": "1" if variant == "buffered" else "",
        "BVE5COMPAT_TEST_ASSERT_HOST_BC_BUFFERED": "1",
        "BVE5COMPAT_TEST_MOCK_SOUND_CYCLE": "",
        "BVE5COMPAT_TEST_HARNESS_CADENCE_US": str(cadence_us),
        "BVE5COMPAT_TEST_HARNESS_PAUSE_FRAME": str(pause_frame),
        "BVE5COMPAT_TEST_HARNESS_PAUSE_MS": str(pause_ms),
        "BVE5COMPAT_TEST_MOCK_ATS_SLOW_FRAME": str(slow_frame),
        "BVE5COMPAT_TEST_MOCK_ATS_SLOW_MS": str(slow_ms),
    })
    started = time.perf_counter()
    try:
        result = subprocess.run(
            [str(harness), str(selected_front), str(frames)],
            cwd=directory, env=env, capture_output=True, text=True,
            errors="replace", timeout=timeout)
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(f"{variant}: harness timed out after {timeout}s") from exc
    wall_s = time.perf_counter() - started
    (directory / "harness-result.txt").write_text(
        f"exit_code={result.returncode}\nwall_seconds={wall_s:.6f}\n"
        f"STDOUT\n{result.stdout}\nSTDERR\n{result.stderr}\n", encoding="utf-8")
    if (result.returncode or "FAST_ARRAY_CODEC_OK" not in result.stdout
            or f"MOCK_ATS_BRIDGE_OK frames={frames}" not in result.stdout):
        raise RuntimeError(f"{variant} roundtrip failed, exit={result.returncode}, "
                           f"detail={directory / 'harness-result.txt'}")
    timing = TIMING.search(result.stdout)
    if not timing or (int(timing[1]), int(timing[2]), int(timing[5]), int(timing[6])) != \
            (frames, cadence_us, pause_frame, pause_ms):
        raise RuntimeError(f"{variant}: harness cadence settings not applied")
    metrics = parse_log(log)
    if metrics is None or not metrics["valid_pair"] or \
            metrics["host_frames"] != frames or metrics["round_frames"] != frames:
        raise RuntimeError(f"{variant}: mismatch in host/bridge frame metrics")
    text = log.read_text(encoding="utf-8", errors="replace")
    if any(line.startswith(("ERROR ", "UNHANDLED_EXCEPTION")) for line in text.splitlines()):
        raise RuntimeError(f"{variant}: ATS reported an error")
    if len(metrics["phases"]) != 9 or any(
            stage["count"] != frames for stage in metrics["phases"].values()):
        raise RuntimeError(f"{variant}: frontend timing stages missing")
    snapshots = sum(line.startswith("FRONTEND_FRAME ")
                    for line in text.splitlines())
    expected_snapshots = 1 + frames // 300
    if snapshots != expected_snapshots:
        raise RuntimeError(f"{variant}: expected {expected_snapshots} frame snapshots, got {snapshots}")
    buffer_lines = [line for line in text.splitlines()
                    if line.startswith("FRONTEND_DIAGNOSTIC_BUFFER_SUMMARY ")]
    if variant == "buffered" and (
            len(buffer_lines) != 1 or "dropped=0" not in buffer_lines[0]):
        raise RuntimeError("Buffered diagnostic data lost or summary is missing")
    if variant == "legacy" and buffer_lines:
        raise RuntimeError("Legacy bridge unexpectedly has buffered diagnostics")

    actual_ats_ns = slow_ns(text, HOST_ATS, slow_frame + 1)
    actual_frontend_receive_ns = slow_ns(text, FRONTEND_PHASE, slow_frame + 1, "receive")
    actual_input_ns = slow_ns(text, HOST_INPUT, pause_frame + 1)
    # Windows Sleep can overshoot. Validate minimum injected time with margin.
    if actual_ats_ns < (slow_ms - 12) * 1_000_000:
        raise RuntimeError(
            f"{variant}: injected {slow_ms}ms ATS delay missing at frame {slow_frame + 1}")
    if actual_frontend_receive_ns < (slow_ms - 12) * 1_000_000:
        raise RuntimeError(f"{variant}: ATS pause missing from frontend receive stage")
    if actual_input_ns < (pause_ms - 15) * 1_000_000:
        raise RuntimeError(f"{variant}: harness-side pause did not appear as stdin wait")
    if cadence_us and int(timing[3]) < (
            (frames - 1) * cadence_us / 1000.0 * 0.8):
        raise RuntimeError(f"{variant}: harness did not maintain paced elapsed time")
    phase = metrics["phases"]
    return {
        "variant": variant,
        "frames": frames,
        "cadence_us": cadence_us,
        "wall_seconds": round(wall_s, 4),
        "harness_elapsed_ms": int(timing[3]),
        "max_elapse_us": int(timing[4]),
        "ats_pause_frame": slow_frame + 1,
        "ats_pause_ms_measured": round(actual_ats_ns / 1e6, 3),
        "frontend_receive_ms": round(actual_frontend_receive_ns / 1e6, 3),
        "input_idle_frame": pause_frame + 1,
        "input_idle_ms_measured": round(actual_input_ns / 1e6, 3),
        "frame_log_mean_us": round(phase["frame_log"]["avg_ns"] / 1000.0, 4),
        "frame_log_max_us": round(phase["frame_log"]["max_ns"] / 1000.0, 3),
        "frame_log_over_5ms": phase["frame_log"]["over_5ms"],
        "bridge_full_mean_us": round(phase["full"]["avg_ns"] / 1000.0, 4),
        "diagnostic_snapshots": snapshots,
    }


def write_report(rows: list[dict], output: Path, cadence_us: int,
                 pause_frame: int, pause_ms: int,
                 slow_frame: int, slow_ms: int) -> None:
    with (output / "cadence-regression-results.csv").open("w", encoding="utf-8", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    lines = [
        "# BVE6 b5 mock ATS: paced frame / injected stall regression",
        "",
        "**BVE6 was NOT launched. No real vehicle, route, GPU rendering or FPS was tested.**",
        "",
        f"- Fixed nominal cadence: {cadence_us} microseconds between frames",
        f"- ATS callback injected pause: zero-based frame {slow_frame}, requested {slow_ms}ms",
        f"- Harness (game thread) injected pause: zero-based frame {pause_frame}, requested {pause_ms}ms",
        "- The built-in mock ATS also includes its previous 25ms pause at zero-based frame 123.",
        "- All ATS handle/panel/sound values are asserted for every synthetic Elapse call.",
        "",
        "| Variant | Frames | Actual wall (s) | ATS pause observed (ms) | "
        "Host stdin wait (ms) | Frontend frame_log avg/max (us) |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for row in rows:
        lines.append(
            f"| {row['variant']} | {row['frames']} | {row['wall_seconds']:.3f} | "
            f"{row['ats_pause_ms_measured']:.2f} | "
            f"{row['input_idle_ms_measured']:.2f} | "
            f"{row['frame_log_mean_us']:.3f} / {row['frame_log_max_us']:.2f} |"
        )
    lines.extend([
        "",
        "## Confirmed checks", "",
        "- The simulated ATS wait was recorded both as host ATS time and "
        "frontend receive time for the same frame.",
        "- The simulated driver/harness pause was recorded as a long wait "
        "for the *next* x86 host stdin message, not as a slow ATS callback.",
        "- All frontend and backend roundtrips completed; all periodic diagnostic "
        "snapshots were present after Dispose.",
        "",
        "## Limitations", "",
        "- The test runs at a nominal 60Hz-like cadence but cannot reproduce "
        "actual game scene complexity, OS/GPU thread behavior or BVE rendering.",
        "- Windows scheduler wakeups and the host ATS 25ms baseline pause "
        "can affect individual timings.",
        "- Both bridge variants are isolated per run. A single paired run "
        "does not establish general performance superiority.",
        "- Real FPS improvement or elimination of the 1.19-second game stall "
        "remains UNVERIFIED; this test is an offline regression gate.",
        "",
    ])
    (output / "cadence-regression-report.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--legacy-frontend", type=Path, required=True)
    p.add_argument("--buffered-frontend", type=Path, required=True)
    p.add_argument("--backend", type=Path, required=True)
    p.add_argument("--mock-ats", type=Path, required=True)
    p.add_argument("--harness", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--frames", type=int, default=600)
    p.add_argument("--cadence-us", type=int, default=16667)
    p.add_argument("--pause-frame", type=int, default=400)
    p.add_argument("--pause-ms", type=int, default=120)
    p.add_argument("--slow-frame", type=int, default=450)
    p.add_argument("--slow-ms", type=int, default=85)
    p.add_argument("--timeout", type=int, default=90)
    args = p.parse_args()
    if (args.frames < 501 or args.frames > 10000 or
        not 10000 <= args.cadence_us <= 50000 or
        not 0 <= args.pause_frame < args.frames or
        not 0 <= args.slow_frame < args.frames or
        args.pause_frame == args.slow_frame or
        args.pause_ms < 50 or args.pause_ms > 500 or
        args.slow_ms < 50 or args.slow_ms > 500):
        p.error("Invalid cadence/stall test bounds")
    for path in (args.legacy_frontend, args.buffered_frontend,
                 args.backend, args.mock_ats, args.harness):
        if not path.is_file():
            p.error(f"Required test binary not found: {path}")
    if args.output.exists():
        p.error("Refusing to overwrite existing test folder")
    args.output.mkdir(parents=True)
    results = []
    for variant, front in (("legacy", args.legacy_frontend),
                           ("buffered", args.buffered_frontend)):
        print(f"PACED_START={variant}", flush=True)
        try:
            row = run_case(variant, front, args.backend, args.mock_ats, args.harness,
                           args.output / variant, args.frames, args.cadence_us,
                           args.pause_frame, args.pause_ms, args.slow_frame,
                           args.slow_ms, args.timeout)
        except (RuntimeError, OSError) as exc:
            (args.output / "INCOMPLETE.txt").write_text(
                "Paced regression failed. No performance conclusion.\n" + str(exc) + "\n",
                encoding="utf-8")
            raise SystemExit("PACED_REGRESSION_FAILED: " + str(exc))
        results.append(row)
        print(f"PACED_PASS={variant} ATS={row['ats_pause_ms_measured']:.2f}ms "
              f"host_idle={row['input_idle_ms_measured']:.2f}ms "
              f"frame_log_avg={row['frame_log_mean_us']:.3f}us", flush=True)
    write_report(results, args.output, args.cadence_us,
                 args.pause_frame, args.pause_ms, args.slow_frame, args.slow_ms)
    print(f"PACED_REGRESSION_OK={args.output / 'cadence-regression-report.md'}", flush=True)


if __name__ == "__main__":
    main()