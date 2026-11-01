# BVE互換ランチャー Ver.0.29b5 — 性能調査用開発版

この版はまだ公開版ではありません。b4の機能を引き継ぎ、32ビットATSをBVE6へ橋渡しする処理の所要時間を詳しく調査するための開発版です。**FPS向上は未確認**です。

## 実装したこと
- バージョン表示をVer.0.29b5へ更新。
- 32ビットATS互換起動時、環境変数BVE5COMPAT_PROFILE=1を設定して計測を有効化。
- ATSのElapse呼び出しで、encode（送信前変換）・send（送信）・receive（応答受信／待機）・decode（受信後変換）・core（上記全体）の所要時間を集計。
- 計測結果はBVE終了時にbridge.logへ出力。各段階の回数、平均・最大ナノ秒数、1/5/16/33ms超の回数を記録。
- 既存のMETRICS bridge_elapse_framesおよびhost_elapse_framesは維持。
- benchmarks/AnalyzeBridgeMetrics.pyを更新し、新形式のログを段階ごとに見られるようにした。

receiveにはイベント処理、Windowsパイプの読み取り・待機、バッファ処理が含まれます。純粋な通信遅延だけではありません。coreの測定と従来のbridge_elapseの測定範囲は異なります。各値は実行全体の平均であり、個別のカクつきと直接結びつけることはできません。

## テストで確認したこと
- VS2022のv143ツールセット指定で、x64のfrontend DLL、Win32のbackend EXE、.NET Framework 4.8ランチャーをビルド成功。
- 32ビットの模擬ATSで500回と2000回のElapse往復を実行し、ハンドル・パネル・音声の値が維持されることを検証。
- 計測有効時に5種類のログが出力され、計測無効時には出力されないことを確認。
- 応答受信用Windowsイベントの再利用を試したが、模擬ATSの比較では遅くなったため**採用せず元に戻した**。復元後のテストも成功した。
- 実際のBVEでのb4とのFPS比較・ATS互換性試験は未実施。

## 計測ログの表示

PowerShellで開発ソースのルートへ移動し、以下を実行してください。

    python .\benchmarks\AnalyzeBridgeMetrics.py --directory "$env:LOCALAPPDATA\BVE6互換ランチャー\作業データ" --limit 5

bridge.logはユーザーのローカル環境に保存され、個人のファイルパスを含む可能性があります。公開時には内容を確認してください。

## 開発方針

- b4（v0.29b4）の公開物・タグ・b3自動更新情報は変更しない。
- b5はまず実測を優先し、性能改善を数値で裏付けられた場合だけ実装する。
- 互換性に影響しそうな最適化は、実車両・実走行による確認を完了するまで一般公開しない。
## b5開発版の追加：遅延発生時刻の記録

5ms以上のATS処理遅延の発生区間をQPCで記録し、PresentMonのCPUStartQPC列と比較するツールを追加しました。詳細はbenchmarks/B5_SLOWTRACE_GUIDE.mdを参照してください。b4公開版は変更していません。実際のBVEでの同時計測は未実施です。

## b5内部解析拡張（2026-11-01）

32ビットATSホストのparse/ats/diagnostics/encode/sendを分割計測し、5ms以上の遅延にはQPC時刻を付けました。模擬ATSによる往復テストで前後双方のイベントが同じフレームで記録されたことを確認済み。PowerShellの標準FPS収録時間を45秒から240秒に変更しました。BVEが終了した場合は早く記録を止める指定も追加しています。

この機能は原因調査用で、BVE本体や車両ATSのアルゴリズム、Windows/GPU設定は変更しません。b4配布版は変更していません。実走行でのFPS向上や互換性の検証は今後の課題です。

## ATS処理の調査と未使用コード整理（開発版）

保存済み240秒のデータを再分析し、ATS本体で5msを超えた区間と描画間隔50ms超の時間的関連を確認しました（原因と断定はできません）。不要なパネル出力比較を削除し、模擬ATSで通信結果の一致を確認しました。計測時間の差は小さく、実際のFPS改善は未証明です。詳細はbenchmarks/B5_OPTIMIZATION_TRIAL.mdとB5_GOAL_ANALYSIS_20261101.mdを参照してください。

## 計測後の自動解析と性能比較（b5）

CaptureB5Timing.ps1 の -AnalyzeAfterExit でBVEの正常終了後にFPS/ATSの時間照合を実行できる。BVEのPID・時刻・CSVのProcessIDを照合し、異なる実行ログは自動で選ばない。5ms超のATS遅延が0件の正常なログにも対応した。配列16進変換の最適化試験は性能向上が確認できなかったため撤回。詳しくはbenchmarks/B5_AUTO_ANALYSIS_REPORT.mdを参照。FPS向上は未確認。

## ワンクリックBVE起動・FPS計測・自動解析（b5開発版）

BVE6-OneClick-Test.cmdをダブルクリックすると、管理者権限を求めるのはPresentMon計測のみとし、ゲームは通常権限で自動起動する。前回のシナリオに指定された既定車両で起動し、PIDを正確に引き継いでFPS計測、BVEの通常終了後にレポート作成まで行う。初回だけシナリオの設定が必要。BVE実走行での一連の操作は未検証、乾式起動テストは成功。詳細はbenchmarks/B5_ONECLICK_GUIDE.md。

## 描画開始の自動検出・Elapse後段の遅延計測（開発版）

