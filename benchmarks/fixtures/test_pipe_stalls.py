"""Offline x64 WriteFile / x86 stdin QPC correlation regression."""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from AnalyzePipeStalls import parse_pipe_waits, overlap_ms, compare, build_report


class PipeStallTests(unittest.TestCase):
    def test_same_frame_overlap_with_shared_qpc(self):
        text = (
            "SLOW_PHASE frame=3774 stage=send duration_ns=231000000 "
            "qpc_start=100000000 qpc_end=102310000 qpc_freq=10000000 "
            "utc_sample_filetime100ns=134379782661438462\n"
            "HOST_INPUT_READ_SUMMARY threshold_ms=50 frames=500 avg_ns=25000000 "
            "max_ns=220000000 stored=1 dropped=0 qpc_freq=10000000\n"
            "SLOW_HOST_INPUT frame=3774 duration_ns=220000000 "
            "qpc_start=100050000 qpc_end=102250000 qpc_freq=10000000 bytes=4130\n"
        )
        sends, waits, summary = parse_pipe_waits(text)
        self.assertEqual(len(sends), 1)
        self.assertEqual(len(waits), 1)
        self.assertEqual(summary["frames"], 500)
        self.assertAlmostEqual(overlap_ms(sends[0], waits[0]), 220.0)
        result = compare(sends, waits)[0]
        self.assertEqual(result["send"]["frame"], 3774)
        self.assertAlmostEqual(result["max_overlap_ms"], 220.0)

    def test_different_frame_does_not_match(self):
        text = (
            "SLOW_PHASE frame=300 stage=send duration_ns=10000000 "
            "qpc_start=100000000 qpc_end=100100000 qpc_freq=10000000 "
            "utc_sample_filetime100ns=134379782661438462\n"
            "HOST_INPUT_READ_SUMMARY threshold_ms=50 frames=500 avg_ns=30000000 "
            "max_ns=200000000 stored=1 dropped=0 qpc_freq=10000000\n"
            "SLOW_HOST_INPUT frame=299 duration_ns=200000000 "
            "qpc_start=99000000 qpc_end=101000000 qpc_freq=10000000 bytes=4130\n"
        )
        sends, waits, _ = parse_pipe_waits(text)
        self.assertEqual(compare(sends, waits)[0]["matching_input_waits"], [])

    def test_rejects_frequency_and_summary_mismatch(self):
        front = (
            "SLOW_PHASE frame=1 stage=send duration_ns=10000000 "
            "qpc_start=100000000 qpc_end=100100000 qpc_freq=10000000 "
            "utc_sample_filetime100ns=134379782661438462\n"
        )
        slow = (
            "SLOW_HOST_INPUT frame=1 duration_ns=100000000 "
            "qpc_start=100000000 qpc_end=101000000 qpc_freq=10000000 bytes=4000\n"
        )
        with self.assertRaisesRegex(ValueError, "summary"):
            parse_pipe_waits(front + "HOST_INPUT_READ_SUMMARY threshold_ms=50 frames=500 "
                             "avg_ns=1000 max_ns=100000000 stored=0 dropped=0 "
                             "qpc_freq=10000000\n" + slow)
        with self.assertRaisesRegex(ValueError, "frequency"):
            parse_pipe_waits(front + slow.replace("qpc_freq=10000000", "qpc_freq=20000000").replace("duration_ns=100000000", "duration_ns=50000000"))

    def test_old_log_reports_missing_host_instrumentation(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "bridge.log"
            path.write_text("METRICS bridge_elapse_frames=500 avg_us=300 max_us=10000\n",
                            encoding="utf-8")
            report = build_report(path)
            self.assertIn("not available", report)
            self.assertIn("No frontend WriteFile", report)


if __name__ == "__main__":
    unittest.main()