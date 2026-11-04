# Ver.0.29b11 — launch options development notes

This is a development branch, not a GitHub release or automatic update. It
is based on published v0.29b9, **not** on the unapproved b10 ATS diagnostic branch.
The published b9 update manifest and the installed application are unchanged.

## Implemented

- Settings button now opens a dialog with tabs for BVE window size and monitor,
  reduced graphics load, and additional input devices.
- BVE5 and BVE6 preferences are edited independently by preserving the native
  XML format and unknown sections and making timestamped backups of source files.
- BVE6 settings file: `Documents/BveTs/Settings/BveTs6.Preferences.xml`.
- BVE5 settings file: `Documents/BveTs/Settings/Preferences.xml`.
- Windowed mode, Width, Height, DrawDistance, BackDrawDistance, Anisotropy,
  and the active InputPlugins list are exposed. The user must explicitly save.
- The lightweight preset *sets values only*, and does not claim that FPS will
  increase in every route. No BVE binary or original route/vehicle data is edited.
- Monitors are enumerated from the current Windows display topology; the
  selected monitor is stored in a launcher-specific XML file in LocalAppData.
  After BVE starts, only the launched BVE process window is moved via SetWindowPos;
  the separate native `シナリオの選択` dialog is also moved to the selected
  monitor without changing its own size;
  primary-display selection and Windows display resolution are untouched.
  Fullscreen monitor movement is not supported (BVE decides where fullscreen is).
- Additional input-device plugins can be toggled from the native
  InputPlugins list, without launching BVE.
- Known controllers in `Kusaanko.NumerousControllerInterface.xml` can be
  enabled/disabled and their existing ProfileMap selections can be edited.
  Only profile names already present in that file are allowed.
- Saving is disabled if any BveTs instance is running.
- During BVE gameplay the existing b9 launcher quiet mode remains in effect.

## Automated checks

- `TestB11StartupSettings.ps1` copied the native BVE6 preferences and
  controller settings into an isolated test folder, changed selected values,
  verified round-trips and safety backups, and verified BVE key assignment
  and unrelated controller profiles were not modified.
- Per-version saved monitor choices were checked independently.
- A synthetic hidden WinForms window was moved to a negative-coordinate
  secondary monitor and its bounds were verified.
- The new dialog was created and all three tabs checked.
- Hashes of the user's original BVE6 preferences and controller settings
  were identical before/after the tests.
- A controlled copy of the controller settings also passed an explicit
  profile-selection save/readback and the profile editor UI test.
- B9 other-vehicle picker, B6 candidate choices, and PanelView diagnostic
  regression tests passed against the new development executable.
- A Windows Defender custom scan of the development launcher reported no
  new threat for this executable. This does not constitute a guarantee of safety.

## Real BVE6 start-menu integration verification (2026-11-04)

- Launched the installed BVE6 executable at
  `C:/Program Files/mackoy/BveTs6/BveTs.exe` without opening a route.
  The test supplied DISPLAY3 to the launcher monitor-placement hook without
  saving or modifying the user's launcher monitor preference.
- BVE creates two visible top-level windows, the main `BVE Trainsim 6`
  and a modal `シナリオの選択` form. The first test showed that moving just
  the BVE main window did NOT reliably move the separate picker.
- Updated the window selector to recognize both BVE-native windows,
  ignore plugin/HUD/other windows, preserve the picker size and center both.
- The subsequent actual BVE6 test verified the main window and scenario
  picker both centered on the selected DISPLAY3 monitor after 9 seconds.
- Closed the test-created native scenario dialog using WM_CLOSE and then
  the main BVE window by ordinary CloseMainWindow. The BVE process exited.
- The test restored the original BVE6 preferences on exit and verified that
  the launcher's saved monitor preference was never modified.
- A separate route-based smoke test earlier reported a
  `Mackoy.Bvets.TsmcInterface.TsmcInterface.DataReadAndLevMov()`
  NullReferenceException from a BVE input plugin. This is not demonstrated
  to be caused by the launcher. It remains a separate diagnostic issue.

## Remaining limitations

- The launcher cannot rewrite or optimize BVE's proprietary rendering engine.
- No real-world FPS before/after benchmark has been completed; a reduced
  rendering range may improve performance at the expense of visible scenery.
- Device-specific keyboard/button assignment editors beyond the plugin list
  and NumerousController on/off/profile selections are not yet implemented.
- Real BVE6 start-menu and scenario-picker placement was verified. The
  driving window (after a route loads) has not been tested in this version.
- Do not publish as a release until the actual BVE launch is confirmed.
