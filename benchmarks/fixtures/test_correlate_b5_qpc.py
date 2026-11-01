"""Regression checks for QPC matching; does not run or edit BVE."""
import csv
from pathlib import Path
import tempfile
import unittest
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from CorrelateBveStalls import read_events, read_frames, correlate


class SlowEventJoinTest(unittest.TestCase):
    def test_correlates_only_same_qpc_window_and_keeps_swapchains_separate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            freq = 10000000
            log = root / "bridge.log"
            csv_path = root / "fps.csv"
            log.write_text(
                "SLOW_PHASE_SUMMARY threshold_ns=5000000 stored=1 dropped=0 qpc_freq=10000000\n"
                "SLOW_PHASE frame=124 stage=receive duration_ns=25000000 "
                "qpc_start=100000000 qpc_end=100250000 qpc_freq=10000000 "
                "utc_sample_filetime100ns=134379699992953263\n"
                "SLOW_HOST_PHASE_SUMMARY threshold_ns=5000000 stored=1 dropped=0 qpc_freq=10000000\n"
                "SLOW_HOST_PHASE frame=124 stage=ats duration_ns=18000000 "
                "qpc_start=100030000 qpc_end=100210000 qpc_freq=10000000\n",
                encoding="utf-8"
            )
            with csv_path.open("w", encoding="utf-8", newline="") as file:
                writer = csv.writer(file)
                writer.writerow(["ProcessID","SwapChainAddress","CPUStartQPC","MsBetweenPresents"])
                writer.writerow(["123","0x1","100120000","50.1"])
                writer.writerow(["123","0x1","100200000","15.0"])
                writer.writerow(["123","0x2","100100000","11.0"])
                writer.writerow(["123","0x1","112000000","60.0"])
            events, dropped = read_events(log)
            self.assertEqual(dropped, 0)
            self.assertEqual(len(events), 2)
            self.assertEqual({e["stage"] for e in events}, {"receive", "host_ats"})
            groups = read_frames(csv_path)
            self.assertEqual(len(groups), 2)
            matches = correlate(events, groups, 0, 0, 33.33, "0x1")
            self.assertEqual(len(matches[0][1]), 1)
            self.assertEqual(matches[0][1][0]["count"], 2)
            self.assertEqual(matches[0][1][0]["slow"], 1)
            self.assertAlmostEqual(matches[0][1][0]["max_ms"], 50.1)
            self.assertEqual(correlate(events, groups, 0, 0, 33.33, "0x3")[0][1], [])
            host_event = next(e for e in events if e["stage"] == "host_ats")
            host_matches = correlate([host_event], groups, 0, 0, 33.33, "0x1")
            self.assertEqual(host_matches[0][1][0]["count"], 2)

    def test_new_frontend_post_full_events_are_parsed(self):
        with tempfile.TemporaryDirectory() as directory:
            logfile = Path(directory) / "bridge.log"
            logfile.write_text(
                "SLOW_PHASE_SUMMARY threshold_ns=5000000 stored=2 dropped=0 qpc_freq=10000000\n"
                "SLOW_PHASE frame=300 stage=post duration_ns=18000000 "
                "qpc_start=10000000 qpc_end=10180000 qpc_freq=10000000 "
                "utc_sample_filetime100ns=134379699992953263\n"
                "SLOW_PHASE frame=300 stage=full duration_ns=20000000 "
                "qpc_start=9980000 qpc_end=10180000 qpc_freq=10000000 "
                "utc_sample_filetime100ns=134379699992953263\n",
                encoding="utf-8",
            )
            events, dropped = read_events(logfile)
            self.assertEqual({e["stage"] for e in events}, {"post", "full"})
            self.assertEqual({e["frame"] for e in events}, {300})
            self.assertEqual(dropped, 0)

    def test_rejects_csv_without_qpc(self):
        with tempfile.TemporaryDirectory() as directory:
            p = Path(directory) / "fps.csv"
            p.write_text("TimeInMs,MsBetweenPresents\n14.0,33.3\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "CPUStartQPC"):
                read_frames(p)


if __name__ == "__main__":
    unittest.main()