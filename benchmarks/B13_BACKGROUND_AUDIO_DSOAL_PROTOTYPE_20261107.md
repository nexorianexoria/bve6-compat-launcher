# BVE5 / BVE6 バックグラウンド音声：独立検証 (2026-11-07)

## 結論

- BVE5 と BVE6 の両方で、DSOAL をアプリケーションローカルの DirectSound 代替 DLL として実際に読み込む **合成試験に成功**。
- 元の BVE、SlimDX、ATS、ユーザー設定、正式公開版 0.29b13 は変更していない。
- BVE6の京成千葉線シナリオについては、ユーザーが別ウィンドウ操作中の音声継続を確認済み。BVE5やほかのシナリオ・ATSについては引き続き未検証。

## 一次資料

1. Microsoft DirectSound の GLOBALFOCUS 仕様:
   https://learn.microsoft.com/en-us/previous-versions/windows/desktop/ee416818%28v%3Dvs.85%29
2. BVE6 公式版の規約（バイナリ改変禁止）:
   https://bvets.net/en/download/main6.html
3. DSOAL の公式ソースと利用法（DirectSound → OpenAL）:
   https://github.com/kcat/dsoal
4. DSOAL 公式配布（`latest-master`, 2026-11-07）:
   https://github.com/kcat/dsoal/releases/tag/latest-master
5. DxWnd 開発者の過去の32ビット対応範囲説明:
   https://sourceforge.net/p/dxwnd/discussion/general/thread/15378e01a5/

## DSOAL 配布ファイルの照合

- ファイル: `DSOAL.7z`（GitHub公式配布、1,441,556 bytes）
- SHA-256: `51552E3D192D0432DA54DF75E20CF1EE720D7906207D6BEF8F72869CA73C2096`
- GitHub Release API が公開する digest と一致。
- `Win32/dsound.dll` と `Win32/dsoal-aldrv.dll`、`Win64/` の同名 DLL を個別の隔離領域へ抽出。
- 既定の `DSOAL` ビルドを使用し、追加 HRTF プリセットは使っていない。

## 合成試験結果

- 使用環境: Windows 上の実際の BVE5 `SlimDX.dll`（x86）、BVE6 `SlimDX.dll`（x64）。
- DirectSound バッファー `0x00e0` と `0x80e0` の作成、能力取得は双方成功。
- 合成プログラムのロード済みモジュール一覧で、実際にアプリケーションローカルの DSOAL `dsound.dll` と `dsoal-aldrv.dll` が読み込まれていることを確認。
- 合成プログラムは**無音**であり、実際の音声再生、BVE走行、フォーカスを切り替えた際の聞こえ方は未確認。
- 再実行手順: `benchmarks/TestB13DsoalRuntime.ps1`。

## BVEを改変しないための試験用コピー

`benchmarks/PrepareB13BackgroundAudioSandbox.ps1` は、インストール済み BVE のファイルをローカルで**同一バイトのままコピー**し、DSOAL を試験用フォルダー**だけ**に置く。

| 対象 | スタートファイル（Windowsのローカルパス） |
| --- | --- |
| BVE5 | `dist-b13-perf/audio-focus-sandbox/BVE5/Start-BVE-Audio-Test.cmd` |
| BVE6 | `dist-b13-perf/audio-focus-sandbox/BVE6/Start-BVE-Audio-Test.cmd` |

双方のインストール済み BVE と試験用コピーの SHA-256、DSOAL DLL の SHA-256、DLL の PE アーキテクチャが一致することを `TestB13BackgroundAudioSandbox.ps1` で確認した。

**実際に試す際の手順**

1. 通常起動中の BVE を終了する。
2. 対象の `Start-BVE-Audio-Test.cmd` を開く。試験用コピーからBVEを起動する。元の BVE exe には一切追加ファイルを入れない。
3. 走行音が鳴るシナリオで、別ウィンドウをクリックして音声が続くか確認する。比較対象は通常の BVE。警笛、ATS音、モーター音を可能なら個別に確認する。
4. 停止や不具合があれば試験用 BVE を終了し、通常の BVE から起動する。DSOAL は元のインストール先に追加されていない。
5. 成功の判断には、音が鳴るだけでなく BVE5/6・32bit ATS・PanelViewEX・TSHUD・入力・FPSに悪影響がないことも必要。

