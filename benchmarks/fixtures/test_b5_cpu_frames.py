"""Validate CPU/GPU per-swapchain measurement without operating BVE."""
import csv
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from AnalyzeB5CpuFrames import report, parse_csv


class PerSwapchainCpuGpuReportTests(unittest.TestCase):
    def test_separate_swapchains_and_long_cpu_frame(self):
        with tempfile.TemporaryDirectory() as folder:
            csvfile=Path(folder)/"presentmon.csv"
            with csvfile.open("w",encoding="utf-8",newline="") as f:
                writer=csv.writer(f)
                writer.writerow(["ProcessID","SwapChainAddress","CPUStartQPC",
                                 "MsBetweenPresents","MsCPUBusy","MsGPUBusy"])
                writer.writerow(["20","0xA","10000000", "16.5","12.3","2.1"])
                writer.writerow(["20","0xA","10500000", "55.5","52.7","2.8"])
                writer.writerow(["20","0xB","10450000", "16.1","8.8","6.1"])
            groups=parse_csv(csvfile)
            self.assertEqual(len(groups),2)
            text=report(csvfile,qpc_begin=10000000)
            self.assertIn("swap chain 0xA",text)
            self.assertIn("swap chain 0xB",text)
            self.assertIn("interval=55.50ms, CPU busy=52.70ms, GPU busy=2.80ms",text)
            self.assertIn("First recorded frame after capture start: 0.00s",text)
            self.assertIn("Frames with interval >50ms: 1",text)

    def test_missing_columns_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            csvfile=Path(folder)/"bad.csv"
            csvfile.write_text("CPUStartQPC,MsBetweenPresents\n100000,33.3\n")
            with self.assertRaisesRegex(ValueError,"Missing required"):
                parse_csv(csvfile)


if __name__=="__main__":
    unittest.main()