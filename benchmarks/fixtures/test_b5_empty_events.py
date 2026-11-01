"""Regression checks for empty ATS slow-event runs."""
import csv
from pathlib import Path
import tempfile
import unittest
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from CorrelateBveStalls import read_events
from AnalyzeB5Goal import assess


class NoSlowAtsEvents(unittest.TestCase):
    def test_zero_events_are_valid_for_goal_report(self):
        with tempfile.TemporaryDirectory() as path:
            directory = Path(path)
            logfile = directory / "bridge.log"
            csvfile = directory / "fps.csv"
            logfile.write_text(
                "SLOW_PHASE_SUMMARY threshold_ns=5000000 stored=0 dropped=0 qpc_freq=10000000\n"
                "SLOW_HOST_PHASE_SUMMARY threshold_ns=5000000 stored=0 dropped=0 qpc_freq=10000000\n",
                encoding="utf-8")
            with csvfile.open("w", encoding="utf-8", newline="") as out:
                writer = csv.writer(out)
                writer.writerow(["ProcessID", "SwapChainAddress", "CPUStartQPC",
                                 "MsBetweenPresents", "MsCPUBusy", "MsGPUBusy"])
                writer.writerow([4242, "0x123", 100000000, "16.0", "8", "2"])
                writer.writerow([4242, "0x123", 100160000, "16.0", "8", "2"])
            events, overflow = read_events(logfile, allow_empty=True)
            self.assertEqual(events, [])
            self.assertEqual(overflow, 0)
            with self.assertRaisesRegex(ValueError, "No slow ATS phase"):
                read_events(logfile)
            report = assess(csvfile, logfile, repeats=10)
            self.assertIn("No host ATS events >5ms", report)
            self.assertIn("Swap chain 0x123", report)

    def test_empty_malformed_log_is_not_silent_success(self):
        with tempfile.TemporaryDirectory() as path:
            logfile = Path(path) / "bridge.log"
            logfile.write_text("HOST_STARTED pid=3\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "No slow ATS phase"):
                read_events(logfile, allow_empty=True)


if __name__ == "__main__":
    unittest.main()