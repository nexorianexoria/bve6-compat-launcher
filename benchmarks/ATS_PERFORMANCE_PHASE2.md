# 次フェーズ：ATS互換ブリッジの処理時間調査（非公開開発用）

## 確認した実測値

2026-10-31のBVE起動ログに、以下のMETRICSが記録されている。

- run-20261031-212108-1a2d17: 15,760回。backend ATS Elapse平均1,685us、frontend bridge Elapse往復平均2,071us、平均値の差386us。最大値はbackend 43,719us、往復44,269us。
- run-20261031-210518-601a3b: 24,270回。backend ATS Elapse平均1,599us、frontend bridge Elapse往復平均2,002us、平均値の差403us。最大値はbackend 29,574us、往復34,227us。
- run-20261031-201730-6822e0: 7,798回。backend ATS Elapse平均43us、frontend bridge Elapse往復平均407us、平均値の差364us。

数値の出典は各実行のbridge.log内のMETRICS行。平均値の差には通信、データ変換、待機、追加処理が含まれうるが、現時点で個々の要素に分解できない。各最大値は別のフレームの可能性があり、最大値同士の引き算はしない。

PresentMonで得られた運転画面候補の約29〜33ms/フレームと、上記の約2ms/往復は測定時間の範囲が異なる。両者の差が特定の処理の所要時間であるとは判断できない。使用したATSが原因と断定できない。

## コードで確認した経路

- src/bridge/frontend/dllmain.cpp: Elapseはデータを文字列へ変換し、パイプに送信、応答をWaitForSingleObjectを使って受信する。呼び出し全体を集計してDispose時にMETRICSとして記録。
- src/bridge/backend/exemain.cpp: ATSのElapse呼び出し前後で時間を集計して終了時にMETRICSとして記録。
- frontend側receive_from_backendは各呼び出しごとにWindowsイベントオブジェクトを作成・閉鎖しているが、これがどの程度負荷に寄与するか未計測。

## 次に確認する項目

1. 往復時間を送信、待機/応答、逆変換に分けて計測する（計測データの書き込みは終了時にまとめる）。
2. 平均値に加えて遅いフレームの分布を記録する。
3. 同一路線・同一車両・同一視点での比較を行い、実FPSとブリッジ計測の対応を確認する。
4. ATSを使用しない通常起動と、互換起動の比較を行う際は、同じ車両が正常動作するかを事前に確認する。

この開発ブランチは公開済みb4と別扱いとし、検証なしで既存のBVE本体や公開配布物に変更を加えない。