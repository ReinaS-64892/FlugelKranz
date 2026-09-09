# FlugelKranz

Reina_Sakiria が実現した VRChat の「自由飛行」を、Monado / WiVRn の空間操作 API で発展させるためのツールです。アバターギミックではありません。

## 起動

.NET 10 SDK、Linux の Wayland セッション、Monado / WiVRn のサービスと対応する OpenXR ランタイムが必要です。

```sh
dotnet restore FlugelKranz.slnx
dotnet build FlugelKranz.slnx
dotnet run --project src/FlugelKranz
```

libmonado の既定パスは `/usr/lib/wivrn/libmonado_wivrn.so` です。異なる場合は指定してください。

起動時の引数処理は `System.CommandLine` を使用します。`--help` でオプションを確認できます。

```sh
dotnet run --project src/FlugelKranz -- --lib-monado /path/to/libmonado.so
```

OpenXR ローダー（`libopenxr_loader.so.1`）が同じ Monado / WiVRn サービスを使用するよう設定してください。必要に応じて `XR_RUNTIME_JSON=/path/to/runtime.json` を起動時に指定します。FlugelKranz はシステムのランタイム設定を変更しません。

UI は [Avalonia](https://docs.avaloniaui.net/docs/platform-specific-guides/linux) の `UseWayland()` を明示的に選択します。XWayland への自動フォールバックはありません。[Avalonia.Markup.Declarative](https://github.com/AvaloniaCommunity/Avalonia.Markup.Declarative) と CommunityToolkit.Mvvm により、UI・バインディングを C# で記述しています。OpenXR は NuGet の [Evergine.Bindings.OpenXR](https://github.com/EvergineTeam/OpenXR.NET) を使用します。

## 操作

1. HMD・両コントローラーを接続し、「オンにする」を押します。
2. グリップを一度離します。左手グリップを握って動かすと **Space Drag**、右手グリップを握って回すと **Space Turn** が動作します。
3. Space Turn は頭の位置を支点に、ピッチ・ヨー・ロールすべてを扱います。両手を握る場合は、左手でつかんだ点が支点になります。
4. グリップを離すと変換を保持します。「オフにする」は入力による操作を止め、位置・姿勢を保持します。「接続時の位置・姿勢に戻す」はオフにして初期オフセットへ復元します。

通常のウィンドウ終了時も復元します。強制終了やサービス切断では復元できない場合があります。他のツールがオフセットを変更した場合は停止し、その変更を上書きしません。トラッキングを失った手は操作を解除し、復帰後はグリップを離すまで再開しません。慣性による継続移動は今回の実装には含めていません。

## 対応条件と制約

- libmonado API **1.4 以降の 1.x**。公開 API のみを使用し、Monado サブモジュールは変更しません。
- OpenXR の `XR_MND_headless` と `XR_KHR_convert_timespec_time`、STAGE / VIEW 空間が必要です。描画なしセッションを使い、VRChat からプライマリー・フォーカスを奪う操作は行いません。
- HMD と両コントローラーが同一のトラッキング原点に属し、libmonado から固定 STAGE オフセットを読める構成が対象です。ドライバーが動的に提供する STAGE や複数原点の構成では、起動時に理由を表示して操作を開始しません。
- Oculus Touch / Valve Index の squeeze 値、HTC Vive / Microsoft Motion Controller の squeeze ボタンにバインドします。他のコントローラーはランタイム側のプロファイル互換性に依存します。グリップのない Simple Controller の select ボタンは代用しません。
- オフセットは対象原点を使う他の XR クライアントにも作用します。別原点のフルボディトラッカーとの整合は未対応です。
- VRChat 内の移動・水平線調整はアプリ側の座標変換です。本ツールはその設定を読み書きしません。

## 検証

```sh
dotnet test FlugelKranz.slnx
dotnet run --project src/FlugelKranz -- --diagnose --lib-monado /path/to/libmonado.so
```

`--diagnose` は UI を開かず、接続先の一致・基準空間・HMD と両手の入力を確認します。空間オフセットは書き込みません。接続できても有効な入力が揃わなければ非ゼロで終了します。

自動テストは三軸回転、左手の固定点、左右同時操作、既存オフセット、入力への変換の混入防止、トラッキング喪失、オン・オフ・復元、UI のバインディングと接続エラーを対象にします。実機では VRChat を動かしたまま、左右個別・同時操作、各回転軸、グリップ解放、オフ、復元、終了を確認してください。自動テストだけではランタイム・コントローラー・VRChat の組み合わせを保証できません。

## 座標変換

`T` を物理トラッキング原点から Monado ルートへの変換、`S` を固定 STAGE からルートへの変換とします。OpenXR が返す STAGE 内の姿勢 `P` から、物理姿勢を `T⁻¹ × S × P` で復元します。積は右側を先に適用します。

ドラッグ中は左手のルート座標を固定します。ターン中はグリップ開始時の右手姿勢に現在の物理姿勢の逆回転を合成し、クォータニオンで全軸を保持します。右手のみなら HMD、両手なら左手の固定点を支点として並進を補正します。VRChat 世界と飛行者の軸への対応は、VRChat 側の水平線調整などを合成した結果になります。
