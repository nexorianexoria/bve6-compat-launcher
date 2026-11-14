# Ver.0.29b15 セキュリティ検証結果（2026年11月14日）

**結論：安全性の判定は未完了です。v0.29b15は実機利用者による検証用にポータブルZIPとインストーラーを先行公開しました。ただし、インストール時のDefender検出・失敗のおそれは残ります。Windows Defenderが脅威を検出した場合は使用を中止してください。**

## 確認した事実

- Microsoft DefenderはZIPの一時展開中のランチャーEXEと、別の配布候補に含まれる`導入・更新.exe`を`Trojan:Win32/Wacatac.H!ml`（脅威ID `2147814523`）として検出しました。検疫したファイルを復元したり、Defenderの保護を無効化したりはしていません。
- 本体のReleaseビルドを直接カスタムスキャンしたときは新たな検出が記録されませんでした。これは安全性を証明しません。
- 本体のSHA-256は実機で動作を確認した研究版と一致しました：`C03DD2405CF7EF395D39FBD1D7BEB4D1313558D08297393516A4131EDB233D1D`。
- `src/BveBackgroundAudio.cs`に登録された4つのDSOAL DLLのSHA-256は、同梱ファイルの値と一致しました。この検証はファイルの内部整合性を示すもので、第三者による安全保証ではありません。
- b14からb15へのソース差分を確認しました。今回追加された音声処理の中に、新しい任意の外部通信先、レジストリ自動起動登録、PowerShell起動、任意プログラムのダウンロード・実行は見つかりませんでした。既存の正規更新機構ではGitHubの固定されたHTTPS通信先を使用します。
- 実行ファイルにはデジタル署名がありません。VirusTotalの公開済みレポートには当該SHA-256が見つからず、ファイル送信による解析は未実施です。
- Microsoftによる誤検知判定は得られていません。したがって、トロイの木馬であるとも誤検知であるとも確定できません。

## 公開・インストールについて

自動更新用`update.json`は安定版`0.29b14`のままとし、既存インストールからのb15自動更新は始めていません。検証用に[b15ポータブルZIP](downloads/0.29b15/BVE6Launcher-0.29b15-Portable.zip)と[インストーラー](https://github.com/nexorianexoria/bve6-compat-launcher/releases/download/v0.29b15/BVE6Launcher-Setup.exe)を用意しました。インストーラーは[b15パッケージ](downloads/0.29b15/BVE6Launcher-package.zip)を取得して導入します。**インストーラーでの実インストール完了は未検証**であり、配布物のハッシュ一致は安全判定ではありません。最新の安定版は[Releases / latest](https://github.com/nexorianexoria/bve6-compat-launcher/releases/latest)から確認してください。

ポータブルZIPのSHA-256：`A8CF2DC93D6231D6B1D61480EF9B0ACA86F5715D9D4D66B32F2EF528ACFA6B82`。ZIP内のランチャーEXEのSHA-256は、上記で示した実機テスト済みバイナリと一致しています。

インストーラーのSHA-256：`F3B9B823FD87C6965DBC5A48D5EE8F316EF18BBE9F5FBD0D433E5B8C9FE594F0`。導入用パッケージのSHA-256：`091DD6C3EE867AA79281CDF7EB569E8A5C4A8D633B05399FFC457069D2F0C799`。ZIP内の12エントリ、11配布ファイルの内部SHA-256は検証済みです。

警告が発生した実行ファイルを使うためにDefenderを無効化したり、例外登録をしたりしないでください。

**SmartScreenとウイルス検出は別です。** 個人開発の未署名アプリを起動するときにSmartScreenが「認識されないアプリ」「不明な発行元」と表示されることはあります。この警告だけでは感染を意味しませんが、アプリが安全だという意味でもありません。今回の`Trojan:Win32/Wacatac.H!ml`の検出については依然として原因未確定です。

## 今後の確認

Microsoftの[ファイル分析申請](https://www.microsoft.com/en-us/wdsi/filesubmission)による判定を得ていません。**分析への提出と安全性判定は未完了です。**
