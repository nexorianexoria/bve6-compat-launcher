# v0.29b12-test / Toei Mita vehicle sound compatibility experiment

This branch is an **unreleased development candidate**. GitHub's b11
installer, updater manifest, and public download files are unchanged.

## Source evidence

- Mita Line `1037G` is marked as 2025-spec, and its station list uses
  `Melody_eki_A3_L/R` as the platform departure sound.
- Mita Line `1343T` is marked as 2022-spec, and its station list uses
  `Melody_eki_A2_L/R`.
- `TKK3020/3121.txt` uses `TKK3020/Sound.txt`, which maps ATS channels
  200-203 to `NewB_L/R` and `NewA_L/R`; its channels 204/205 are explicitly
  labeled `都営乗降促進左/右` and reference `JSC_1_L/R.wav`.
- Actual G/H keyboard-to-ATS-sound mappings in the closed-source MCCP
  plug-in **have not been verified**. This is a targeted experiment, not a
  confirmed fix for all vehicle and route combinations.

## Implementation

- When using the exact TKK3020/3121 vehicle on a route named 地下鉄三田線,
  prepare a temporary vehicle sound copy for both BVE6 normal and compatibility
  startup. This source-guided experiment maps ATS 200 and 202 to 204, and
  ATS 201 and 203 to 205. In all other cases, sound files are left alone.
- The original Sound.txt, 3121.txt, MCCP plug-in, BveEX plug-ins,
  route/station files, and their WAV files are never rewritten.
- In BVE6 normal mode, the temporary vehicle has absolute references to
  original assets, and an independent VehiclePluginUsing.xml pointing to the
  original BveEX DLLs. The compatibility mode keeps its previous
  staging approach.
- The generated run directory contains `三田線の音声調整.txt` describing the
  mapping and the uncertainty.

## Validation

- Tested route 1037G and 1343T in normal and compatibility preparation.
- Each case generated a shadow vehicle, shadow sound file, and BveEX
  configuration; temporary ATS channels 200-203 matched the existing
  original 204/205 resources, with valid WAV files.
- Hashes of original scenario, vehicle and sound files stayed unchanged.
- Sotetsu route with TKK3020 vehicle kept the original vehicle path.
- B9 other-vehicle selection, B6 route candidate choice and PanelViewEX
  diagnostic regression checks passed against the candidate build.

## Open items

- Do not publish until the user can confirm which sound G/H actually triggers,
  and verify both BVE6 normal and compatibility start with this shadow vehicle.
- This remaps **vehicle-side passenger-promotion sound**, NOT the
  route's distinct station-specific platform melodies.
- A separate exit-time BVE NullReferenceException has not been fixed.
- Do not attempt in-process key injection or disturb a running BVE game.
