# Ver.0.29b17 音声ヘッダー・変換処理のオフライン検証（2026-11-14）

## 発生条件

- b16インストール済みのBVE6互換ランチャーで、三田線1037G軽量版とTKK3020/3121.txtを選択。
- b16の診断記録: `DSOAL_FORMAT_PREFLIGHT known=False references=148 unsupported=34`、理由は`joint/131_nisic1.wav`のRIFFサイズ不一致。ここで検査が停止したため、148は全参照数ではない。
- 元`joint/131_nisic1.wav`は601,496バイト、RIFF宣言内の終端は389,176バイト、末尾212,320バイト。RIFF内は`JUNK(52) / fmt(16, PCM16bit stereo 48kHz, blockAlign4) / bext(604) / data(388460)`で境界が一致。**末尾が再生対象音声であるかは不明。**
- Sound.txt全230参照 / 206種類のうち、24/32bit整数PCM非対応は34参照（34種類）。その34ファイルはすべて`fmt / data / SAUR(32)`の構造で、`SAUR`には文字列`1.8.0.0`とゼロパディングが格納されていた。

## b17の実装範囲

- `src/BveDsoalCompatibility.cs`: RIFF外の末尾データが存在しても、**RIFF内のチャンクが完結**しており、形式・データ境界・チャンクの順序・末尾バイト数などの検査に合格した、既知の対応形式に限って、形式判定を継続。RIFF外の末尾は音声へ追加しない。検査ログ`DSOAL_RIFF_TRAILER`を記録。
- `src/BvePcmFloatStaging.cs`: 検査済みのWavosaur 1.8.0.0形式の`SAUR`（32バイト）に限定して、変換後の作業WAVへチャンクをそのまま複製。メタデータをPCM音声として扱わず、その他の不明な追加チャンクは引き続き拒否。元WAVは読み取り専用で開く。
- `src/Release.cs`: `0.29b17 / 0.2.9.28`。

## 実行済み検証と結果

1. Release構成のC#ビルド成功。
2. 実データの`joint/131_nisic1.wav`に対する形式判定は対応PCM16として正常終了（元ファイルSHA-256: `81B1BAE6BEC096C95FB1B764CB7C3BDADAA7BD3386E16303ADC3AA2E51A5C1A6`）。
3. 上記シナリオの事前検査は`known=True / 230参照 / 206種類 / 非対応34参照`。
4. `Program.Prepare`で新規作成した**ランチャー専用作業コピー**に対する`BvePcmFloatStaging.StagePreparedScenario`は34ファイル変換に成功。別途生成した`scenario.float32.txt`の事前検査は`known=True / 非対応0参照`。
5. 10ケースの合成WAV（正常PCM16、RIFF外に整列済み末尾、未整列末尾、末尾の直前に追加チャンク、blockAlign不正、RIFF宣言長過大、正常PCM24、PCM24+末尾、正常float32、data欠落）で、期待した受容・拒否を確認。
6. 配布ZIP内10ファイルのSHA-256、package-index、外部ZIPのSHA-256が一致。テスト用別フォルダーへInstallEngine経由のインストール成功。
7. Defenderが有効な状態で新しいb17インストーラー・本体・2種類のZIPに個別スキャンを実施し、新たな脅威の検出なし（ただし安全性の保証ではない）。

## 制限と未確認事項

- BVE6の実走行や音が正しく再生されることは、このオフライン検査では確認できない。DSOALの音声割当や音質の確認は未実施。
- 元のRIFF外212,320バイトの意味は不明。音声としての連結や追加は一切行わない。
- b15で検出された`Trojan:Win32/Wacatac.H!ml`の原因は未確定。マルウェアの不存在や誤検知の証明にはならない。
- b14への自動更新案内はまだ変更していない。b17は手動インストール用の試験版である。

RIFFのチャンク構造: https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-

`SAUR`についてのWavosaur開発者の説明: https://www.wavosaur.com/forum/click-at-end-of-sounds-t315.html
