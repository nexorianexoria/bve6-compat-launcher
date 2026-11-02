# BVE6 / ATS / HornMaster investigation — November 2, 2026

## Reported symptoms

- In the Osaka Metro Series 66 vehicle of the Hankyu Kyoto local 821 scenario, the horn may sound when moving to the emergency brake position.
- The horn button sometimes produces no audible sound.
- Some ATS button presses have no visible effect.

## Verified facts from the installed b9 session

- Scenario: HK京都本線Ver3.5 - 普通821; vehicle: HankyuTrainPack/J9/OC66/Vehicle/66610f_821.txt.
- Actual 32-bit plug-in: OC66/ats/detailmanager.dll, loading ATS_HK.dll, Brake.dll, HornMaster.dll, and other modules.
- The bridge log shows brake notch 7 and at least one HornBlow event, but older logs do not retain every key/horn code or the sound 31–34 output.
- External-controller configuration enables MultiTrainController P5B5. Hardware includes MasterController V1.00.
- HornMaster's author documents instability with some external controllers and non-support for some buttons:
  https://sites.google.com/view/fwchbve/top/bveデーター公開一覧/hornmaster

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
