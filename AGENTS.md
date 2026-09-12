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
- `src/FlugelKranz.Core/` は座標変換・論理 Drag / Turn 操作・モード移行・実行制御を担当し、UI やネイティブ API に依存しません。
- `tests/FlugelKranz.Tests/` には xUnit v3 と Avalonia.Headless.XUnit によるテストがあります。

## Monado サブモジュールの読み取り専用規則

`src/MonadoXrApi/monado/` は API・実装を参照するための読み取り専用（ReadOnly）領域です。ファイルの追加・編集・削除、コード生成・整形・ビルドによる出力など、配下への書き込みは一切禁止します。サブモジュールの更新・チェックアウトや参照コミットの変更も行わないでください。バインディングなどの生成物は必ずサブモジュール外に出力します。本プロジェクトでは上流 Monado XR への PR 作成・送信は不要であり、行いません。

Monado 本体を本当に改造しなければ実現できない要件が判明した場合は、必要な機能、既存 API では実現できない根拠、検討した代替手段、必要な変更範囲を Reina_Sakiria に説明して判断を仰いでください。改造が必要と判断しただけでは書き込みは許可されません。明示的な承認を得るまで読み取り専用を維持してください。

飛行による空間移動は `mnd_root_set_reference_space_offset`（現在の OpenXR 入力が使う STAGE）で行ってください。`mnd_root_set_tracking_origin_offset` はスペースキャリブレーターなどが扱う原点設定であり、本プロジェクトから呼び出してはいけません。トラッキング原点のオフセットは読み取り専用で扱い、HMD 再接続や原点更新を理由に処理を停止しないでください。

Monado 本体のビルド、ダミードライバーを使った実験、統合テストは実行して構いません。ただしビルドディレクトリと生成物はサブモジュールの外に置き、サブモジュール内へキャッシュやテスト結果も書き込まないでください。

## 仕様の参照先

コントローラー別の入力割当、片手・両手の Drag / Turn、自由飛行・無限歩行の挙動、モード移行、慣性パラメーター、トラッキング喪失とリセット時の状態は `README.md` を唯一の権威として参照してください。設定保存と UI の仕様はこのファイルの該当セクションを参照してください。これらを変更するときは、対応する仕様書と実装・テストを同時に更新します。

実装では Drag の線速度と Turn の角速度を分離し、物理座標の復元と参照空間の適用順序を README の説明に合わせてください。削除済みの機能を再導入する場合や、README の仕様で判断できない挙動を変更する場合は、先に Reina_Sakiria へ確認します。

## 設定保存

設定値は変更時に JSON へ保存し、次回起動時に復元します。既定の保存先は `~/.config/FlugelKranz/config.json` です。`XDG_CONFIG_HOME` は設定ディレクトリ、`FLUGELKRANZ_CONFIG` は保存先ファイルを上書きします。設定は `schemaVersion`、`mode`、`freeFlight`、`infiniteWalking`、`valveIndex` の階層を持ち、モード別の値と機器固有の入力値を混在させません。今回のモード導入以前の平坦な設定は互換読み込みせず、現在の既定値で初期化して上書きします。保存形式や `SettingsStore` を変更した場合は、現行形式の読み書きと旧形式の移行をテストしてください。各 UI 設定には既定値へ戻す操作を用意します。

## UI 方針

