# Repository Guidelines

## プロジェクトの目的と背景

FlugelKranz は、Reina_Sakiria が実現した VRChat 上の「自由飛行」を発展させる道具です。ここでの自由飛行とは、通常の状態では消えない飛行・浮遊を可能にする技術であり、アバターギミックではありません。Monado XR Runtime および WiVRn の API を用いて、より自由な回転と、美しく多彩な飛び方の実現を目指します。

始祖である Reina_Sakiria の説明による既存手法は、OVR-AS と VRChat の水平線の調整で寝ている状態を縦にし、OVR-AS の SpaceDrag に慣性を付与して飛行を表現します。回転は VRChat 世界のグローバル Y 軸と、SpaceTurn による現実世界の Y 軸の二軸に制限されます。後者は水平線の調整によって VRChat 世界での飛行者の Z 軸に対応します。この制約を越えることが本プロジェクトの中心課題です。

## AI エージェントの作業方針

主な調査・実装・検証は AI エージェントが担当します。Reina_Sakiria の説明を設計の前提として尊重し、実装上の仮説と区別してください。Monado / WiVRn の API の機能や制約は、対象バージョンのヘッダー・実装・資料で確認し、未確認の機能を利用可能と断定しないでください。座標変換を扱う際は、現実世界・VRChat 世界・飛行者のローカル座標系を明示し、軸の対応と回転の適用順序を説明してください。

## プロジェクト構成

- `FlugelKranz.slnx` は、`src/` 配下の4つの C# プロジェクトと `tests/` 配下のテストをまとめます。
- `src/FlugelKranz/` は Avalonia のネイティブ Wayland アプリです。`Views/` の UI は Avalonia.Markup.Declarative で C# に記述し、`ViewModels/` には CommunityToolkit.Mvvm を使用します。
- `src/MonadoXrApi/` は libmonado の汎用相互運用ライブラリです。`LibMonadoLibrary` がネイティブライブラリのロードと ABI バージョン確認を担い、`MonadoRoot` が公開 libmonado API を型付きでラップします。このプロジェクトは `FlugelKranz.Core` を参照せず、FlugelKranz 固有の座標・入力・操作方針、および OpenXR 実装を含めません。
- `src/FlugelKranz.OpenXR/` は Evergine.Bindings.OpenXR による入力取得と、OpenXR 姿勢を FlugelKranz の物理座標系へ変換する実装を担当します。`FlugelKranz.Core` と `MonadoXrApi` を参照し、両者を結び付ける FlugelKranz 固有のランタイム実装を置きます。
- `src/MonadoXrApi/monado/` は、ネイティブコードとテストを含む上流の Git サブモジュールです。以下の読み取り専用規則に従ってください。
- `src/FlugelKranz.Core/` は座標変換・グリップ操作・実行制御を担当し、UI やネイティブ API に依存しません。
- `tests/FlugelKranz.Tests/` には xUnit v3 と Avalonia.Headless.XUnit によるテストがあります。

## Monado サブモジュールの読み取り専用規則

`src/MonadoXrApi/monado/` は API・実装を参照するための読み取り専用（ReadOnly）領域です。ファイルの追加・編集・削除、コード生成・整形・ビルドによる出力など、配下への書き込みは一切禁止します。サブモジュールの更新・チェックアウトや参照コミットの変更も行わないでください。バインディングなどの生成物は必ずサブモジュール外に出力します。本プロジェクトでは上流 Monado XR への PR 作成・送信は不要であり、行いません。

Monado 本体を本当に改造しなければ実現できない要件が判明した場合は、必要な機能、既存 API では実現できない根拠、検討した代替手段、必要な変更範囲を Reina_Sakiria に説明して判断を仰いでください。改造が必要と判断しただけでは書き込みは許可されません。明示的な承認を得るまで読み取り専用を維持してください。

## ビルド・開発コマンド

`net10.0` と `.slnx` 形式に対応した .NET SDK を使用してください。以下はリポジトリのルートで実行します。

- `dotnet restore FlugelKranz.slnx` — NuGet の依存関係を復元します。
- `dotnet build FlugelKranz.slnx` — ソリューション全体をビルドします。
- `dotnet test FlugelKranz.slnx` — 座標変換・実行制御・UI の自動テストを実行します。
- `dotnet run --project src/FlugelKranz -- --help` — CLI の使い方を表示します。
- `dotnet run --project src/FlugelKranz` — Wayland UI を起動します。libmonado は `XR_RUNTIME_JSON` または XDG の OpenXR runtime manifest から `MND_libmonado_path` を探索し、見つからない場合は `/usr/lib/wivrn/libmonado_wivrn.so` を使用します。`--lib-monado PATH` で明示指定できます。