一部の240秒実走行では描画サンプルが約119秒分しかなく、ワンクリック計測でPresentMonが実フレームを検出してから240秒の本計測を開始する方式へ変更。ATS橋渡しのframe_log、sound_log、post、fullを追加し、従来のcore計測外で発生する遅延を特定しやすくした。模擬ATS・パーサ・実CSVサンプルの検証済み。実際のBVEでの新方式の通し計測はまだ未実施。詳細はbenchmarks/B5_PRESENT_START_AND_POST_TIMING.md。

## 231ms送信遅延の切り分け（開発版）

32ビットATSホスト側の標準入力待ち時間をWindows QPCで記録し、x64側のWriteFile遅延とフレーム番号・時刻で照合する分析機能を追加。通常の入力待ちも含まれるため、パイプの詰まりの証明ではない。模擬ATS・単体テスト・旧ログの解析互換性は検証済み。FPS向上・実走行での遅延解消は未確認。詳細はbenchmarks/B5_PIPE_WAIT_INVESTIGATION.md。

## 計測開始のATS初回Elapse待機・CPU/GPU描画系列別レポート

一部の実走行で本計測開始から最初のPresentMon描画まで約31秒の空白があったため、b5のワンクリック計測開始前にATS初回Elapseと描画の両方を確認する仕組みを追加。ATSホストは最初のElapse完了時に一度だけログへ記録する。CSVの描画系列ごとのCPU/GPU Busyを比較するb5-cpu-gpu-frames.mdも自動生成する。模擬ATSと旧実走行データで検証済み。新方式の実BVE通し試験は未実施、FPS改善未確認。詳細はbenchmarks/B5_FIRST_ATS_AND_CPU_REPORT.md。

## 定期診断ログのメモリバッファ化（開発版）

300フレームごとのFRONTEND_FRAMEと音声状態変化時のFRONTEND_SOUND_RETURNを、Elapse中にファイルへ直接書き込む方式から、上限4096行のメモリに蓄積して通常終了時のDisposeで一括保存する方式へ変更。保存数・破棄数をログに記録し、自動解析で欠落時に警告を表示。模擬ATS10万フレームの再試験を通過。実BVEでのFPS改善は未検証。詳細はbenchmarks/B5_BUFFERED_DIAGNOSTICS.md。

## BVEを起動しない自動互換テストと診断ログA/B比較

毎回240秒の実走行を行う必要を減らすため、RunB5OfflineChecks.ps1とCompareB5BufferedDiagnostics.pyを追加。C++/C#ビルド、模擬ATS・Python回帰試験、旧版と改良版の交互A/B計測を一括実行。旧3回・新3回（各12000フレーム）でframe_log平均の中央値が1.048μsから0.250μsへ。ブリッジ全体の実行時間は同程度、実運転FPSの改善は未証明。詳細はbenchmarks/B5_OFFLINE_AB_GUIDE.md。

## オフライン運転周期とATS一時停止の模擬試験

BVE本体を起動せず、模擬ATSを16,667μsと33,333μsの周期で動作させ、ATS本体の85ms待機と運転側の120ms待機を別々に再現する回帰試験を追加。既存のRunB5OfflineChecks.ps1へ統合し、毎回の実BVE運転テスト負担を軽減。模擬テストのみであり実ゲームのFPS改善は未確認。詳細はbenchmarks/B5_CADENCE_STALL_GUIDE.md。

## 32ビットATSホスト側のブレーキ音声診断ログのバッファ化

ATSホストのElapse処理中にブレーキ音声150～167が状態変化するたび発生していたBC_SOUNDファイル出力を、4096件上限のメモリ記録へ移行し、Disposeでまとめて保存する。音声/パネル/ATS通信仕様は変更しない。未保存数はBC_SOUND_BUFFER_SUMMARYで報告。模擬ATSの446件記録、上限超過921件、profile ON/OFF、60Hz/30Hz相当の回帰試験は成功。実BVEのFPS改善は未検証。詳細はbenchmarks/B5_HOST_BRAKE_SOUND_BUFFER.md。

## ATSパネル・音声配列の変換時メモリ割り当て削減

Elapse通信で使う256要素の配列について、2048文字の一時std::stringを生成する方式を、固定長スタック配列からストリームへ直接書き込む方式へ変更。読み込みもstreambufから直接復元する。フロントエンド・ATSホストの往復で、配列用の一時std::stringを最大8個/フレーム削減。プロトコルとデータ値は変更せず、固定長streambufによるINT_MIN/INT_MAX、全256要素、不正文字、短い入力、区切り文字の回帰試験を追加。全オフライン検証成功。実BVEのFPS改善は未確認。詳細はbenchmarks/B5_FAST_ARRAY_CODEC.md。

## Elapse通信の固定長ストリーム化とイベント再利用

Elapse要求・応答のostringstream/istringstreamと一時std::stringを固定長8192バイトのstreambufへ置換。x86ホストの入力行バッファを再利用し、フロントエンドのOVERLAPPED読取イベントをATSセッション中に再利用。WriteFileの部分書き込みにも対応。従来プロトコルとのバイト一致、配列・数値解析、容量超過、3,000フレーム模擬通信の回帰テストを追加。同一模擬ATSによる前コミット比較を10,000フレーム×5組で実施し、ブリッジ往復平均の中央値は160usから152usへ低下した。単体マイクロベンチでの差は小さく、実BVEのFPS向上は未検証。詳細はbenchmarks/B5_FIXED_PROTOCOL_STREAMS.md。
