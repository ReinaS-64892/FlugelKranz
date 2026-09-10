using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Declarative;
using Avalonia.Media;
using FlugelKranz.ViewModels;

namespace FlugelKranz.Views;

public sealed class MainView(MainViewModel vm) : ViewBase<MainViewModel>(vm)
{
    protected override object Build(MainViewModel model) =>
        new ScrollViewer().Content(
            new StackPanel().Margin(32).Spacing(20).Children(
                new TextBlock().Text("FlugelKranz").FontSize(32).FontWeight(FontWeight.SemiBold),
                new TextBlock().Text("自由飛行の、その先へ。"),
                new Button()
                    .HorizontalAlignment(HorizontalAlignment.Stretch)
                    .HorizontalContentAlignment(HorizontalAlignment.Center)
                    .MinHeight(60)
                    .FontSize(22)
                    .Content(model, x => x.ToggleLabel)
                    .Command(model, x => x.ToggleCommand),
                new TextBlock().Text(model, x => x.Status).TextWrapping(TextWrapping.Wrap),
                new Border().Padding(18).CornerRadius(new CornerRadius(12))
                    .Background(new SolidColorBrush(Color.Parse("#202D3A")))
                    .Child(new StackPanel().Spacing(14).Children(
                        new TextBlock().Text("SPACE DRAG / 左手").FontWeight(FontWeight.SemiBold),
                        new TextBlock().Text(model, x => x.LeftStatus).TextWrapping(TextWrapping.Wrap),
                        new TextBlock().Text("SPACE TURN / 右手").FontWeight(FontWeight.SemiBold),
                        new TextBlock().Text(model, x => x.RightStatus).TextWrapping(TextWrapping.Wrap)
                    )),
                new Border().Padding(18).CornerRadius(new CornerRadius(12))
                    .Background(new SolidColorBrush(Color.Parse("#18232E")))
                    .Child(new StackPanel().Spacing(10).Children(
                        new TextBlock().Text("慣性パラメーター（仮 UI）").FontSize(20).FontWeight(FontWeight.SemiBold),
                        new CheckBox().Content("ステップモード（慣性なし）")
                            .IsChecked(model, x => x.StepMode),
                        new Button().Content("ステップモードを初期値に戻す")
                            .Command(model, x => x.ResetStepModeCommand),
                        new CheckBox().Content("慣性カットオフを有効にする")
                            .IsChecked(model, x => x.InertiaCutoffEnabled),
                        new Button().Content("カットオフ有効を初期値に戻す")
                            .Command(model, x => x.ResetInertiaCutoffEnabledCommand),
                        new TextBlock().Text(model, x => x.DragCutoffLabel),
                        new Slider().Minimum(0).Maximum(50).TickFrequency(0.5)
                            .Value(model, x => x.DragCutoffCentimetresPerSecond),
                        new Button().Content("Drag カットオフを初期値に戻す")
                            .Command(model, x => x.ResetDragCutoffCommand),
                        new TextBlock().Text(model, x => x.TurnCutoffLabel),
                        new Slider().Minimum(0).Maximum(180).TickFrequency(1)
                            .Value(model, x => x.TurnCutoffDegreesPerSecond),
                        new Button().Content("Turn カットオフを初期値に戻す")
                            .Command(model, x => x.ResetTurnCutoffCommand),
                        new TextBlock().Text("慣性加速倍率").FontWeight(FontWeight.SemiBold),
                        new TextBlock().Text(model, x => x.DragAccelerationLabel),
                        new Slider().Minimum(0).Maximum(5).TickFrequency(0.05)
                            .Value(model, x => x.DragAccelerationMultiplier),
                        new Button().Content("Drag 加速倍率を初期値に戻す")
                            .Command(model, x => x.ResetDragAccelerationCommand),
                        new TextBlock().Text(model, x => x.TurnAccelerationLabel),
                        new Slider().Minimum(0).Maximum(5).TickFrequency(0.05)
                            .Value(model, x => x.TurnAccelerationMultiplier),
                        new Button().Content("Turn 加速倍率を初期値に戻す")
                            .Command(model, x => x.ResetTurnAccelerationCommand),
                        new TextBlock().Text("ベクトル回転倍率").FontWeight(FontWeight.SemiBold),
                        new TextBlock().Text(model, x => x.VectorRotationLabel),
                        new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                            .Value(model, x => x.VectorRotationMultiplier),
                        new Button().Content("ベクトル回転倍率を初期値に戻す")
                            .Command(model, x => x.ResetVectorRotationCommand),
                        new TextBlock().Text("慣性減速").FontWeight(FontWeight.SemiBold),
                        new TextBlock().Text(model, x => x.InertiaDecelerationLabel),
                        new Slider().Minimum(0).Maximum(10).TickFrequency(0.01)
                            .Value(model, x => x.InertiaDecelerationPerSecond),
                        new Button().Content("慣性減速を初期値に戻す")
                            .Command(model, x => x.ResetDecelerationCommand),
                        new CheckBox().Content("慣性減速免除を有効にする")
                            .IsChecked(model, x => x.DecelerationExemptionEnabled),
                        new Button().Content("減速免除有効を初期値に戻す")
                            .Command(model, x => x.ResetExemptionEnabledCommand),
                        new TextBlock().Text(model, x => x.DragDecelerationExemptionDurationLabel),
                        new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                            .Value(model, x => x.DragDecelerationExemptionDurationRatio),
                        new Button().Content("Drag 免除時間を初期値に戻す")
                            .Command(model, x => x.ResetDragExemptionCommand),
                        new TextBlock().Text(model, x => x.TurnDecelerationExemptionDurationLabel),
                        new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                            .Value(model, x => x.TurnDecelerationExemptionDurationRatio),
                        new Button().Content("Turn 免除時間を初期値に戻す")
                            .Command(model, x => x.ResetTurnExemptionCommand),
                        new TextBlock().Text(model, x => x.DecelerationExemptionStrengthLabel),
                        new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                            .Value(model, x => x.DecelerationExemptionStrength),
                        new Button().Content("免除割合を初期値に戻す")
                            .Command(model, x => x.ResetExemptionStrengthCommand),
                        new TextBlock().Text("ドラグスムーズ").FontWeight(FontWeight.SemiBold),
                        new TextBlock().Text(model, x => x.DragSmoothLabel),
                        new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                            .Value(model, x => x.DragSmoothSeconds),
                        new Button().Content("Drag スムーズを初期値に戻す")
                            .Command(model, x => x.ResetDragSmoothCommand),
                        new TextBlock().Text(model, x => x.TurnSmoothLabel),
                        new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                            .Value(model, x => x.TurnSmoothSeconds),
                        new Button().Content("Turn スムーズを初期値に戻す")
                            .Command(model, x => x.ResetTurnSmoothCommand),
                        new TextBlock().Text("ドラブレーキ値").FontWeight(FontWeight.SemiBold),
                        new TextBlock().Text(model, x => x.BrakeLabel),
                        new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                            .Value(model, x => x.BrakeStrength),
                        new Button().Content("ブレーキ値を初期値に戻す")
                            .Command(model, x => x.ResetBrakeCommand),
                        new CheckBox().Content("方向補正を有効にする")
                            .IsChecked(model, x => x.DirectionCorrectionEnabled),
                        new Button().Content("方向補正有効を初期値に戻す")
                            .Command(model, x => x.ResetDirectionCorrectionEnabledCommand),
                        new TextBlock().Text(model, x => x.DragCorrectionTimeLabel),
                        new Slider().Minimum(0).Maximum(5).TickFrequency(0.1)
                            .Value(model, x => x.DragCorrectionMaxSeconds),
                        new Button().Content("Drag 補正時間を初期値に戻す")
                            .Command(model, x => x.ResetDragCorrectionTimeCommand),
                        new TextBlock().Text(model, x => x.DragCorrectionStrengthLabel),
                        new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                            .Value(model, x => x.DragCorrectionStrength),
                        new Button().Content("Drag 補正強度を初期値に戻す")
                            .Command(model, x => x.ResetDragCorrectionStrengthCommand),
                        new TextBlock().Text(model, x => x.TurnCorrectionTimeLabel),
                        new Slider().Minimum(0).Maximum(5).TickFrequency(0.1)
                            .Value(model, x => x.TurnCorrectionMaxSeconds),
                        new Button().Content("Turn 補正時間を初期値に戻す")
                            .Command(model, x => x.ResetTurnCorrectionTimeCommand),
                        new TextBlock().Text(model, x => x.TurnCorrectionStrengthLabel),
                        new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                            .Value(model, x => x.TurnCorrectionStrength),
                        new Button().Content("Turn 補正強度を初期値に戻す")
                            .Command(model, x => x.ResetTurnCorrectionStrengthCommand)
                    )),
                new TextBlock()
                    .Text("オフでは位置・姿勢を保持します。両手を握ると、左手を支点に移動と回転を組み合わせます。ステップモードではグリップ解放時の慣性を付与しません。")
                    .TextWrapping(TextWrapping.Wrap),
                new Button().Content("接続時の位置・姿勢に戻す")
                    .IsEnabled(model, x => x.IsConnected)
                    .Command(model, x => x.ResetCommand),
                new TextBlock().Text("通常終了時にも接続時の状態へ戻します。")
                    .FontSize(12).TextWrapping(TextWrapping.Wrap)
            ));
}