## コーディング規約

起動時の引数解析・ヘルプ表示・引数エラー処理・実行処理への振り分けには `System.CommandLine` を使用してください。Avalonia の起動はエントリースレッド上で行います。

インデントはスペース4個とし、型・メンバーには PascalCase、引数・ローカル変数には camelCase を使用してください。ファイルスコープ名前空間を使用し、UI・ViewModel・空間操作・ネイティブ接続の責務を分離してください。Null 許容参照型と暗黙的な using は有効です。unsafe コードは相互運用に必要な範囲に限定し、生成されたバインディングは直接編集せず再生成してください。

`MonadoXrApi` に OpenXR・Avalonia・`FlugelKranz.Core` への依存を追加してはいけません。libmonado のロードは `LibMonadoLibrary` に閉じ込め、API 呼び出しは `MonadoRoot` などのラッパーを経由します。FlugelKranz 固有の座標変換・飛行制御・libmonado と OpenXR の協調処理は `FlugelKranz.Core` または `FlugelKranz.OpenXR` に実装してください。使用されない生成済み ABI バインディングは保持しません。

C# 用のフォーマッターやリンターの設定は登録されていません。スペルチェック用の辞書は `cspell.json` にあります。

## テスト方針

変更後はソリューションをビルドし、`dotnet test FlugelKranz.slnx` を実行してください。テスト名は検証する振る舞いが分かる名前にします。カバレッジの数値基準は未設定ですが、回転の全軸・座標変換の適用順序・入力への変換の混入・トラッキング喪失・オフと復元を検証してください。実機確認の手順と対応条件は `README.md` を参照し、自動テストと実機検証の結果を区別してください。上流のネイティブテストは `src/MonadoXrApi/monado/tests/` にあり、参照に利用できます。検証時もサブモジュールへの書き込みは禁止です。

## コミット・プルリクエスト

コミットメッセージは Conventional Commits を基本とし、`<type>(<scope>): <説明>` の形式を使用してください。scope は省略可能です。type は `feat`、`fix`、`docs`、`refactor`、`test`、`chore` など、変更の目的に合わせます。例：`docs: コミット規則を更新`。件名は簡潔にし、英語の場合は命令形を使用してください。

今後は作業の完了・検証後、一機能や一修正など意味のあるまとまりごとに AI エージェントがコミットしてください。密接に関連する変更は必要に応じてまとめ、細分化しすぎないでください。push は Reina_Sakiria が行います。AI エージェントは `git push` などによるリモートへの送信を行わないでください。

コミットの作成・修正には `git commit`、`git commit --amend`、`git rebase` など通常の Git コマンドを使用してください。`.git` 内のファイルを直接編集したり、`git commit-tree` / `git update-ref` で履歴を組み立てたりしないでください。

AI エージェントは、コミット時に `-c commit.gpgSign=false` を明示して GPG 署名を無効にしてください。この環境では対話式の PIN 入力を行えないためです。署名失敗を理由にコミットを保留せず、通常の Git コマンドでローカルコミットを作成してください。

AI / LLM がコミットを作成する場合は、常に Git の Author（作成者）に AI であることが分かる名前と専用メールアドレスを明示してください。OpenAI Codex は Author・Committer の両方に `OpenAI Codex <codex@openai.com>` を使用してください。サンプルやプレースホルダーのメールアドレス（`example.invalid` など）は使用禁止です。例：`git -c user.name='OpenAI Codex' -c user.email='codex@openai.com' -c commit.gpgSign=false commit --author='OpenAI Codex <codex@openai.com>' -m 'docs: コミット規則を更新'`。この例は Author と Committer の両方をそのコマンド限りで設定します。人間のユーザーの名前・メールアドレスを作成者として流用せず、`Co-authored-by` の追記だけで代用しないでください。

プルリクエストには目的、影響するプロジェクト、検証コマンドと結果、必要なネイティブライブラリを記載します。関連 Issue があればリンクし、サブモジュールの参照コミットを変更した場合は明記してください。
