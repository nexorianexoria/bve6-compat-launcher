## b5の追加計測：長時間処理とFPSの時刻照合

この機能は**開発版のみ**です。公開済みのb4と自動更新設定は変更しません。

### 計測内容
- ATS互換処理のencode/send/receive/decodeのいずれかが5ms以上の場合、低頻度イベントをメモリ内に保持します。
- 各イベントにフレーム番号、処理段階、所要時間、Windows QPC上での開始・終了推定時刻、QPC周波数を記録します。
- Dispose時にbridge.logへSLOW_PHASEとSLOW_PHASE_SUMMARYを書き込みます。運転中にこのイベントをディスクへ逐次記録しません。
- 記録上限は512イベントで、超過はdroppedとして計上します。
- UTC値は採取時刻の補助情報であり、厳密な比較はQPCで行います。ステージ開始・終了QPC値はchronoとQPCから推定した値なので、微小な計測誤差があります。

### PresentMonとの照合

PresentMon 2.5.1では--qpc_timeオプションによりCPUStartQPC列が出力されます。この値は、描画処理開始時刻であり、画面への表示時刻そのものではありません。

管理者PowerShellを開き、BVE運転画面が表示された状態で以下を実行します。

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\benchmarks\CaptureB5Timing.ps1" -Seconds 240

計測中にBVEを終了した場合も計測を停止します。計測が終わったらBVEを通常終了し、出力したb5-qpc-presentmon.csvと実行フォルダーの作業データ内にあるbridge.logを使って照合します。

    python ".\benchmarks\CorrelateBveStalls.py" --csv "FPS CSVのパス" --bridge-log "bridge.logのパス"

描画系列が複数あるときは、運転画面の系列を別途確認して--swapchainを指定します。異なるSwapChainAddressの数値を合算しません。

### 検証状況

- 模擬32bit ATSで25msの処理遅延を発生させ、500回の通信を確認。SLOW_PHASEが1件記録されました。
- その記録のQPC値から作ったテスト用PresentMon CSVとの時刻照合テスト、系列を分離するテスト、QPCのないCSVを拒否するテストに成功。
- 実際のBVEによる新方式のFPS同時計測はまだ未実施です。
- 2026-11-01の旧b5実走行記録にあった約647msの送信遅延には、新しいQPC記録が付いていません。過去の遅延が画面のカクつきと一致したかを後から断定することはできません。
- 5ms以上の処理の発生と、PresentMonの表示間隔が同じ時刻帯でも、原因関係は別途検証が必要です。
### 次段階：32ビットATSホスト内部の時間分解

- ホスト側のparse（受信データ解析）、ats（ATSのElapse関数）、diagnostics（診断集計）、encode（返信データ生成）、send（返信書き込み）を分けて記録します。
- METRICS host_phase として平均・最大時間をBVE終了時に出力します。
- 各段階で5ms以上かかった場合、SLOW_HOST_PHASE としてWindows QPCの開始・終了時刻を最大512件記録します。
- フロントエンドのSLOW_PHASEとホストのSLOW_HOST_PHASEは同じWindows QPCクロックを利用します。日時の一致だけではATS処理が原因とは断定できません。
- 2026-11-01の実走行45秒記録には、確認できた5ms以上の遅延3件がすべて収録範囲外でした。標準のFPS計測時間を240秒に拡張し、BVEの早期終了も検知するようにしました。
- 既存実走行に対して今回のホスト内部計測はまだ行っていません。実際のFPS改善もまだ未確認です。
