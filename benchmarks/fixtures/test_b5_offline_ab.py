"""Regression tests for no-BVE A/B benchmarking and report generation."""
from __future__ import annotations
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from CompareB5BufferedDiagnostics import prepare_order, write_report


class OfflineComparisonTests(unittest.TestCase):
    def test_alternating_order(self):
        self.assertEqual(prepare_order(3), [
            (1, "legacy"), (1, "buffered"),
            (2, "buffered"), (2, "legacy"),
            (3, "legacy"), (3, "buffered"),
        ])

    def test_report_keeps_limits_and_paired_runs(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "out"
            rows = [
                {"variant": variant, "run_name": f"{pair}-{variant}",
                 "frames": 3000, "wall_seconds": 1.2 if variant == "legacy" else 1.4,
                 "roundtrip_mean_us": 250,
                 "host_ats_mean_us": 160,
                 "frame_log_mean_us": 1.0 if variant == "legacy" else 0.2,
                 "frame_log_max_us": 500 if variant == "legacy" else 40,
                 "frame_log_over_5ms": 0,
                 "bridge_full_mean_us": 255 if variant == "legacy" else 260,
                 "bridge_full_max_us": 34000,
                 "snapshots": 11, "sounds": 1, "log_path": f"log_{variant}"}
                for pair, variant in prepare_order(2)
            ]
            write_report(rows, {"legacy": "oldhash", "buffered": "newhash"},
                         folder, frames=3000, pairs=2)
            report = (folder / "offline-ab-report.md").read_text(encoding="utf-8")
            self.assertIn("BVE6 was not launched", report)
            self.assertIn("not a measured change in actual BVE FPS", report)
            self.assertIn("Median frame_log mean ratio (buffered / legacy): 0.200", report)
            self.assertEqual(
                len((folder / "offline-ab-results.csv").read_text().splitlines()), 5
            )

    def test_incomplete_pair_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaisesRegex(ValueError, "Incomplete paired comparison"):
                write_report(
                    [{"variant": "legacy", "run_name": "1-legacy",
                      "frames": 3000, "wall_seconds": 1,
                      "frame_log_mean_us": 1, "frame_log_max_us": 20,
                      "bridge_full_mean_us": 90}],
                    {"legacy": "x", "buffered": "y"}, Path(tmp), 3000, 1,
                )


if __name__ == "__main__":
    unittest.main()