**注意**: 試験用BVEでも、通常のBVEと同じDocuments配下のユーザー設定を読み込んだり、通常終了時に書き込んだりする可能性は残る。元の実行ファイルは完全に同一だが、設定ファイルの書き込みを一切保証するものではない。

## 開発・公開の状況

- `research/b13-background-audio` は検証用のローカルブランチ。
- 本体のバイナリはGitに追加していない。DSOAL配布物や試験用 BVE コピーもGitには追加していない。
- 正式公開版のUIでは音声項目が無効のまま。別途作った**ローカルの試験版ランチャー**では、検証済みDSOALがある場合に独立した音声チェックボックスを使用できる（初期値オフ）。
- GitHubの既存リリース `v0.29b13` に変更なし。

## 2026-11-08 の実機確認と試験版ランチャー

**BVE6**

- ユーザー本人が、検証用BVE6で「他のところを触っても音が出た」と報告した。
- 実機シナリオ：`mackoy.Keisei.txt`。DSOALの `dsound.dll` と `dsoal-aldrv.dll` の読み込みを確認した。
- 非アクティブ時のWindows音声セッションピーク：単体のDSOAL試験コピーで最大 **0.049390**、開発用ランチャーの `BveBackgroundAudio.StartCore` 経由で最大 **0.048211**。アクティブ側のサンプルは両試験とも0件。数値だけではアクティブ時との比較、すべての運転音の動作を示さない。
- 本体コピーと音声DLLの一致、起動後の元のBVE実行ファイルの非変更、通常のBVE6設定ファイルのバックアップとのハッシュ一致を確認した。

**BVE5**

- DSOALの合成試験とコピーは合格。BVE5の実機試験でも非アクティブ時のWindows音声セッションを検出（最大ピーク **0.088663**）。DSOALログは生成されたが、実機プロセスのモジュール検出は不一致で、ユーザー本人による聴感確認はまだない。
- よって、BVE5も音声維持が解決済みとは判断しない。

**音声試験用ランチャー（正式公開しない）**

- 開発用実行ファイル：
  `dist-b13-perf/audio-preview/BVE6互換ランチャー.exe`
- 検証済みDSOAL DLLは、その実行ファイルと同じフォルダーの
  `audio/Win32` と `audio/Win64` 内。音声DLLや本体コピーはGitに入れない。
- 本体を置き換えず、音声オンの場合だけ一時的なBVEコピーへDSOALを配置して起動。音声オフは通常の起動経路。
- BVE5/6別のオン・オフ保存、最前面表示との独立、SHA-256チェック、コピーの一致、音声オフならコピーしない動作、DLL欠落時に起動を止める動作について自動試験に成功：
  `benchmarks/TestB13BackgroundAudioIntegration.ps1`。
- 試験用コピーは次回のバックグラウンド音声起動時に、BVEが起動していないことを確認してから旧コピーのみ削除する。無関係のフォルダーを削除しない試験に合格した。
- コード：`src/BveBackgroundAudio.cs`。BVE以外のウィンドウへの切り替え時の操作・画面やBveEX、PanelViewEX、TSHUD、32bit ATSとの互換性、起動終了時の安定性は広範囲には未検証。
- **注意：** 実験的なオプションを含むローカル開発版のみ。正式公開版 `v0.29b13` と `main` ブランチは変更しない。

## 2026-11-08 追加実機試験：BVE5 / ATS / PanelViewEX

### BVE5 の DirectSound 読み込みを再検証

- プロセスのビット数に合わせたモジュール確認プログラム
  `benchmarks/B13BveModulesReadOnly.cs` を追加し、x86/x64 を個別にビルド。
- `mackoy.Keisei.txt` をBVE5で試験。アプリケーションローカルの
  **Win32 `dsound.dll` と `dsoal-aldrv.dll` が両方ロードされた**。
