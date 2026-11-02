# BVE6 / ATS / HornMaster investigation — November 2, 2026

## Reported symptoms

- In the Osaka Metro Series 66 vehicle of the Hankyu Kyoto local 821 scenario, the horn may sound when moving to the emergency brake position.
- The horn button sometimes produces no audible sound.
- Some ATS button presses have no visible effect.
- The user confirmed that a keyboard horn press is silent too, not only the
  external controller button. That narrows the scope beyond controller mapping.

## Verified facts from the installed b9 session

- Scenario: HK京都本線Ver3.5 - 普通821; vehicle: HankyuTrainPack/J9/OC66/Vehicle/66610f_821.txt.
- Actual 32-bit plug-in: OC66/ats/detailmanager.dll, loading ATS_HK.dll, Brake.dll, HornMaster.dll, and other modules.
- The bridge log shows brake notch 7 and at least one HornBlow event, but older logs do not retain every key/horn code or the sound 31–34 output.
- External-controller configuration enables MultiTrainController P5B5. Hardware includes MasterController V1.00.
- HornMaster's author documents instability with some external controllers and non-support for some buttons:
  https://sites.google.com/view/fwchbve/top/bveデーター公開一覧/hornmaster
- The installed HornMaster.dll explicitly imports USER32!GetAsyncKeyState
  and DirectInput8Create; installed Other66.dll imports GetAsyncKeyState.
  The KHK1500 MotorMaster.dll imports GetAsyncKeyState and GetForegroundWindow.
  This was checked with Visual Studio dumpbin /imports against the user's DLLs.
  These are architectural compatibility risks, **not conclusive evidence of
  why a specific input failed**. Microsoft's API documentation:
  https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate
- The vehicle author's actual 66 series keyboard assignments on BVE defaults
  are Home = ATS reset, PageUp = ATS shunting, End = ATS confirmation.
  Some controls are intentionally conditional and may have no visible effect
  outside the intended operating state:
  https://sites.google.com/view/fwchbve/top/bveデーター公開一覧/阪急線車両パック/機器操作解説
- HornMaster's HornParameter.txt points its primary and secondary horn sounds
  to ATS indices 31 through 34. All four indices are present in the prepared
  BVE6 sound.txt, and their 44.1 kHz stereo PCM WAV files exist.
- BVE5.8.7554.391 is installed at
  C:/Program Files (x86)/mackoy/BveTs5/BveTs.exe. The original 66 series
  scenario specifies its unmodified x86 DetailManager.dll through the original
  BveTs Vehicle 1.00 `Ats` field. `CheckLegacyVehicleFallback.ps1`
  passed the read-only path, x86 and HornParameter mapping checks.
- An attempt to run a BVE5 GUI smoke test was **skipped safely** because
  the user had BVE6 running. No BVE5 executable was started. Audio playback
  and keyboard behavior in BVE5 remain unverified.

## Other vehicle (KHK1500) evidence

- The later Keikyu 1500 / C-ATS scenario uses a different DetailManager,
  C_ATS.dll and MotorMaster.dll, not HornMaster.dll.
- That run recorded a HornBlow, KeyDown and KeyUp callback, but the b9 log
  contains no callback types or native BVE sound-player results.
- Its horn configuration references four existent non-silent PCM WAV files.
- The converted sound.txt retains the legacy Version 1.0 header. Compatibility
  with modern BVE sound syntax is worth checking, but **not a proven cause**.

## Source-only changes (NOT published)

- Serialize complete frontend pipe writes with a Windows SRW lock, and make backend failure state atomic.
- Buffer frontend key-down, key-up and horn callback codes with delivery status until Dispose.
- Record host-side ATS key and horn callback types and bounded sound channel 31–34 changes, without changing any of the sound output data.
- Provide optional synthetic mock ATS and a concurrency stress test.

## Test outcome

- A synthetic ATS roundtrip confirms the new bridge forwards key 7 and horn 0 with sound 31 and 33 states.
- A 100-frame stress test with 80 key-down, 80 key-up and 80 horn callbacks reports no losses.
- The original b9 bridge also passed the same synthetic input stress test. Therefore the reported real-vehicle fault has NOT been reproduced in the test harness, and the frontend lock cannot be claimed as a confirmed fix.
- Attempted 0.29b10 packaging was blocked at archive-extraction verification by Microsoft Defender (Trojan:Win32/Wacatac.B!ml). Defender quarantined a temporary extracted executable and reported DidThreatExecute=False. **No b10 binaries are approved or published.**
- Existing b9 installation, vehicle files and input-device settings remain unchanged.

## Outstanding verification

1. Distinguish the BVE keyboard-assigned ATS/horn inputs from physical controller inputs with the same vehicle.
2. Validate the modified bridge with the real ATS_HK / HornMaster modules once safe distribution and release checks pass.
3. Determine whether the real HornMaster plug-in actually emits sound commands 31–34 on horn presses and why the emergency position causes a warning sound.
4. Once the user is no longer driving in BVE6, perform an isolated BVE5
   direct-ATS comparison; do not interrupt or terminate an active drive.
