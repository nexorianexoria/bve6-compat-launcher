"""Guard normal-launch vs one-click profile behavior for b5 portable RC."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / "src/Launcher.cs").read_text(encoding="utf-8")
RELEASE = (ROOT / "src/Release.cs").read_text(encoding="utf-8")

class TestReleaseDefaults(unittest.TestCase):
    def test_gui_launch_is_profile_off(self):
        begin = SOURCE.index('async void Start(int mode)')
        end = SOURCE.index('finally{Idle();SetLaunchEnabled(true);}', begin)
        gui = SOURCE[begin:end]
        self.assertIn('start.EnvironmentVariables["BVE5COMPAT_PROFILE"]="0"', gui)
        self.assertNotIn('start.EnvironmentVariables["BVE5COMPAT_PROFILE"]="1"', gui)
        self.assertIn('BVE5COMPAT_ATS', gui)
        self.assertIn('BVE5COMPAT_WORKDIR', gui)

    def test_headless_oneclick_profile_remains_on(self):
        begin = SOURCE.index('static int AutoStartCompat(')
        end = SOURCE.index('using(var process=Process.Start(start))', begin)
        recorder = SOURCE[begin:end]
        self.assertIn('start.EnvironmentVariables["BVE5COMPAT_PROFILE"]="1"', recorder)
        self.assertIn('BVE5COMPAT_LOG', recorder)
        self.assertIn('--validate-only', SOURCE)

    def test_ui_and_binary_version_are_b5(self):
        self.assertIn('public const string Display="0.29b5"', RELEASE)
        self.assertIn('public const string Number="0.2.9.16"', RELEASE)
        self.assertIn('AutoUpdate.Check(this)', SOURCE)
        self.assertNotIn('（b5開発版）', SOURCE)
        self.assertIn('Release.Display+" 正式版"', SOURCE)

if __name__ == "__main__":
    unittest.main()
