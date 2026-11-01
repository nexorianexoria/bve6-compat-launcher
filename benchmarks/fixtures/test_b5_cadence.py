"""Unit checks for the mock 60Hz cadence/ATS pause reporter."""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from RunB5CadenceRegression import HOST_ATS, HOST_INPUT, FRONTEND_PHASE, slow_ns, write_report


class CadenceRegressionTests(unittest.TestCase):
    def test_separates_ats_callback_and_host_idle(self):
        text = (
            "SLOW_HOST_INPUT frame=401 duration_ns=120000000 "
            "qpc_start=100000 qpc_end=1300000 qpc_freq=10000000 bytes=4123\n"
            "SLOW_HOST_PHASE frame=451 stage=ats duration_ns=86000000 "
            "qpc_start=1400000 qpc_end=2260000 qpc_freq=10000000\n"
            "SLOW_PHASE frame=451 stage=receive duration_ns=87000000 "
            "qpc_start=1390000 qpc_end=2260000 qpc_freq=10000000\n"
        )
        self.assertEqual(slow_ns(text, HOST_INPUT, 401), 120000000)
        self.assertEqual(slow_ns(text, HOST_ATS, 451), 86000000)
        self.assertEqual(slow_ns(text, FRONTEND_PHASE, 451, "receive"), 87000000)
        self.assertEqual(slow_ns(text, HOST_ATS, 401), 0)
        self.assertEqual(slow_ns(text, HOST_INPUT, 451), 0)

    def test_report_states_no_real_fps_measurement(self):
        with tempfile.TemporaryDirectory() as td:
            output=Path(td)
            base=dict(frames=600,wall_seconds=10.1,ats_pause_ms_measured=86.0,
                      input_idle_ms_measured=121.0,frame_log_mean_us=0.2,
                      frame_log_max_us=20.0)
            write_report([{**base,"variant":"legacy"},{**base,"variant":"buffered"}],
                         output,16667,400,120,450,85)
            report=(output/"cadence-regression-report.md").read_text(encoding="utf-8")
            self.assertIn("BVE6 was NOT launched",report)
            self.assertIn("remains UNVERIFIED",report)
            self.assertEqual(len((output/"cadence-regression-results.csv").read_text().splitlines()),3)


if __name__ == "__main__":
    unittest.main()