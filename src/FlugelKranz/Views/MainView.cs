using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Declarative;
using Avalonia.Media;
using FlugelKranz.ViewModels;
using System.Linq.Expressions;

namespace FlugelKranz.Views;

public sealed class MainView(MainViewModel vm) : ViewBase<MainViewModel>(vm)
{
    protected override object Build(MainViewModel model)
    {
        var root = new Grid
        {
            ClipToBounds = true,
            ColumnDefinitions = new ColumnDefinitions("*, 0")
        };
        var main = new Border
        {
            Padding = new Thickness(28)
        }.Classes("app-surface")
            .Child(new StackPanel().Orientation(Orientation.Horizontal).Spacing(16)
                .HorizontalAlignment(HorizontalAlignment.Center)
                .VerticalAlignment(VerticalAlignment.Center).Children(
                    new Button
                    {
                        Width = 300,
                        Height = 220,
                        FontSize = 52,
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(48),
                        Padding = new Thickness(0),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center
                    }.Classes("primary-action")
                        .Content(model, x => x.ToggleLabel)
                        .Command(model, x => x.ToggleCommand),
                    new Button
                    {
                        Width = 64,
                        Height = 64,
                        FontSize = 28,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(32),
                        Padding = new Thickness(0),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center
                    }.Classes("settings-action")
                        .Content("⚙")
                        .Command(model, x => x.ToggleSettingsCommand)
                ));
        var panel = new Border
        {
            Padding = new Thickness(20),
            ClipToBounds = true
        }.Classes("settings-surface")
            .Width(model, x => x.SettingsPanelWidth)
            .Child(new ScrollViewer().Content(SettingsPanel(model)));

        Grid.SetColumn(panel, 1);
        root.Children.Add(main);
        root.Children.Add(panel);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.SettingsPanelWidth))
                root.ColumnDefinitions[1].Width = new GridLength(model.SettingsPanelWidth);
        };
        root.ColumnDefinitions[1].Width = new GridLength(model.SettingsPanelWidth);
        return root;
    }

    private static Control SettingsPanel(MainViewModel model) =>
        new StackPanel().Spacing(10).MinWidth(380).Children(
            new DockPanel().Children(
                new TextBlock().Text("設定").FontSize(24).FontWeight(FontWeight.SemiBold)
                    .VerticalAlignment(VerticalAlignment.Center),
                new Button().Content("閉じる")
                    .HorizontalAlignment(HorizontalAlignment.Right)
                    .Command(model, x => x.ToggleSettingsCommand)
            ),
            new TextBlock().Text(model, x => x.Status).TextWrapping(TextWrapping.Wrap),
            new TextBlock().Text("操作状態").FontWeight(FontWeight.SemiBold),
            new TextBlock().Text(model, x => x.LeftStatus),
            new TextBlock().Text(model, x => x.RightStatus),
            Row("ステップモード", new CheckBox().IsChecked(model, x => x.StepMode),
                new Button().Content("初期値").Command(model, x => x.ResetStepModeCommand)),
            Row("慣性カットオフ", new CheckBox().IsChecked(model, x => x.InertiaCutoffEnabled),
                new Button().Content("初期値").Command(model, x => x.ResetInertiaCutoffEnabledCommand)),
            Row(model, x => x.DragCutoffLabel, new Slider().Minimum(0).Maximum(50).TickFrequency(0.5)
                    .Value(model, x => x.DragCutoffCentimetresPerSecond),
                new Button().Content("初期値").Command(model, x => x.ResetDragCutoffCommand)),
            Row(model, x => x.TurnCutoffLabel, new Slider().Minimum(0).Maximum(180).TickFrequency(1)
                    .Value(model, x => x.TurnCutoffDegreesPerSecond),
                new Button().Content("初期値").Command(model, x => x.ResetTurnCutoffCommand)),
            Row(model, x => x.DragAccelerationLabel, new Slider().Minimum(0).Maximum(5).TickFrequency(0.05)
                    .Value(model, x => x.DragAccelerationMultiplier),
                new Button().Content("初期値").Command(model, x => x.ResetDragAccelerationCommand)),
            Row(model, x => x.TurnAccelerationLabel, new Slider().Minimum(0).Maximum(5).TickFrequency(0.05)
                    .Value(model, x => x.TurnAccelerationMultiplier),
                new Button().Content("初期値").Command(model, x => x.ResetTurnAccelerationCommand)),
            Row("ベクトル回転倍率", new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.VectorRotationMultiplier),
                new Button().Content("初期値").Command(model, x => x.ResetVectorRotationCommand)),
            Row(model, x => x.InertiaDecelerationLabel, new Slider().Minimum(0).Maximum(10).TickFrequency(0.01)
                    .Value(model, x => x.InertiaDecelerationPerSecond),
                new Button().Content("初期値").Command(model, x => x.ResetDecelerationCommand)),
            Row("減速免除", new CheckBox().IsChecked(model, x => x.DecelerationExemptionEnabled),
                new Button().Content("初期値").Command(model, x => x.ResetExemptionEnabledCommand)),
            Row(model, x => x.DragDecelerationExemptionDurationLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.DragDecelerationExemptionDurationRatio),
                new Button().Content("初期値").Command(model, x => x.ResetDragExemptionCommand)),
            Row(model, x => x.TurnDecelerationExemptionDurationLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.TurnDecelerationExemptionDurationRatio),
                new Button().Content("初期値").Command(model, x => x.ResetTurnExemptionCommand)),
            Row(model, x => x.DecelerationExemptionStrengthLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.DecelerationExemptionStrength),
                new Button().Content("初期値").Command(model, x => x.ResetExemptionStrengthCommand)),
            Row(model, x => x.DragSmoothLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.DragSmoothSeconds),
                new Button().Content("初期値").Command(model, x => x.ResetDragSmoothCommand)),
            Row(model, x => x.TurnSmoothLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.TurnSmoothSeconds),
                new Button().Content("初期値").Command(model, x => x.ResetTurnSmoothCommand)),
            Row("ドラグブレーキ値", new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.BrakeStrength),
                new Button().Content("初期値").Command(model, x => x.ResetBrakeCommand)),
            Row("方向補正", new CheckBox().IsChecked(model, x => x.DirectionCorrectionEnabled),
                new Button().Content("初期値").Command(model, x => x.ResetDirectionCorrectionEnabledCommand)),
            Row(model, x => x.DragCorrectionTimeLabel, new Slider().Minimum(0).Maximum(5).TickFrequency(0.1)
                    .Value(model, x => x.DragCorrectionMaxSeconds),
                new Button().Content("初期値").Command(model, x => x.ResetDragCorrectionTimeCommand)),
            Row(model, x => x.DragCorrectionStrengthLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.DragCorrectionStrength),
                new Button().Content("初期値").Command(model, x => x.ResetDragCorrectionStrengthCommand)),
            Row(model, x => x.TurnCorrectionTimeLabel, new Slider().Minimum(0).Maximum(5).TickFrequency(0.1)
                    .Value(model, x => x.TurnCorrectionMaxSeconds),
                new Button().Content("初期値").Command(model, x => x.ResetTurnCorrectionTimeCommand)),
            Row(model, x => x.TurnCorrectionStrengthLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.TurnCorrectionStrength),
                new Button().Content("初期値").Command(model, x => x.ResetTurnCorrectionStrengthCommand)),
            new Button().Content("接続時の位置・姿勢に戻す")
                .IsEnabled(model, x => x.IsConnected)
                .Command(model, x => x.ResetCommand)
        );

    private static Grid Row(string label, Control editor, Button reset) =>
        Row(new TextBlock().Text(label), editor, reset);

    private static Grid Row(MainViewModel model, Expression<Func<MainViewModel, string>> label, Control editor, Button reset) =>
        Row(new TextBlock().Text(model, label), editor, reset);

    private static Grid Row(Control label, Control editor, Button reset)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*, 3*, Auto"),
            ColumnSpacing = 8
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(editor, 1);
        Grid.SetColumn(reset, 2);
        row.Children.Add(label);
        row.Children.Add(editor);
        row.Children.Add(reset);
        return row;
    }
}
