# 衣装簡単切替ツール

VRChatアバターの衣装を、個別またはグループで切り替えるUnityエディター拡張です。Radial Inventory System V4 4.6.0を基にした改変版です。

## 主な機能

- Expressionsメニューから、個別切替・まとめて着る・まとめて脱ぐを操作。
- 着替え時に他の登録衣装を自動OFF。アクセサリーなどのグループは保持可能。
- 衣装だけの透過PNGと、グループごとに着替えた全身PNGを自動／手動で撮影。
- 多数のメニュー項目を自動でページ分割。再生成時もメニューの参照を保持。
- 元のSimple／Basic／Advancedの編集画面、設定JSON、排他設定やセットを継承。

アバターや衣装モデル、購入素材、個人の設定・サムネイルは付属しません。

## 対応環境

Unity 2022.3 / VRChat Avatars SDK **3.10.3以上・3.11.0未満**。
バックアップ付き移行ランチャーはWindows PowerShell 5.1用です。Macでの動作は未検証です。

## VCCから導入

1. 配布サイトの「VCCに追加」を押して、VCCで登録を確認します。
2. 対象プロジェクトの「Manage Project」を開き、「衣装簡単切替ツール」を追加します。
3. Unityを開き、上部の **衣装簡単切替ツール → エディター** を選びます。
4. アバターと衣装を設定し、「適用する」を押して、シーンを保存します。

ボタンが開かない場合は、配布サイトに表示される `index.json` のURLを VCCの **Settings → Packages → Add Repository** に入力してください。

ZIPを直接受け取った場合は、プロジェクト外のフォルダーへ展開し、`package.json` のあるフォルダーを **Settings → Packages → User Packages → Add** から登録できます。この方法では新バージョンの自動取得はできません。

## オリジナルからの移行

本ツールは `yagihata.radialinventorysystem.v4` を置き換えます。オリジナルと同時に入れないでください。設定と生成アセットは従来の `Assets/YagihataItems/RadialInventorySystemV4/` を使用します。

**バックアップ付き移行（Windows・推奨）**

1. 配布されたVPMパッケージZIPを、対象プロジェクトの外へ展開します。
2. Unityでシーンを保存し、Unityを閉じます。
3. 展開先の `install.bat` をダブルクリックして、対象プロジェクトのフォルダーを選びます。
4. Assets・Packages・ProjectSettings・UserSettingsのコピーを作成した後、元のVPMパッケージを置き換えます。
5. Unityを開き、補助ツールから既存設定を読み込み、対象アバターと参照を確認します。必要に応じて「移行診断・参照の修正」で直してから適用してください。
6. 公開リポジトリをVCCへ登録しておけば、以降はVCCから更新できます。

VCCだけで置き換える場合も、先にVCCのプロジェクトバックアップを作成してください。`legacyPackages` により元のVPMパッケージを置き換えますが、本ツールのバックアップランチャーはVCCからの導入時に自動実行されません。

**.unitypackage版のスクリプトがAssets内にある場合**は、そのまま追加しないでください。クラス・GUIDが重複します。バックアップ後に旧コードを取り除き、設定JSON・生成アセット・画像を保持した状態で移行する必要があります。移行ランチャーは旧コードを検出すると処理を停止します。

## 更新と復元

- 公開リポジトリからの更新：Unityを保存して閉じ、プロジェクトのバックアップを作ってからVCCで更新します。Unityを開き、必要に応じて「適用する」でメニューを再生成します。
- ランチャーで更新：新しいZIPをプロジェクト外へ展開し、`install.bat` を実行します。
- ランチャーのバックアップはプロジェクトの親フォルダーの `RI-Kai-Backups` に保存されます。Assets全体のコピーに必要な空き容量を確保してください。
- ランチャーの復元：Unityを閉じ、展開先の `restore.bat` を実行。対象プロジェクトと `backup.json` のあるバックアップを選びます。復元直前の状態も保存します。
- VCCで作ったバックアップは、VCC側の手順で復元してください。ランチャー用のバックアップとは形式が異なります。

## 操作方法と検証範囲

[操作ガイド](Packages/net.l1zerox.radialinventory/Documentation~/操作ガイド.md) / [変更履歴](Packages/net.l1zerox.radialinventory/CHANGELOG.md)

1.7.1ではUnity 2022.3.6f1 / SDK 3.10.3で、生成・互換性50項目、Gesture Managerで一括切替27ケース、移行・復元4項目を確認しました。これらは開発環境の結果であり、あらゆるアバターや他ツールとの組み合わせを保証するものではありません。**VRChat実機・端末間同期は未検証**です。

## ライセンス

MIT。原作者：Copyright 2020 Yagihata。改変版：L1ZeroX。
[LICENSE](LICENSE)を配布物に同梱しています。原作者・VRChat公式による配布ではありません。

## 配布者向け

[GitHubでの初回公開・更新](公開手順.md)を参照してください。

形式は[VRChat公式のパッケージ仕様](https://vcc.docs.vrchat.com/vpm/packages/)と[リポジトリ仕様](https://vcc.docs.vrchat.com/vpm/repos/)に合わせています。
