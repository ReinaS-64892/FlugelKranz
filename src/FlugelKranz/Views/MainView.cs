using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        var toggle = new Button
        {
            Width = 300,
            Height = 220,
            FontSize = 52,
            CornerRadius = new CornerRadius(48),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        }
            .Content(model, x => x.ToggleLabel)
            .Command(model, x => x.ToggleCommand);
        toggle.Classes.Add("flight-toggle");
        void UpdateToggleClass()
        {
            if (model.IsEnabled)
                toggle.Classes.Add("flight-toggle-enabled");
            else
                toggle.Classes.Remove("flight-toggle-enabled");
        }
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.IsEnabled))
                UpdateToggleClass();
        };
        UpdateToggleClass();
        var modeButton = new Button
        {
            Width = 64,
            Height = 64,
            FontSize = 28,
            CornerRadius = new CornerRadius(32),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        }
            .Content(model, x => x.ModeLabel)
            .Command(model, x => x.ToggleModeCommand);
        ToolTip.SetTip(modeButton, "I: 無限歩行 / F: 自由飛行");
        var settingsButton = new Button
        {
            Width = 64,
            Height = 64,
            FontSize = 28,
            CornerRadius = new CornerRadius(32),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        }
            .Content("⚙")
            .Command(model, x => x.ToggleSettingsCommand);
        var resetPoseButton = new Button
        {
            Width = 64,
            Height = 64,
            FontSize = 28,
            CornerRadius = new CornerRadius(32),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        }
            .Content("↻")
            .IsEnabled(model, x => x.IsConnected)
            .Command(model, x => x.ResetCommand);
        ToolTip.SetTip(resetPoseButton, "接続時の位置・姿勢に戻す");
        var main = new Border
        {
            Padding = new Thickness(28)
        }.Classes("app-surface")
            .Child(new StackPanel().Orientation(Orientation.Horizontal).Spacing(16)
                .HorizontalAlignment(HorizontalAlignment.Center)
                .VerticalAlignment(VerticalAlignment.Center).Children(
                    modeButton,
                    toggle,
                    new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                        .Children(settingsButton, resetPoseButton)
                ));
        var panel = new Border
        {
            Padding = new Thickness(20),
            ClipToBounds = true
        }.Classes("settings-surface")
            .Width(model, x => x.SettingsPanelWidth)
            .Child(new ScrollViewer
            {
                AllowAutoHide = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 12, 0)
            }.Content(SettingsPanel(model)));

        Grid.SetColumn(panel, 1);
        root.Children.Add(main);
        root.Children.Add(panel);
        root.SizeChanged += (_, args) =>
        {
            double width = args.NewSize.Width;
            model.SetSettingsPanelTargetWidth(width <= 720 ? width : width * 0.8);
        };
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.SettingsPanelWidth))
                root.ColumnDefinitions[1].Width = new GridLength(model.SettingsPanelWidth);
        };
        root.ColumnDefinitions[1].Width = new GridLength(model.SettingsPanelWidth);
        return root;
    }

    private static Control SettingsPanel(MainViewModel model) =>
        new StackPanel().Spacing(10).Children(
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
            new TextBlock().Text(model, x => x.ValveIndexForceStatus)
                .IsVisible(model, x => x.IsValveIndexDetected),
            Divider(),
            Header("Valve Index 専用入力"),
            Row(model, x => x.ValveIndexPositionDeadZoneLabel,
                new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.ValveIndexPositionDeadZone),
                ResetButton(model, x => x.ResetValveIndexPositionDeadZoneCommand)),
            Row(model, x => x.ValveIndexForceThresholdLabel,
                new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.ValveIndexForceThreshold),
                ResetButton(model, x => x.ResetValveIndexForceThresholdCommand)),
            Divider(),
            Header("動作モード"),
            Row(model, x => x.ModeDescription, new Button().Content("I / F 切替")
                    .Command(model, x => x.ToggleModeCommand),
                ResetButton(model, x => x.ResetModeCommand)),
            Divider(),
            Header("無限歩行 — ドラグスムーズ"),
            Row(model, x => x.InfiniteDragSmoothLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.InfiniteDragSmoothSeconds),
                ResetButton(model, x => x.ResetInfiniteDragSmoothCommand)),
            Row(model, x => x.InfiniteTurnSmoothLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.InfiniteTurnSmoothSeconds),
                ResetButton(model, x => x.ResetInfiniteTurnSmoothCommand)),
            Row(model, x => x.InfiniteTurnHeadSmoothLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.InfiniteTurnHeadSmoothSeconds),
                ResetButton(model, x => x.ResetInfiniteTurnHeadSmoothCommand)),
            Divider(),
            Header("無限歩行ブースト"),
            Row(model, x => x.InfiniteWalkingBoostLabel, new Slider().Minimum(0).Maximum(2).TickFrequency(0.05)
                    .Value(model, x => x.InfiniteWalkingBoostMultiplier),
                ResetButton(model, x => x.ResetInfiniteWalkingBoostCommand)),
            Divider(),
            Header("自由飛行 — Turn 原点"),
            Row("頭を原点にする", new CheckBox().IsChecked(model, x => x.UseHeadTurnOrigin),
                ResetButton(model, x => x.ResetTurnOriginCommand)),
            Divider(),
            Header("自由飛行 — 慣性カットオフ"),
            Row("有効", new CheckBox().IsChecked(model, x => x.InertiaCutoffEnabled),
                ResetButton(model, x => x.ResetInertiaCutoffEnabledCommand)),
            Row(model, x => x.DragCutoffLabel, new Slider().Minimum(0).Maximum(50).TickFrequency(0.5)
                    .Value(model, x => x.DragCutoffCentimetresPerSecond),
                ResetButton(model, x => x.ResetDragCutoffCommand)),
            Row(model, x => x.TurnCutoffLabel, new Slider().Minimum(0).Maximum(180).TickFrequency(1)
                    .Value(model, x => x.TurnCutoffDegreesPerSecond),
                ResetButton(model, x => x.ResetTurnCutoffCommand)),
            Divider(),
            Header("慣性加速倍率"),
            Row(model, x => x.DragAccelerationLabel, new Slider().Minimum(0).Maximum(5).TickFrequency(0.05)
                    .Value(model, x => x.DragAccelerationMultiplier),
                ResetButton(model, x => x.ResetDragAccelerationCommand)),
            Row(model, x => x.TurnAccelerationLabel, new Slider().Minimum(0).Maximum(5).TickFrequency(0.05)
                    .Value(model, x => x.TurnAccelerationMultiplier),
                ResetButton(model, x => x.ResetTurnAccelerationCommand)),
            Divider(),
            Header("Z加速"),
            Row(model, x => x.ZAccelerationLabel, new Slider().Minimum(1).Maximum(5).TickFrequency(0.05)
                    .Value(model, x => x.ZAccelerationMultiplier),
                ResetButton(model, x => x.ResetZAccelerationCommand)),
            Divider(),
            Header("慣性加速ブーストモード"),
            Row("有効", new CheckBox().IsChecked(model, x => x.InertiaAccelerationBoostEnabled),
                ResetButton(model, x => x.ResetInertiaAccelerationBoostEnabledCommand)),
            Row(model, x => x.InertiaAccelerationBoostMaximumLabel, new Slider().Minimum(1).Maximum(6).TickFrequency(0.05)
                    .Value(model, x => x.InertiaAccelerationBoostMaximumMultiplier),
                ResetButton(model, x => x.ResetInertiaAccelerationBoostMaximumMultiplierCommand)),
            Divider(),
            Header("ベクトル回転倍率"),
            Row(model, x => x.VectorRotationLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.VectorRotationMultiplier),
                ResetButton(model, x => x.ResetVectorRotationCommand)),
            Divider(),
            Header("慣性減速"),
            Row(model, x => x.InertiaDecelerationLabel, new Slider().Minimum(0).Maximum(10).TickFrequency(0.01)
                    .Value(model, x => x.InertiaDecelerationPerSecond),
                ResetButton(model, x => x.ResetDecelerationCommand)),
            Divider(),
            Header("慣性減速免除"),
            Row("有効", new CheckBox().IsChecked(model, x => x.DecelerationExemptionEnabled),
                ResetButton(model, x => x.ResetExemptionEnabledCommand)),
            Row(model, x => x.DragDecelerationExemptionDurationLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.DragDecelerationExemptionDurationRatio),
                ResetButton(model, x => x.ResetDragExemptionCommand)),
            Row(model, x => x.TurnDecelerationExemptionDurationLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.TurnDecelerationExemptionDurationRatio),
                ResetButton(model, x => x.ResetTurnExemptionCommand)),
            Row(model, x => x.DecelerationExemptionStrengthLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.DecelerationExemptionStrength),
                ResetButton(model, x => x.ResetExemptionStrengthCommand)),
            Divider(),
            Header("ドラグスムーズ"),
            Row(model, x => x.DragSmoothLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.DragSmoothSeconds),
                ResetButton(model, x => x.ResetDragSmoothCommand)),
            Row(model, x => x.TurnSmoothLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.TurnSmoothSeconds),
                ResetButton(model, x => x.ResetTurnSmoothCommand)),
            Divider(),
            Header("ドラグブレーキ"),
            Row(model, x => x.BrakeRampLabel, new Slider().Minimum(0).Maximum(1).TickFrequency(0.01)
                    .Value(model, x => x.BrakeRampSeconds),
                ResetButton(model, x => x.ResetBrakeRampCommand))
        );

    private static Grid Row(string label, Control editor, Button reset) =>
        Row(new TextBlock().Text(label), editor, reset);

    private static Separator Divider() => new() { Margin = new Thickness(0, 8) };

    private static Button ResetButton(
        MainViewModel model,
        Expression<Func<MainViewModel, System.Windows.Input.ICommand>> command) =>
        CreateResetButton(model, command);

    private static Button CreateResetButton(
        MainViewModel model,
        Expression<Func<MainViewModel, System.Windows.Input.ICommand>> command)
    {
        var button = new Button
        {
            Content = "↻",
            FontSize = 20,
            Width = 36,
            Height = 36,
            MinWidth = 36,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, "初期値に戻す");
        return button.Command(model, command);
    }

    private static TextBlock Header(string text) =>
        new TextBlock().Text(text).FontWeight(FontWeight.SemiBold).FontSize(16);

    private static Grid Row(MainViewModel model, Expression<Func<MainViewModel, string>> label, Control editor, Button reset) =>
        Row(new TextBlock().Text(model, label), editor, reset);

    private static Grid Row(Control label, Control editor, Button reset)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*, 3*, Auto"),
            ColumnSpacing = 8
        };
        label.VerticalAlignment = VerticalAlignment.Center;
        editor.VerticalAlignment = VerticalAlignment.Center;
        reset.VerticalAlignment = VerticalAlignment.Center;
        if (label is TextBlock textBlock)
            textBlock.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(label, 0);
        Grid.SetColumn(editor, 1);
        Grid.SetColumn(reset, 2);
        row.Children.Add(label);
        row.Children.Add(editor);
        row.Children.Add(reset);
        return row;
    }
}