[Avalonia](https://docs.avaloniaui.net/docs/platform-specific-guides/linux) は `UseWayland()` を明示的に選択し、XWayland へ自動フォールバックしません。[Avalonia.Markup.Declarative](https://github.com/AvaloniaCommunity/Avalonia.Markup.Declarative) と CommunityToolkit.Mvvm を使い、View とバインディングは基本的に C# で記述します。FluentTheme の Light / Dark テーマ辞書を使い、既定は白い Light テーマにします。状態色はテーマリソースから取得し、個別の固定色を場当たり的に追加しません。

メイン画面は大きな ON/OFF ボタン、その左の現在モードを示す円形 `I` / `F` 切替ボタン、その右の円形の設定 `⚙`・位置姿勢リセット `↻` ボタンを配置します。設定と位置姿勢リセットは中央線の上下に縦配置します。初期ウィンドウは横長（1100×650）とし、設定パネルはメイン領域を左へ押し出して幅の約80%を占め、幅720px以下では全幅にします。設定項目はモードごとの区切りとグループを明示し、ラベル・操作部・小さな正方形の初期値リセットを同じ行で揃えます。Valve Index が有効な間は操作状態に左右の trackpad force を表示し、専用グループで位置デッドゾーンと感圧閾値を調整可能にします。設定スクロールバーは常時表示（`AllowAutoHide = false`）とし、操作部と重ならない右マージンを確保します。

## ビルド・開発コマンド

`net10.0` と `.slnx` 形式に対応した .NET SDK を使用してください。以下はリポジトリのルートで実行します。

- `dotnet restore FlugelKranz.slnx` — NuGet の依存関係を復元します。
- `dotnet build FlugelKranz.slnx` — ソリューション全体をビルドします。
- `dotnet test FlugelKranz.slnx` — 座標変換・実行制御・UI の自動テストを実行します。
- `dotnet run --project src/FlugelKranz -- --help` — CLI の使い方を表示します。
- `dotnet run --project src/FlugelKranz -- --diagnose --lib-monado PATH` — UI を開かず、接続先・基準空間・HMD・左右入力とオフセットを確認します。
- `dotnet run --project src/FlugelKranz` — Wayland UI を起動します。libmonado は `XR_RUNTIME_JSON` または XDG の OpenXR runtime manifest から `MND_libmonado_path` を探索し、見つからない場合は `/usr/lib/wivrn/libmonado_wivrn.so` を使用します。`--lib-monado PATH` で明示指定できます。

## コーディング規約

起動時の引数解析・ヘルプ表示・引数エラー処理・実行処理への振り分けには `System.CommandLine` を使用してください。Avalonia の起動はエントリースレッド上で行います。

インデントはスペース4個とし、型・メンバーには PascalCase、引数・ローカル変数には camelCase を使用してください。ファイルスコープ名前空間を使用し、UI・ViewModel・空間操作・ネイティブ接続の責務を分離してください。Null 許容参照型と暗黙的な using は有効です。unsafe コードは相互運用に必要な範囲に限定し、生成されたバインディングは直接編集せず再生成してください。

`MonadoXrApi` に OpenXR・Avalonia・`FlugelKranz.Core` への依存を追加してはいけません。libmonado のロードは `LibMonadoLibrary` に閉じ込め、API 呼び出しは `MonadoRoot` などのラッパーを経由します。FlugelKranz 固有の座標変換・飛行制御・libmonado と OpenXR の協調処理は `FlugelKranz.Core` または `FlugelKranz.OpenXR` に実装してください。使用されない生成済み ABI バインディングは保持しません。

物理ボタンを操作器へ直結せず、`FlugelKranz.OpenXR` で手ごとの論理 Drag / Turn / モード切替へ変換してから Core へ渡してください。入力割当は `ControllerInputMapping` に集約し、将来 Drag と Turn が同じ物理ボタンへ割り当てられても操作器を変更せずに済む構成を保ちます。

C# 用のフォーマッターやリンターの設定は登録されていません。

## テスト方針

変更後はソリューションをビルドし、`dotnet test FlugelKranz.slnx` を実行してください。テスト名は検証する振る舞いが分かる名前にします。カバレッジの数値基準は未設定ですが、論理入力割当、片手・両手の切替時の再基準化、両モードの軸制約、Turn 原点、モード移行、座標変換の適用順序、トラッキング喪失、オフと復元を検証してください。実機確認の手順と対応条件は `README.md` を参照し、自動テストと実機検証の結果を区別してください。上流のネイティブテストは `src/MonadoXrApi/monado/tests/` にあり、参照に利用できます。検証時もサブモジュールへの書き込みは禁止です。

## コミット・プルリクエスト

コミットメッセージは Conventional Commits を基本とし、`<type>(<scope>): <説明>` の形式を使用してください。scope は省略可能です。type は `feat`、`fix`、`docs`、`refactor`、`test`、`chore` など、変更の目的に合わせます。例：`docs: コミット規則を更新`。件名は簡潔にし、英語の場合は命令形を使用してください。

今後は作業の完了・検証後、一機能や一修正など意味のあるまとまりごとに AI エージェントがコミットしてください。密接に関連する変更は必要に応じてまとめ、細分化しすぎないでください。push は Reina_Sakiria が行います。AI エージェントは `git push` などによるリモートへの送信を行わないでください。

コミットの作成・修正には `git commit`、`git commit --amend`、`git rebase` など通常の Git コマンドを使用してください。`.git` 内のファイルを直接編集したり、`git commit-tree` / `git update-ref` で履歴を組み立てたりしないでください。

AI エージェントは、コミット時に `-c commit.gpgSign=false` を明示して GPG 署名を無効にしてください。この環境では対話式の PIN 入力を行えないためです。署名失敗を理由にコミットを保留せず、通常の Git コマンドでローカルコミットを作成してください。

AI / LLM がコミットを作成する場合は、常に Git の Author（作成者）に AI であることが分かる名前と専用メールアドレスを明示してください。OpenAI Codex は Author・Committer の両方に `OpenAI Codex <codex@openai.com>` を使用してください。サンプルやプレースホルダーのメールアドレス（`example.invalid` など）は使用禁止です。例：`git -c user.name='OpenAI Codex' -c user.email='codex@openai.com' -c commit.gpgSign=false commit --author='OpenAI Codex <codex@openai.com>' -m 'docs: コミット規則を更新'`。この例は Author と Committer の両方をそのコマンド限りで設定します。人間のユーザーの名前・メールアドレスを作成者として流用せず、`Co-authored-by` の追記だけで代用しないでください。

プルリクエストには目的、影響するプロジェクト、検証コマンドと結果、必要なネイティブライブラリを記載します。関連 Issue があればリンクし、サブモジュールの参照コミットを変更した場合は明記してください。
