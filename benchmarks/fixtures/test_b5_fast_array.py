"""Run the C++ fixed-buffer codec and protocol-stream microbenchmark."""
from __future__ import annotations
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[2]
harness = root / "dist-b5" / "bridge-mock" / "bridge_harness.exe"
if not harness.is_file():
    raise SystemExit(f"Mock harness missing: {harness}")
result = subprocess.run(
    [str(harness)], capture_output=True, text=True, errors="replace", timeout=90)
if (result.returncode != 2 or "FAST_ARRAY_CODEC_OK" not in result.stdout
        or "PROTOCOL_STREAM_MICROBENCH" not in result.stdout):
    raise SystemExit(
        "FAST_ARRAY_CODEC_FAILED\n"
        f"exit={result.returncode}\nstdout={result.stdout}\nstderr={result.stderr}")
print(result.stdout.strip())
print("FAST_ARRAY_FIXED_BUFFER_REGRESSION=PASS")
