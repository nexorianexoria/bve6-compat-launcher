"""Protect startup update checking and installer manifest wiring."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT/"src/Launcher.cs").read_text(encoding="utf-8")
AUTO = (ROOT/"src/AutoUpdate.cs").read_text(encoding="utf-8")
INSTALL = (ROOT/"src/InstallEngine.cs").read_text(encoding="utf-8")

class TestUpdateStartup(unittest.TestCase):
    def test_gui_shown_checks_for_updates_once(self):
        self.assertEqual(SOURCE.count("AutoUpdate.Check(this)"), 1)
        self.assertIn("if(startServices)Shown+=(s,e)=>{bveActivity.Start();LoadData();AutoUpdate.Check(this);}", SOURCE)
    def test_check_asks_user_only_if_version_newer(self):
        self.assertIn("if(update==null)return;", AUTO)
        self.assertIn("UpdatePrompt.Show(owner,update,root!=null,out launchAfterUpdate)", AUTO)
        self.assertIn("new Version(Release.Number)", AUTO)
    def test_installer_manifest_has_version_and_hash_verification(self):
        self.assertIn("ValidateManifest(", INSTALL)
        self.assertIn("Hash(zip).Equals(manifest.sha256", INSTALL)
        self.assertIn("package-index.json",INSTALL)

if __name__=="__main__":
    unittest.main()