- 非アクティブ時の音声セッション：131サンプル中106サンプルで非ゼロ、
  最大ピーク `0.100869`。フォアグラウンドとの比較試験ではない。
- 元のBVE5実行ファイル、ユーザー設定ファイルの不変を確認。
- **ユーザーによるBVE5での聴感確認は未実施**。

### PanelViewEX / BveEX の共存

- BVE6 `_BveExSamples.Basic.Panel.txt`：DSOAL二つのDLL、
  `BveEx.DummyAts.x64.dll`、`PanelView.Native.dll` (x64) を読み込み。
  可視ウィンドウ `PanelViewEX 1.01` と `BVE Trainsim 6` を確認（PID 30584）。
- BVE5 同シナリオ：DSOAL二つのDLL、
  `BveEx.DummyAts.x86.dll`、`PanelView.Native.dll` (x86) を読み込み。
  可視ウィンドウ `PanelViewEX 1.01` と `Bve trainsim 5` を確認（PID 7604）。
- このサンプルの音声セッションはピーク0。無音のため音声維持試験の根拠には使わない。
- BVE6 `mackoy.Keisei.txt`：`KqAts64.dll`、DSOAL、
  PanelView Native (x64) とPanelViewEX 1.01 可視ウィンドウが共存（PID 29272）。
  フォアグラウンド62サンプル（最大 `0.706771`）、
  非アクティブ282サンプル（最大 `0.846580`）で音声出力を検出。
- 各DLLとウィンドウの読み込み・表示は確認したが、
  **全操作・パネル更新・画質・FPS・長時間安定性は未検証**。

### BVE6 と32ビットATS互換ブリッジの同時実行

- シナリオ：`JRTozai2227B.txt`、車両の32ビット `tozai.dll`。
- 実際のランチャーコード `Program.Prepare` で
  `32bit ATSを橋渡し` モードを準備し、`BveBackgroundAudio.StartCore`
  経由でDSOAL付きBVE6コピーを起動した。
- `Bve5VehicleCompatibility.exe` の別プロセスを確認。
  `bridge.log` に `host_elapse_frames=1294`,
  `bridge_elapse_frames=1294`、`HOST_FINISHED` が記録されている。
  エラー・例外等を示す文字列は検出されなかった。
- フォアグラウンド34サンプル（最大 `0.055671`）、
  非アクティブ169サンプル（最大 `0.135230`）で音声出力を検出。
- 元のBVE6実行ファイル・設定は不変。BVE終了後、ATSブリッジも終了。
- **ATS機器の正常な保安装置動作、警報発生時の挙動、走行中の安全性は未検証**。
- 実行用スクリプト:
  `benchmarks/TestB13LiveBveAudio.ps1 -Version 6 -UseLauncherCore -UseCompatPrep -Scenario <path> -ObserveSeconds 25`。

### TsHud の状態と制限

- `C:\Users\Public\Documents\BveEx\2.0\LoadedExtensions.xml` に
  `TsHud.dll` `IsEnabled="true"` を確認。
- `TsHud.dll` の実ファイルは存在し、ファイルバージョン `1.1.0.0`。
- **TsHudの実際の画面描画、更新、操作は確認できていない**。
  設定有効だけで「動作成功」とは判定しない。

### コピーとロールバックの安全確認

- 音声オンのときだけ、元BVEの全ファイルを別ディレクトリへ複製。
  本体配下のリンク先ディレクトリはたどらず、コピーを拒否する。
- コピーはマーカーを持つ試験用フォルダーだけを次回のテスト開始時に整理し、
  同名形式でもマーカーのないフォルダーは削除しない。
- 上記の独立設定、DSOAL検証、音声オフ時の既存起動経路、
  DLL欠落時の安全停止、マーカー付き旧試験フォルダーの削除を
  `benchmarks/TestB13BackgroundAudioIntegration.ps1` で検証済み。
- **一般公開はしない。現行正式公開版 `v0.29b13` に変更なし**。
