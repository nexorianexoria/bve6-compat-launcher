# BVE5 / BVE6 background audio — read-only investigation (2026-11-07)

## User-observed behavior

User reports that **all BVE sound**, not merely MotorMaster or a particular
ATS plugin, disappears when another application window receives focus.
The existing always-on-top option works acceptably. The user wants two
independent launcher checkboxes.

## Evidence from the installed BVE binaries

Read-only `ildasm` inspection; the original vendor files were **not**
modified, copied into the repository, or redistributed.

- BVE6: `C:\Program Files\mackoy\BveTs6\BveTs.exe`,
  file version **6.0.7554.619**.
- BVE5: `C:\Program Files (x86)\mackoy\BveTs5\BveTs.exe`,
  file version **5.8.7554.391**.
- Both reference `SlimDX.DirectSound.SoundBufferDescription` and
  `SlimDX.DirectSound.SecondarySoundBuffer`.
- Both versions have **three** observed buffer-description flag-setting
  sites using the constant **`0xe0`**.
- That value lacks the DirectSound `DSBCAPS_GLOBALFOCUS` flag
  (`0x8000`). The BVE6 program sets the DirectSound cooperative level
  separately; this is not an audio buffer focus flag.

Microsoft's DirectSound documentation says `DSBCAPS_GLOBALFOCUS`
allows an application's sound buffers to continue playing when focus
switches to another application:
https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee416818(v=vs.85)

## What is established and what is not

**Established:** The installed BVE5 and BVE6 builds do not request the
DirectSound global-focus behavior at these three buffer-creation sites.
This is consistent with the user's observation.

**Not established:** Whether BVE also pauses its simulation, explicitly
stops sounds, or performs other actions when focus is lost; whether changing
only the buffer flags would fully restore background sound; or whether
patching would preserve the rendering/input/BveEX/ATS compatibility.

## Engineering constraints / current feature status

- The independent launcher checkbox for background sound remains **disabled**
  and clearly labeled unimplemented, not a fake toggle.
- The always-on-top setting is separate, remains opt-in, and is functional
  on a synthetic Win32 window (real BVE behavior user says looks fine).
- A speculative Core Audio session-unmute helper from earlier prototyping
  crashed its isolated test process; that helper was **removed**.
- Never modify or replace the original installed BVE binaries, SlimDX DLL,
  Windows global audio configuration, audio drivers, or third-party plugins.
- Do **not** publish/distribute any patched proprietary BVE binary.
- Any future interoperability experiment must be isolated, reversible,
  licensed, and validated with a real BVE run before enabling its checkbox.

## Current verdict

The exact **BVE-owned** DirectSound buffer configuration has now been
identified. There is still **no demonstrated safe launcher-side fix**
for all background audio, so the requested audio checkbox cannot yet be
enabled truthfully.
