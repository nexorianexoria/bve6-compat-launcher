#!/usr/bin/env python3
"""BVE-free, non-admin x64 bridge A/B benchmark using the same x86 mock ATS.

This benchmarks the ATS bridge under a synthetic input stream. It does not
launch BVE and cannot establish real driving FPS improvements.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import time

from AnalyzeBridgeMetrics import parse_log


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def check_files(paths: list[Path]) -> None:
    for path in paths:
        if not path.is_file():
            raise ValueError(f"Required test binary missing: {path}")


def run_variant(label: str, source: Path, backend: Path, mock_ats: Path,
                harness: Path, output: Path, frames: int, timeout: int) -> dict:
    folder = output / label
    folder.mkdir(parents=True, exist_ok=False)
    target_frontend = folder / "Bve5VehicleCompatibility_.dll"
    target_backend = folder / "Bve5VehicleCompatibility.exe"
    target_mock = folder / "mock_ats.dll"
    shutil.copyfile(source, target_frontend)
    shutil.copyfile(backend, target_backend)
    shutil.copyfile(mock_ats, target_mock)
    log = folder / "bridge.log"
    env = os.environ.copy()
    env.update({
        "BVE5COMPAT_ATS": str(target_mock),
        "BVE5COMPAT_WORKDIR": str(folder),
        "BVE5COMPAT_LOG": str(log),
        "BVE5COMPAT_PROFILE": "1",
        "BVE5COMPAT_TEST_HARNESS_PAUSE_MS": "",
        "BVE5COMPAT_TEST_HARNESS_PAUSE_FRAME": "",
        "BVE5COMPAT_TEST_HARNESS_CADENCE_US": "",
        "BVE5COMPAT_TEST_MOCK_ATS_SLOW_FRAME": "",
        "BVE5COMPAT_TEST_MOCK_ATS_SLOW_MS": "",
        "BVE5COMPAT_TEST_MOCK_SOUND_CYCLE": "",
        "BVE5COMPAT_TEST_ASSERT_HOST_BC_BUFFERED": "",
        "BVE5COMPAT_TEST_ASSERT_BUFFERED": "",
    })
    started = time.perf_counter()
    try:
        result = subprocess.run(
            [str(harness), str(target_frontend), str(frames)],
            cwd=folder,
            env=env,
            capture_output=True,
            text=True,
            errors="replace",
            timeout=timeout,
        )
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(f"{label} timed out after {timeout}s. Check {log}") from exc
    wall_s = time.perf_counter() - started
    (folder / "harness-output.txt").write_text(
        f"exit_code={result.returncode}\n"
        f"wall_elapsed_s={wall_s:.6f}\n"
        f"STDOUT\n{result.stdout}\nSTDERR\n{result.stderr}\n", encoding="utf-8")
    if (result.returncode != 0 or "FAST_ARRAY_CODEC_OK" not in result.stdout
            or "MOCK_ATS_BRIDGE_OK" not in result.stdout):
        raise RuntimeError(f"{label} mock roundtrip failed: exit={result.returncode}. "
                           f"See {folder / 'harness-output.txt'}")
    parsed = parse_log(log)
    if parsed is None or parsed["round_frames"] != frames or \
            parsed["host_frames"] != frames or not parsed["valid_pair"]:
        raise RuntimeError(f"{label} has missing or mismatched ATS metrics: {log}")
    phases = parsed["phases"]
    if len(phases) != 9 or any(metric["count"] != frames for metric in phases.values()):
        raise RuntimeError(f"{label} has incomplete phase metrics: {log}")
    raw = log.read_text(encoding="utf-8", errors="replace")
    if any(line.startswith(("ERROR ", "UNHANDLED_EXCEPTION")) for line in raw.splitlines()):
        raise RuntimeError(f"{label} bridge reported an error: {log}")
    snapshots = sum(1 for line in raw.splitlines()
                    if line.startswith("FRONTEND_FRAME "))
    sounds = sum(1 for line in raw.splitlines()
                 if line.startswith("FRONTEND_SOUND_RETURN "))
    expected = 1 + frames // 300
    if snapshots != expected:
        raise RuntimeError(f"{label} lost FRONTEND_FRAME snapshots: "
                           f"expected={expected} recorded={snapshots}")
    diagnostic_lines = [line for line in raw.splitlines()
                        if line.startswith("FRONTEND_DIAGNOSTIC_BUFFER_SUMMARY ")]
    if label.endswith("buffered"):
        if len(diagnostic_lines) != 1 or "dropped=0" not in diagnostic_lines[0]:
            raise RuntimeError(f"{label} missing/overflowed in-memory diagnostic buffer")
    else:
        if diagnostic_lines:
            raise RuntimeError(f"{label} unexpectedly uses buffered diagnostics")
    return {
        "variant": "buffered" if label.endswith("buffered") else "legacy",
        "run_name": label,
        "frames": frames,
        "wall_seconds": wall_s,
        "roundtrip_mean_us": parsed["round_avg_us"],
        "host_ats_mean_us": parsed["host_avg_us"],
        "frame_log_mean_us": phases["frame_log"]["avg_ns"] / 1000.0,
        "frame_log_max_us": phases["frame_log"]["max_ns"] / 1000.0,
        "frame_log_over_5ms": phases["frame_log"]["over_5ms"],
        "bridge_full_mean_us": phases["full"]["avg_ns"] / 1000.0,
        "bridge_full_max_us": phases["full"]["max_ns"] / 1000.0,
        "snapshots": snapshots,
        "sounds": sounds,
        "log_path": str(log),
    }


def prepare_order(pairs: int) -> list[tuple[int, str]]:
    # Reverse the per-pair ordering to reduce thermal/cache/order bias.
    return [(index, variant)
            for index in range(1, pairs + 1)
            for variant in (("legacy", "buffered") if index % 2 else
                            ("buffered", "legacy"))]


def write_report(rows: list[dict], hashes: dict, output: Path, frames: int,
                 pairs: int) -> None:
    output.mkdir(parents=True, exist_ok=True)
    fields = list(rows[0])
    with (output / "offline-ab-results.csv").open("w", encoding="utf-8", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)
    lines = [
        "# BVE6 b5 bridge: offline periodic diagnostics A/B",
        "",
        "**Synthetic ATS bridge benchmark only; BVE6 was not launched.**",
        "Timing is affected by host scheduling, cold caches, antivirus scanning, "
        "machine activity, and the simulated ATS workload. "
        "This is not a measured change in actual BVE FPS.",
        "",
        f"- Frames in each run: {frames:,}",
        f"- Paired repetitions: {pairs}",
        "- Profile mode: enabled, shared x86 mock ATS / backend / harness",
        "- Alternating order: " + ", ".join(f"{i}:{v}" for i, v in prepare_order(pairs)),
        f"- Pre-buffered frontend SHA256: {hashes['legacy']}",
        f"- Buffered frontend SHA256: {hashes['buffered']}",
        "",
        "| Variant | Runs | Median wall (s) | Median frame_log mean (us) | "
        "Median frame_log max (us) | Median bridge full mean (us) |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    by_variant = {v: [r for r in rows if r["variant"] == v]
                  for v in ("legacy", "buffered")}
    if len(by_variant["legacy"]) != pairs or len(by_variant["buffered"]) != pairs:
        raise ValueError("Incomplete paired comparison; do not summarize missing runs.")
    for variant, values in by_variant.items():
        def median(key: str) -> float:
            return statistics.median(float(v[key]) for v in values)
        lines.append(
            f"| {variant} | {len(values)} | {median('wall_seconds'):.3f} | "
            f"{median('frame_log_mean_us'):.3f} | "
            f"{median('frame_log_max_us'):.1f} | "
            f"{median('bridge_full_mean_us'):.3f} |"
        )
    legacy = by_variant["legacy"]
    buffered = by_variant["buffered"]
    if len(legacy) != pairs or len(buffered) != pairs:
        raise ValueError("Incomplete paired comparison; do not summarize missing runs.")
    old_median = statistics.median(float(r["frame_log_mean_us"]) for r in legacy)
    new_median = statistics.median(float(r["frame_log_mean_us"]) for r in buffered)
    ratio = (new_median / old_median) if old_median > 0 else None
    lines.extend([
        "",
        "## Interpretation",
        "",
        "- Both versions preserved all mock ATS frame roundtrips and the original "
        "periodic diagnostics line count in each completed run.",
        "- The buffered version records diagnostics in bounded memory during "
        "Elapse and flushes on Dispose; the legacy version opens a log file during driving.",
        "- Frame-log stage mean is an internal microbenchmark. A lower result can "
        "support fewer synchronous diagnostics costs, but cannot establish smoother driving.",
        "- A single highest stall is vulnerable to scheduling noise; compare "
        "repeated measurements and confirm with controlled driving later.",
    ])
    if ratio is not None:
        lines.append(f"- Median frame_log mean ratio (buffered / legacy): {ratio:.3f}.")
    lines.extend([
        "",
        "## Run-by-run measurements",
        "",
        "| Run | Variant | Wall (s) | Frame-log mean/max (us) | Bridge mean (us) |",
        "|---|---|---:|---:|---:|",
    ])
    for row in rows:
        lines.append(
            f"| {row['run_name']} | {row['variant']} | "
            f"{float(row['wall_seconds']):.3f} | "
            f"{float(row['frame_log_mean_us']):.3f}/"
            f"{float(row['frame_log_max_us']):.1f} | "
            f"{float(row['bridge_full_mean_us']):.3f} |"
        )
    lines.extend([
        "",
        "To establish whether 1.19-second frame-log stalls are resolved in "
        "actual BVE6 driving, a separate, controlled driving-window test "
        "would be required. None was performed by this script.",
        "",
    ])
    (output / "offline-ab-report.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--legacy-frontend", type=Path, required=True)
    parser.add_argument("--buffered-frontend", type=Path, required=True)
    parser.add_argument("--backend", type=Path, required=True)
    parser.add_argument("--mock-ats", type=Path, required=True)
    parser.add_argument("--harness", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--frames", type=int, default=12000)
    parser.add_argument("--pairs", type=int, default=3)
    parser.add_argument("--timeout", type=int, default=90)
    args = parser.parse_args()
    if not 1000 <= args.frames <= 100000 or not 1 <= args.pairs <= 10:
        parser.error("Requires 1000-100000 frames and 1-10 paired repetitions")
    inputs = [args.legacy_frontend, args.buffered_frontend, args.backend,
              args.mock_ats, args.harness]
    check_files(inputs)
    hashes = {"legacy": sha256(args.legacy_frontend),
              "buffered": sha256(args.buffered_frontend)}
    if hashes["legacy"] == hashes["buffered"]:
        parser.error("Input DLLs are identical; cannot compare a code change.")
    if args.output.exists():
        parser.error("Output folder exists; refusing to overwrite any previous results")
    args.output.mkdir(parents=True)
    rows = []
    for pair, variant in prepare_order(args.pairs):
        label = f"pair{pair:02}-{variant}"
        frontend = args.legacy_frontend if variant == "legacy" else args.buffered_frontend
        print(f"START {label}: {args.frames} simulated Elapse frames", flush=True)
        try:
            result = run_variant(label, frontend, args.backend, args.mock_ats,
                                 args.harness, args.output, args.frames, args.timeout)
        except (RuntimeError, OSError) as error:
            (args.output / "INCOMPLETE.txt").write_text(
                f"Comparison failed. No performance conclusion.\n{error}\n", encoding="utf-8")
            raise SystemExit(f"OFFLINE_AB_FAILED: {error}")
        rows.append(result)
        print(f"PASS {label}: wall={result['wall_seconds']:.3f}s, "
              f"frame_log_mean={result['frame_log_mean_us']:.3f}us, "
              f"frame_log_max={result['frame_log_max_us']:.1f}us",
              flush=True)
    write_report(rows, hashes, args.output, args.frames, args.pairs)
    print(f"OFFLINE_AB_OK={args.output / 'offline-ab-report.md'}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())