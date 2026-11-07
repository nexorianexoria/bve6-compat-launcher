# BVE background sound: isolated DirectSound evidence (2026-11-07)

## Aim

Determine whether the **actual installed** BVE5/BVE6 SlimDX DirectSound
runtime accepts `DSBCAPS_GLOBALFOCUS`, without changing BVE software or
playing audible sound. This is not an end-to-end BVE audio test.

## Installed versions and read-only baseline

- BVE5 `5.8.7554.391` (32-bit): inspected BveTs.exe contains three
  `SoundBufferDescription.Flags = 0xe0` creation sites.
- BVE6 `6.0.7554.619` (64-bit): same three flag-setting sites, same
  value `0xe0`.
- `0x00e0` = frequency, pan and volume control flags; adding DirectSound
  GlobalFocus (`0x8000`) would make `0x80e0`.
- The original installed BVE and SlimDX binaries were read-only throughout.

## Isolated experiment

`benchmarks/B13DirectSoundFocusProbe.cs` creates a **silent** DirectSound
secondary buffer with the installed, version-matching SlimDX. The x86 and
x64 probes are compiled separately by:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File benchmarks\TestB13DirectSoundFocus.ps1
```

It creates and releases one buffer without GlobalFocus and one with
GlobalFocus. No sound is played, no BVE route is opened, no foreground focus
is manipulated, and original BVE/SlimDX SHA-256 values are checked after
each run. The x86 synthetic executable uses the same .NET 2 mixed-mode
activation policy as the *unmodified* BVE5 application config.

## Results (2026-11-07)

Both x86/BVE5 SlimDX and x64/BVE6 SlimDX:

```text
CooperativeLevel=Priority Status=S_OK
RequestedFlags=0x00E0 RequestedGlobalFocus=False ReportedGlobalFocus=False
RequestedFlags=0x80E0 RequestedGlobalFocus=True ReportedGlobalFocus=True
BUFFER_FLAG_ROUNDTRIP=PASS
AUDIBLE_BACKGROUND_PLAYBACK=NOT_TESTED
ORIGINAL_FILES_UNCHANGED=PASS
```

## Decision

The API/runtime accepts GlobalFocus for both architectures, and reports
that capability on the created buffer. **This does not establish that
actual BVE gameplay audio continues without input focus.**

The official BVE6 software terms prohibit modifying vendor binary files
without permission:
https://bvets.net/en/download/main6.html

The open-source external Windows utility DxWnd is a promising, separate
DirectSound interceptor. Its developer describes enabling `Hook DirectSound`
and `Sound > Global focus > ON` to preserve audio on focus change in
certain older games:
https://sourceforge.net/p/dxwnd/discussion/general/thread/75efbfb3/

SourceForge currently lists DxWnd v2.06.16 dated 2026-10-16:
https://sourceforge.net/projects/dxwnd/files/Latest%20build/

However, attempting to retrieve its official release from this Windows
development environment yielded only ~133 KB HTML pages (instead of the
expected ~9.6 MB RAR). **No third-party hook binary was executed.**
An older public GitHub source mirror examined for reference was dated
2017 and does **not** implement the newer GlobalFocus toggle; do not
assume its flags/profiles match the current official release.

## Requirements before launcher integration

1. Obtain and verify the official DxWnd package in a controlled environment,
   without blanket security exclusions.
2. Configure **DirectSound-only** intercept and GlobalFocus ON, avoiding
   unrelated D3D, input, timing, fullscreen or resolution modifications.
3. A/B test actual BVE5/BVE6 gameplay with foreground/background clicks:
   all running sounds, horn and ATS sounds; input, 32-bit ATS bridge,
   PanelViewEX, TSHUD, graphics/frame rate and loading.
4. If successful, implement an explicitly opt-in independent checkbox,
   launch-time dependency checks, clean rollback, and a fallback to the
   original BVE launch path when unavailable. Do not enable the toggle
   merely because the synthetic probe passes.
5. Never patch, replace, distribute or otherwise alter the vendor BVE
   binaries; preserve the published v0.29b11 release.

**Current status:** synthetic DirectSound test **PASS**; live background
audio **UNVERIFIED**; actual audio checkbox still **disabled**.
