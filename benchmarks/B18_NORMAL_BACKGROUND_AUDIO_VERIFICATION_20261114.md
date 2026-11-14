# Ver.0.29b18 BVE6通常起動のバックグラウンド音声維持・オフライン検証

## 利用者の指定

Ver.0.29b17はBVE6互換起動では起動できる一方、BVE6通常起動で32bit整数PCM34件が検出されて標準DirectSoundへの切替確認が表示された。利用者は**標準DirectSoundへ切り替えず、バックグラウンド音声と元のデータを保持すること**を明示した。

## b18の変更

- `src/BveDsoalCompatibility.cs`に通常起動専用の`NeedsNormalPcmStaging`判定を追加。互換モードの既存分岐は維持。
- `src/BvePcmFloatStaging.cs`に`StageNormalPreparedScenario`追加。ランチャー専用の**新しい作業フォルダー**へ通常起動のシナリオ・車両・Sound.txtの参照コピーをCreateNewで書き出す。元の車両/ATS/Panel等のパスを絶対パスへ解決し、元の資産は読み取りのみ。BveEXのXMLサイドカーがある場合は元のプラグイン参照を維持した作業コピーを作成。
- 作業コピー内の32bit整数PCM WAVのみ既存`StagePreparedScenario`でfloat32へ変換。BVE6通常起動・バックグラウンド音声ONを維持する。音声が不明・変換失敗の場合は標準DirectSoundへの自動切替を行わず、元データと設定を維持して起動中止。
- `src/Release.cs`を`0.29b18 / 0.2.9.29`に更新。

## 検証結果

1. Release C#ビルドに成功。
2. 実際の三田線1037G軽量版シナリオと東急3020系（`TKK3020/3121.txt`）で`PrepareNormal`と`StageNormalPreparedScenario`を順に実行。WAV34ファイル変換・Sound.txtの34参照変更に成功。代替シナリオは`known=True / unsupported=0`。
3. 代替車両の`ATS64`は元の`TKK3020/PluginConverter_3121.dll`のパスを維持。元シナリオ・車両・Sound.txt・WAVのSHA-256は検証前後で一致。
4. モード分岐4ケースのオフライン判定テスト：通常起動＋背景音声ONは通常用変換、互換起動＋ONは互換用変換、通常起動＋OFFとBVE5起動はどちらも変換なし。全件PASS。
5. 配布ZIPの10ファイルの内部SHA-256とpackage-index.jsonが一致。Releaseに含まれる冗長な`導入・更新.exe`は0件。
6. インストール用ZIPを使った別のテスト用フォルダーへの`InstallEngine.Install`は成功。通常のユーザーインストール先には書き込みなし。
7. Defenderリアルタイム保護・アンチウイルスを有効にしたまま、b18インストーラー・本体・パッケージZIP・ポータブルZIPを個別検査。新たな脅威検出なし。

## 未確認

- 実機のBVE6で、BVE6通常起動によりバックグラウンド音声が正常に鳴るか・音質が適切かは未確認。
- 整数32bit PCMからfloat32への変換は一時コピーへの処理であり、すべてのサンプルの値が完全一致するとは限らない。
- b15で起きたDefenderの`Trojan:Win32/Wacatac.H!ml`検出原因は未確定であり、本検査は安全性の証明ではない。
- インストール済みの安定版向け自動更新`update.json`は`0.29b14`のまま。
