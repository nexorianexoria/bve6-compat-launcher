# Ver.0.29b5 インストーラー・起動時更新確認の復旧

## 公開ZIP初版からの修正

- 開発完了時のポータブル版に、従来配布していたインストーラーが含まれていなかったため、ダウンロード用の BVE6Launcher-Setup.exe を改めてビルドする。
- ランチャー起動時の Shown イベントから AutoUpdate.Check(this) を呼び、更新先の update.json を非同期で確認する動作を復旧する。
- 新しいバージョンがある場合にのみ UpdatePrompt.Show を表示し、更新するか、今はしないかを利用者に確認する。
- インストール版で更新を選択した場合は、導入・更新.exe を起動し、検証済みの更新ZIPを取得して適用する。更新後の自動起動は任意。
- ポータブル版では更新の案内が出たときに最新リリースのダウンロードページを開く。
- BVE5/BVE6の起動処理、ATS通信、ログの通常時PROFILE=0は変更しない。

## 更新パッケージ

既存の InstallEngine の12エントリーの形式に合わせる。

- BVE6互換ランチャー.exe
- 導入・更新.exe
- LauncherStarter.exe
- Uninstall.exe
- launcher.ico
- LICENSE.txt
- README.txt
- THIRD_PARTY_NOTICES.txt
- bridge/Bve5VehicleCompatibility.exe
- bridge/Bve5VehicleCompatibility_.dll
- bridge/LICENSE.txt
- package-index.json

package-index.json に各実ファイルのSHA256を保存し、配布の update.json と package.json にはZIP全体のSHA256とバージョン0.2.9.16を掲載する。既存のb4リリースや過去のインストールデータは削除しない。

## オフライン検証

- 起動時更新確認の呼び出しと、新版のみ更新確認ダイアログを出す処理のユニットテストを追加。
- .NET Framework 4.8のランチャー・導入・更新・スターター・アンインストーラーをReleaseビルド。
- リンク・構成・サイズ・バージョン・SHA256を検証したZIPを使用する。
- 開発フォルダー内の専用一時フォルダーに対するInstallEngine.Installの成功、現在.txtとLauncherStarter.exeのSHA256整合性を検証。異なるSHA256のパッケージは受け付けないことも確認する。
- 実際のBVEはこの検証では起動しない。

## 公開後の確認

- 最新GitHub ReleasesにインストーラーとポータブルZIPを掲載する。
- raw.githubusercontent.com 上の update.json と package.json がb5と正しいSHA256を示していることを確認する。
- 既存のb3/b4リリースを残す。