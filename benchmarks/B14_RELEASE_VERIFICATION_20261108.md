# Ver.0.29b14 公開前の検証記録（2026-11-08）

## 結論

- b13からb14へのオフライン自動更新に成功。旧版のインストール処理が受け付けるファイル構成は維持した。
- バックグラウンド音声用のDSOAL DLLはb14ランチャー実行ファイルに埋め込み、オンのときだけBVEの隔離コピーへ展開する。
- BVE5・BVE6ともb14に埋め込んだDLLの読み込みと、非アクティブ時のWindows音声出力を確認した。
- 元のBVE本体・設定ファイルは変更していない。

## パッケージ

- 表示バージョン `0.29b14`、ファイルバージョン `0.2.9.25`。
- 検証用スクリプト：`BuildB14Release.ps1`、`VerifyB14Release.ps1`、`TestB13ToB14Upgrade.ps1`、`TestB14BackgroundAudioIntegration.ps1`。
- b13と同一のインストールファイル許可リスト11個を維持し、古い更新プログラムによるb14導入をテストした。
- DSOALのWin32とWin64の計4ファイルを埋め込み、正規の配布物のSHA-256に一致することを確認した。
- DSOAL・OpenAL Softのライセンス全文と出典は`THIRD_PARTY_NOTICES.txt`に掲載した。

## 実機確認

- BVE6、京成千葉線`mackoy.Keisei.txt`：b14ランチャーの`BveBackgroundAudio.StartCore`から起動。DSOAL二つのDLLの読み込み成功。非アクティブ時121サンプル中102サンプルに音声があり、最大ピーク`0.095775`。
- BVE5、同シナリオ：同様に32ビットDSOALの読み込み成功。非アクティブ時121サンプル中93サンプルに音声があり、最大ピーク`0.080138`。
- 両方ともユーザー設定の変更なし、元BVE実行ファイルの変更なし、試験用プロセスの終了を確認。
- 開発版b13の時点では、BVE6の`tozai.dll`（32ビットATS）のブリッジ処理とDSOALの同時使用、BVE5/6のPanelViewEXとの同時起動も確認した。
- **TsHudの描画・長時間安定性・すべてのATSや車両データの正常動作は未確認。** そのため音声は初期設定オフで独立した試験機能とする。

## 出典

- DSOAL公式：<https://github.com/kcat/dsoal>
- 取得した配布版：<https://github.com/kcat/dsoal/releases/tag/latest-master>
- OpenAL Soft公式：<https://github.com/kcat/openal-soft>
- 音声ライブラリの配布元アーカイブSHA-256：`51552E3D192D0432DA54DF75E20CF1EE720D7906207D6BEF8F72869CA73C2096`
