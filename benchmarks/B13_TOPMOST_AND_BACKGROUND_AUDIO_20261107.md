# b13 opt-in BVE window controls — 2026-11-07

## Scope

- Development branch only: `feature/0.29b13-performance`.
- Public b11, updater manifest, BVE binaries, real settings files, route and
  vehicle packages, and third-party plugins remain untouched.
- New launcher settings: **BVE画面・軽量化・入力デバイス設定 → 画面**.
- Two separately labeled checkboxes, both off by default.

## 1. Main game window always on top (functional candidate)

`BVEの運転画面を最前面に固定` is enabled only when running BVE in
windowed mode. A launcher-owned `window-options.xml` file stores the opt-in
flag independently for BVE5/BVE6. Settings are not saved while BVE is running.

After BVE is launched from the launcher, a lightweight worker enumerates
only BVE-owned, identified main / scenario picker windows and uses Windows
`SetWindowPos(HWND_TOPMOST, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)`.
The scenario chooser is included so it is not hidden behind the topmost main
window. This does **not** force BVE to regain focus or affect other plugin
windows. It watches for BVE window recreation and stops when the process exits.

No change is made if the option is off. Fullscreen mode is not supported.

## 2. Background sound checkbox (explicitly NOT enabled)

The screen includes a second, visibly disabled checkbox:
`別ウィンドウ操作中も音声を維持（現在未対応）`.

Evidence so far does not establish a safe, general method of preventing the
BVE engine and every ATS/audio plugin from stopping sound when BVE loses
input focus. An experimental Windows Core Audio session API helper caused
an isolated test process to terminate unexpectedly; it was **deleted** from
the development implementation and will not be run alongside BVE.

Windows session unmuting would not necessarily restart an audio engine that
stopped generating sound. No attempt is made to spoof foreground focus,
send fake active-window messages, copy/replace sound plugins, or change
system-wide audio settings.

The official MotorMaster author notes that the v1.2.1 release removed its
own stop-sound-on-deactivation behavior. This is *plugin-specific*:
https://sites.google.com/view/fwchbve/top/bveデーター公開一覧/MotorMaster

## Tests

- `BVELauncher.UI.csproj` Release x64 build: PASS.
- `TestB13WindowOptions.ps1`: PASS. Saves each flag per BVE version into
  a temporary test file; verifies default state; toggles topmost on/off
  on a synthetic, non-BVE window; checks both UI checkboxes exist and
  the background-audio one is disabled; verifies live BVE settings unchanged.
- Existing `TestB11StartupSettings.ps1`: PASS against the candidate UI.
- Existing `TestB11PluginAndMonitorUi.ps1`: PASS.
- Existing `TestB9OtherVehicles.ps1`: PASS.
- Existing `TestPanelViewDiagnostics.ps1`: PASS.

## Remaining limits

Live BVE6 and BVE5 topmost behavior has **not** been verified on real
driving windows. Full background-audio playback remains an open task,
not an implemented feature. Neither feature is in the public release.
