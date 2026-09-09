# MEISHI Pop Exporter

VRChatアバターのUnityプロジェクトから、MEISHI Pop!用の `.mpavatar` を書き出すEditor拡張です。公開テスト版です。

**[配布ページ](https://vpm.pipipigiken.jp/)** · **[Unitypackage](https://github.com/orange3134/vpm/releases/latest)**

## インストール

### VCC / VPM（おすすめ）

1. VRCSDK Avatars導入済みのプロジェクトを用意し、Unityを閉じます。VCCでの導入・更新後にUnityを開き直します。
2. [Modular Avatar / NDMFの公式配布元](https://modular-avatar.nadena.dev/ja/docs/intro)をVCCに追加します。NDMFが必要です。Modular Avatarはアバターが使用している場合に必要です。
3. [配布ページ](https://vpm.pipipigiken.jp/)の「VCCに追加する」を押します。手動の場合はVCCの Settings → Packages → Add Repository に下記URLを指定します。

   `https://vpm.pipipigiken.jp/vpm.json`

4. 対象プロジェクトの Manage Project で **MEISHI Pop Exporter** を追加します。**MEISHI Pop Package Format** は依存関係として自動導入されます。

### Unitypackage

VRCSDKとNDMFを先に導入し、[Releases](https://github.com/orange3134/vpm/releases)から `MEISHI-Pop-Exporter-x.y.z.unitypackage` をダウンロードしてUnityへドラッグします。Exporterと共通パッケージをまとめて導入できます。

- Unitypackage版は `Assets/MEISHIPop/Exporter` と `Assets/MEISHIPop/PackageFormat` に入ります。
- VPM版を導入済みならVCCから更新してください。Unitypackageを重ねて入れないでください。
- Unitypackage → VPMの移行では、上記2フォルダーをVPMの `legacyFolders` により置換します。自分でコードを変更した場合は先にバックアップしてください。
- 以前のZIPを「Add package from disk」で追加していた場合は、そのローカルパッケージ2つを外してからVCCで導入します。

## 使い方

1. UnityのEdit Modeで、Hierarchyからアバタールートを選択します。
2. **MEISHI Pop → Export Avatar** を開きます。
3. 追加の表情が必要なら表情名とAnimationClipを指定します。フレーム選択は自動です。「こだわり設定」で時刻を調整できます。
4. iPhone用は **iOS**、Mac Editor用は **StandaloneOSX** を選び、`.mpavatar` を書き出します。
5. MEISHI Pop!でアバターを変更し、新しいファイルを読み込みます。元の出力ファイルの上書きだけでは、取り込み済みの名刺は更新されません。

Unity **2022.3 / Built-in Render Pipeline** 向けです。iPhone用の出力には、そのUnity Editorと同じバージョンの **iOS Build Support** をUnity Hubから追加してください。プラットフォームごとに別ファイルが必要です。

検証済みの書き出し環境：Unity 2022.3.22f1、VRCSDK 3.10.5、NDMF 1.14.8、Modular Avatar 1.18.7。SDK依存範囲は3.10.5以上、3.11未満です。iOS用Bundleの最終表示は実機で確認してください。

## 変換範囲

- NDMF / Modular Avatarの処理結果、Humanoid、Mesh、Material、Shaderを使用します。
- PhysBoneの角度制限、重力、Colliderを中立形式へ変換します。アプリでの動的応答は近似です。
- viseme、瞬き、眼ボーン、一部BlendShape名と追加AnimationClipから静止表情を抽出します。
- 衣装は書き出し時点の状態です。FX Controller / Expressions Menuの動作、Contacts、OSC、掴み、Stretch/Squishなどは再現しません。
- VRC RotationConstraintは一部対応。他の未対応Constraintはレポートを表示して書き出しを停止します。
- シェーダーは元のものを保持します。Metal非対応の記述やVRChat固有の照明など、完全一致は保証できません。

出力元のUnityプロジェクトと、利用できるアバター・素材が必要です。VRChatゲーム内で利用できるだけのアバターや、VRChatサーバーから取得したデータは対象外です。

## 配布構成

このリポジトリにはエクスポーターとSDK非依存の共通形式のみを収録します。MEISHI Pop!アプリ本体、アバター、VRCSDK、NDMF、シェーダーなどの第三者パッケージは同梱しません。

| 内容 | 場所 |
|---|---|
| Exporter | `Packages/com.avatarnamecard.exporter` |
| 共通形式 | `Packages/com.avatarnamecard.avatar-package` |
| VPM配布元情報 | `source.json` |
| 配布用メタデータ・移行GUID | `distribution.json` |
| GitHub Pages | `Website/` |

公式 [template-package-listing](https://github.com/vrchat-community/template-package-listing) を元にしています。リリース内の `vpm-release.json` から全バージョンを収集するPython処理へ変更し、同じ内容を `vpm.json` / `index.json` で公開します。VCCの正式な追加URLは `vpm.json` です。

## 開発・リリース

通常のコード修正は非公開アプリ側の元パッケージで行い、このリポジトリへ必要なファイルだけ同期します。アプリの履歴やAssets全体をコピーしません。

```bash
python3 scripts/sync_from_app.py --source /path/to/private-app-checkout
python3 -m unittest discover -s scripts -p 'test_*.py' -v
python3 scripts/build_release.py
```

`dist/` にVPM ZIP 2つ、Unitypackage 1つ、`vpm-release.json`、`SHA256SUMS.txt` ができます。Python 3.10以降の標準ライブラリだけで動作し、UnityライセンスやSDKの再配布は不要です。Unitypackageは元のmeta/GUIDを保持し、Runtime・Editorのみを同梱します。

公開手順：

1. 元パッケージ2つの `package.json` のversionを同じ新しい番号へ更新し、上記同期を行います。ランタイムのExporterVersion定数なども必要に応じて更新します。
2. `RELEASE_NOTES.md` を更新して、差分を確認・コミットし、mainをpushします。
3. 同じコミットに `vX.Y.Z` タグを付けてpushします。

```bash
git push origin main
git tag vX.Y.Z
git push origin vX.Y.Z
```

**Release Exporter** が両形式を生成してGitHub Releaseへ公開し、完了後に **Build Repo Listing** が配布一覧とサイトを更新します。タグとバージョンが異なる場合は停止します。公開済みの同じバージョンは上書きせず、番号を上げてください。失敗したドラフトの再実行は可能です。

PR / mainへのpushでは **Validate Packages** が構造・GUID・ハッシュ・移行先・再現性・過去バージョン保持を確認し、配布ファイルをActionsのartifactとして保存します。初回のサイト更新は最初のReleaseが存在してから実行してください。

## 独自ドメイン

- GitHubの Settings → Pages → Build and deployment は **GitHub Actions**。
- Custom domain は `vpm.pipipigiken.jp`。
- XserverのDNSは **CNAME `vpm` → `orange3134.github.io`**（TTL 3600）。
- GitHubの証明書発行完了後、**Enforce HTTPS** を有効にします。
- カスタムActionsでの公開なので、CNAMEファイルだけではなくGitHub Pagesの設定でドメインを指定します。

## ライセンス

公開テスト中です。エクスポーターの配布ライセンスは検討中で、現時点ではMIT等のオープンソースライセンスを付与していません。アバターや素材の権利・利用条件は各提供元に従ってください。
