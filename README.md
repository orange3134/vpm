# pipipigiken Packages

Unity・VRChat向けツールの共通VPM配布リストです。

**[配布ページ](https://vpm.pipipigiken.jp/)** · **VCC追加URL：`https://vpm.pipipigiken.jp/vpm.json`**

## リポジトリの役割

| リポジトリ | 内容 |
|---|---|
| [orange3134/vpm](https://github.com/orange3134/vpm)（ここ） | 共通配布一覧・Webページ・一覧更新処理 |
| [orange3134/meishi-pop-exporter](https://github.com/orange3134/meishi-pop-exporter) | MEISHI Pop Exporterのソース・VPM用ZIP・Unitypackage・Release |

このリポジトリにはツールのソースや配布用Releaseを置きません。MEISHI Popアプリ本体は非公開のままです。

## 新しいツールを追加する

1. ツール専用の公開リポジトリを用意します（例：`orange3134/new-tool`）。非公開の開発プロジェクトがある場合は、公開対象のパッケージだけを配布リポジトリへコピーします。
2. 固有のパッケージID（例：`jp.pipipigiken.new-tool`）とバージョンを設定し、**ZIP直下にpackage.jsonがあるVPMパッケージ**をGitHub Releaseへ添付します。[公式パッケージテンプレート](https://github.com/vrchat-community/template-package)も利用できます。Unitypackageも配布する場合は、そのツールのリリース処理で生成します。
3. `source.json` の `githubRepos` に1行追加してpushします。

```json
"githubRepos": [
  "orange3134/meishi-pop-exporter",
  "orange3134/new-tool"
]
```

**Build Repo Listing** が各リポジトリの公開ReleaseからVPM ZIPを収集し、SHA-256を計算して共通の `vpm.json` へ反映します。配布ページの Packages は `Website/index.html` にリンクを追加してください。独自の `vpm-release.json` は不要です。

- ツールごとに独立したバージョン番号を使えます。
- 一覧のID `jp.pipipigiken.vpm` と追加URLは維持します。登録済みの利用者は、VCCで一覧を更新すると新しいツールを選べます。
- Draft / PrereleaseのReleaseは対象外です。安定版SemVerを使用してください。ZIPは1ファイル256 MiBまでです。
- Unitypackage単体、VPM形式ではないZIP、GitHub自動生成ソースアーカイブは取り込みません。
- 他の配布元のパッケージへ依存する場合は、その配布元もVCCへ登録する必要があります。

## 更新タイミング

**約1時間ごとに自動チェックします。** `source.json` やWebページの変更をpushした場合も更新します。GitHubの混雑等で定期実行が遅れる場合があります。

すぐ反映したい場合は、[Actions → Build Repo Listing](https://github.com/orange3134/vpm/actions/workflows/build-listing.yml) で **Run workflow** を実行してください。CLIの場合：

```bash
gh workflow run build-listing.yml --repo orange3134/vpm
```

GitHubの仕様により、公開リポジトリで長期間の無操作が続くと定期実行が無効になる場合があります。新しいツールを公開するときはActionsの状態を確認してください。

標準GitHub-hostedランナーを使う公開リポジトリのActions実行時間は無料です。[GitHub公式説明](https://docs.github.com/en/billing/concepts/product-billing/github-actions)

## 一覧生成と検証

```bash
python3 -m unittest discover -s scripts -p 'test_*.py' -v
python3 scripts/build_listing.py
```

Python 3.10以降の標準ライブラリで動作します。`Website/vpm.json` と互換用の `Website/index.json` を生成します。正式なVCC追加URLは `vpm.json` です。MacのPythonで証明書ストアが未設定なら、適切なCA証明書ストアを設定してください。証明書検証は無効化しません。

公開済みのバージョンが消えたり、ダウンロードURL・ハッシュが変わった場合は公開を停止します。重複したパッケージID・バージョンも拒否します。

### 初期リリースの移行記録

2026-09-09、利用開始前の整理として、ユーザーの明示的な指示でMEISHI Pop v0.2.2のソース・Releaseを `meishi-pop-exporter` へ完全移行しました。旧Releaseと旧タグはここに残しません。

`release-migrations.json` は、その際に承認された**2パッケージ・各1バージョンの旧URL/ハッシュと新URL/ハッシュの正確な対応表**です。一般的な上書きを許可する設定ではありません。移行前の一覧がCDNに残っている間も、この1回の移行だけを検証して反映できます。今後の通常リリースではバージョンを上げてください。

## 独自ドメイン

- GitHub Pagesの公開元は **GitHub Actions**。
- Custom domain：`vpm.pipipigiken.jp`
- XserverのDNS：**CNAME `vpm` → `orange3134.github.io`**（TTL 3600）
- **Enforce HTTPS** 有効。

公式 [template-package-listing](https://github.com/vrchat-community/template-package-listing) を元に、複数リポジトリの全公開バージョンを収集する構成にしています。
