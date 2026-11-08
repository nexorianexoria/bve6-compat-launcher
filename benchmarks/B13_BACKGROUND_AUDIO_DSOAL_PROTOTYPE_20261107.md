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
