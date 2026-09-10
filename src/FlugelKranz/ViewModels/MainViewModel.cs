using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlugelKranz.Core;
using FlugelKranz.OpenXR;

namespace FlugelKranz.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly FlightController controller;
    private readonly SettingsStore settingsStore;
    private CancellationTokenSource? settingsAnimation;
    private double settingsPanelTargetWidth = 430;
    private bool closing;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool isEnabled;
    [ObservableProperty] private bool isSettingsOpen;
    [ObservableProperty] private double settingsPanelWidth;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private string status = "オフ — オンにするとランタイムへ接続します。";
    [ObservableProperty] private string leftStatus = "左手グリップを握って移動";
    [ObservableProperty] private string rightStatus = "右手グリップを握って全軸回転";
    [ObservableProperty] private bool stepMode;
    [ObservableProperty] private bool inertiaCutoffEnabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DragCutoffLabel))]
    private double dragCutoffCentimetresPerSecond = 40;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TurnCutoffLabel))]
    private double turnCutoffDegreesPerSecond = 45;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DragAccelerationLabel))]
    private double dragAccelerationMultiplier = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TurnAccelerationLabel))]
    private double turnAccelerationMultiplier = 0.5;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VectorRotationLabel))]
    private double vectorRotationMultiplier = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InertiaDecelerationLabel))]
    private double inertiaDecelerationPerSecond = 2;
    [ObservableProperty] private bool decelerationExemptionEnabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DragDecelerationExemptionDurationLabel))]
    private double dragDecelerationExemptionDurationRatio = 0.2;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TurnDecelerationExemptionDurationLabel))]
    private double turnDecelerationExemptionDurationRatio = 0.05;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DecelerationExemptionStrengthLabel))]
    private double decelerationExemptionStrength = 0.9;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DragSmoothLabel))]
    private double dragSmoothSeconds = 0.01;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TurnSmoothLabel))]
    private double turnSmoothSeconds = 0.05;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrakeLabel))]
    private double brakeStrength = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrakeRampLabel))]
    private double brakeRampSeconds = 0.2;
    [ObservableProperty] private bool directionCorrectionEnabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DragCorrectionTimeLabel))]
    private double dragCorrectionMaxSeconds = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DragCorrectionStrengthLabel))]
    private double dragCorrectionStrength = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TurnCorrectionTimeLabel))]
    private double turnCorrectionMaxSeconds = 0.5;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TurnCorrectionStrengthLabel))]
    private double turnCorrectionStrength = 1;
    public string ToggleLabel => IsEnabled ? "OFF" : "ON";
    public string DragCutoffLabel => $"Drag: {DragCutoffCentimetresPerSecond:0.0} cm/s";
    public string TurnCutoffLabel => $"Turn: {TurnCutoffDegreesPerSecond:0.0} °/s";
    public string DragAccelerationLabel => $"Drag: {DragAccelerationMultiplier:0.00} 倍";
    public string TurnAccelerationLabel => $"Turn: {TurnAccelerationMultiplier:0.00} 倍";
    public string VectorRotationLabel => $"{VectorRotationMultiplier:0.00}";
    public string InertiaDecelerationLabel => $"{InertiaDecelerationPerSecond:0.00} /秒";
    public string DragDecelerationExemptionDurationLabel =>
        $"Drag 免除時間: {DragDecelerationExemptionDurationRatio:0.00}";
    public string TurnDecelerationExemptionDurationLabel =>
        $"Turn 免除時間: {TurnDecelerationExemptionDurationRatio:0.00}";
    public string DecelerationExemptionStrengthLabel => $"免除割合: {DecelerationExemptionStrength:0.00}";
    public string DragSmoothLabel => $"Drag: {DragSmoothSeconds:0.00} 秒";
    public string TurnSmoothLabel => $"Turn: {TurnSmoothSeconds:0.00} 秒";
    public string BrakeLabel => $"{BrakeStrength:0.00}";
    public string BrakeRampLabel => $"適用時間: {BrakeRampSeconds:0.00} 秒";
    public string DragCorrectionTimeLabel => $"Drag 最大時間: {DragCorrectionMaxSeconds:0.0} 秒";
    public string DragCorrectionStrengthLabel => $"Drag 強度: {DragCorrectionStrength:0.00}";
    public string TurnCorrectionTimeLabel => $"Turn 最大時間: {TurnCorrectionMaxSeconds:0.0} 秒";
    public string TurnCorrectionStrengthLabel => $"Turn 強度: {TurnCorrectionStrength:0.00}";

    public MainViewModel(string libraryPath, string? settingsPath = null)
    {
        settingsStore = new(settingsPath);
        ApplySettings(settingsStore.Load());
        PropertyChanged += SettingsChanged;
        controller = new(
            () => new MonadoFlightRuntime(libraryPath),
            new UiProgress(Update),
            CreateMotionSettings);
    }

    private void SettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
            nameof(StepMode) or nameof(InertiaCutoffEnabled) or nameof(DragCutoffCentimetresPerSecond) or
            nameof(TurnCutoffDegreesPerSecond) or nameof(DragAccelerationMultiplier) or
            nameof(TurnAccelerationMultiplier) or nameof(VectorRotationMultiplier) or
            nameof(InertiaDecelerationPerSecond) or nameof(DecelerationExemptionEnabled) or
            nameof(DragDecelerationExemptionDurationRatio) or nameof(TurnDecelerationExemptionDurationRatio) or
            nameof(DecelerationExemptionStrength) or nameof(DragSmoothSeconds) or nameof(TurnSmoothSeconds) or
            nameof(BrakeStrength) or nameof(BrakeRampSeconds) or nameof(DirectionCorrectionEnabled) or nameof(DragCorrectionMaxSeconds) or
            nameof(DragCorrectionStrength) or nameof(TurnCorrectionMaxSeconds) or nameof(TurnCorrectionStrength)))
            return;

        settingsStore.Save(CreateMotionSettings());
    }

    private void ApplySettings(FlightMotionSettings settings)
    {
        settings = settings.Normalized();
        StepMode = settings.StepMode;
        InertiaCutoffEnabled = settings.InertiaCutoffEnabled;
        DragCutoffCentimetresPerSecond = Math.Round(settings.DragCutoffMetresPerSecond * 100, 6);
        TurnCutoffDegreesPerSecond = Math.Round(settings.TurnCutoffRadiansPerSecond * 180 / Math.PI, 4);
        DragAccelerationMultiplier = Math.Round(settings.DragAccelerationMultiplier, 6);
        TurnAccelerationMultiplier = Math.Round(settings.TurnAccelerationMultiplier, 6);
        VectorRotationMultiplier = Math.Round(settings.VectorRotationMultiplier, 6);
        InertiaDecelerationPerSecond = Math.Round(settings.InertiaDecelerationPerSecond, 6);
        DecelerationExemptionEnabled = settings.DecelerationExemptionEnabled;
        DragDecelerationExemptionDurationRatio = Math.Round(settings.DragDecelerationExemptionDurationRatio, 6);
        TurnDecelerationExemptionDurationRatio = Math.Round(settings.TurnDecelerationExemptionDurationRatio, 6);
        DecelerationExemptionStrength = Math.Round(settings.DecelerationExemptionStrength, 6);
        DragSmoothSeconds = Math.Round(settings.DragSmoothSeconds, 6);
        TurnSmoothSeconds = Math.Round(settings.TurnSmoothSeconds, 6);
        BrakeStrength = Math.Round(settings.BrakeStrength, 6);
        BrakeRampSeconds = Math.Round(settings.BrakeRampSeconds, 6);
        DirectionCorrectionEnabled = settings.DirectionCorrectionEnabled;
        DragCorrectionMaxSeconds = Math.Round(settings.DragCorrectionMaxSeconds, 6);
        DragCorrectionStrength = Math.Round(settings.DragCorrectionStrength, 6);
        TurnCorrectionMaxSeconds = Math.Round(settings.TurnCorrectionMaxSeconds, 6);
        TurnCorrectionStrength = Math.Round(settings.TurnCorrectionStrength, 6);
    }

    private void ResetToDefaults(Action<FlightMotionSettings> apply)
    {
        if (closing) return;
        apply(FlightMotionSettings.Default);
    }

    [RelayCommand] private void ResetStepMode() => ResetToDefaults(s => StepMode = s.StepMode);
    [RelayCommand] private void ResetInertiaCutoffEnabled() => ResetToDefaults(s => InertiaCutoffEnabled = s.InertiaCutoffEnabled);
    [RelayCommand] private void ResetDragCutoff() => ResetToDefaults(s => DragCutoffCentimetresPerSecond = s.DragCutoffMetresPerSecond * 100);
    [RelayCommand] private void ResetTurnCutoff() => ResetToDefaults(s => TurnCutoffDegreesPerSecond = Math.Round(s.TurnCutoffRadiansPerSecond * 180 / Math.PI, 4));
    [RelayCommand] private void ResetDragAcceleration() => ResetToDefaults(s => DragAccelerationMultiplier = s.DragAccelerationMultiplier);
    [RelayCommand] private void ResetTurnAcceleration() => ResetToDefaults(s => TurnAccelerationMultiplier = s.TurnAccelerationMultiplier);
    [RelayCommand] private void ResetVectorRotation() => ResetToDefaults(s => VectorRotationMultiplier = s.VectorRotationMultiplier);
    [RelayCommand] private void ResetDeceleration() => ResetToDefaults(s => InertiaDecelerationPerSecond = s.InertiaDecelerationPerSecond);
    [RelayCommand] private void ResetExemptionEnabled() => ResetToDefaults(s => DecelerationExemptionEnabled = s.DecelerationExemptionEnabled);
    [RelayCommand] private void ResetDragExemption() => ResetToDefaults(s => DragDecelerationExemptionDurationRatio = s.DragDecelerationExemptionDurationRatio);
    [RelayCommand] private void ResetTurnExemption() => ResetToDefaults(s => TurnDecelerationExemptionDurationRatio = s.TurnDecelerationExemptionDurationRatio);
    [RelayCommand] private void ResetExemptionStrength() => ResetToDefaults(s => DecelerationExemptionStrength = s.DecelerationExemptionStrength);
    [RelayCommand] private void ResetDragSmooth() => ResetToDefaults(s => DragSmoothSeconds = s.DragSmoothSeconds);
    [RelayCommand] private void ResetTurnSmooth() => ResetToDefaults(s => TurnSmoothSeconds = s.TurnSmoothSeconds);
    [RelayCommand] private void ResetBrake() => ResetToDefaults(s => BrakeStrength = s.BrakeStrength);
    [RelayCommand] private void ResetBrakeRamp() => ResetToDefaults(s => BrakeRampSeconds = s.BrakeRampSeconds);
    [RelayCommand] private void ResetDirectionCorrectionEnabled() => ResetToDefaults(s => DirectionCorrectionEnabled = s.DirectionCorrectionEnabled);
    [RelayCommand] private void ResetDragCorrectionTime() => ResetToDefaults(s => DragCorrectionMaxSeconds = s.DragCorrectionMaxSeconds);
    [RelayCommand] private void ResetDragCorrectionStrength() => ResetToDefaults(s => DragCorrectionStrength = s.DragCorrectionStrength);
    [RelayCommand] private void ResetTurnCorrectionTime() => ResetToDefaults(s => TurnCorrectionMaxSeconds = s.TurnCorrectionMaxSeconds);
    [RelayCommand] private void ResetTurnCorrectionStrength() => ResetToDefaults(s => TurnCorrectionStrength = s.TurnCorrectionStrength);

    [RelayCommand]
    private void Toggle()
    {
        if (closing) return;
        IsEnabled = !IsEnabled;
        Status = IsEnabled ? "接続・入力を準備しています…" : "オフ — 現在の位置・姿勢を保持します。";
        controller.SetEnabled(IsEnabled);
    }

    [RelayCommand]
    private async Task ToggleSettings()
    {
        if (closing)
            return;

        IsSettingsOpen = !IsSettingsOpen;
        settingsAnimation?.Cancel();
        settingsAnimation?.Dispose();
        settingsAnimation = new CancellationTokenSource();
        var cancellationToken = settingsAnimation.Token;
        double start = SettingsPanelWidth;
        double target = IsSettingsOpen ? settingsPanelTargetWidth : 0;

        try
        {
            for (int step = 1; step <= 12; step++)
            {
                await Task.Delay(16, cancellationToken);
                double progress = step / 12d;
                progress = 1 - Math.Pow(1 - progress, 3);
                double currentTarget = IsSettingsOpen ? settingsPanelTargetWidth : 0;
                SettingsPanelWidth = start + (currentTarget - start) * progress;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void SetSettingsPanelTargetWidth(double width)
    {
        settingsPanelTargetWidth = Math.Max(0, width);
        if (IsSettingsOpen)
            SettingsPanelWidth = settingsPanelTargetWidth;
    }

    [RelayCommand]
    private void Reset()
    {
        if (closing) return;
        IsEnabled = false;
        controller.Reset();
    }

    private void Update(FlightStatus state)
    {
        if (closing) { Console.Error.WriteLine(state.Message); return; }
        IsEnabled = state.Enabled;
        IsConnected = state.Connected;
        Status = state.Message;
        LeftStatus = state.Dragging ? "Space Drag — 操作中" : "左手グリップを握って移動";
        RightStatus = state.Turning ? "Space Turn — 操作中" : "右手グリップを握って全軸回転";
    }

    private FlightMotionSettings CreateMotionSettings() => new()
    {
        StepMode = StepMode,
        InertiaCutoffEnabled = InertiaCutoffEnabled,
        DragCutoffMetresPerSecond = (float)(DragCutoffCentimetresPerSecond / 100),
        TurnCutoffRadiansPerSecond = (float)(TurnCutoffDegreesPerSecond * Math.PI / 180),
        DragAccelerationMultiplier = (float)DragAccelerationMultiplier,
        TurnAccelerationMultiplier = (float)TurnAccelerationMultiplier,
        VectorRotationMultiplier = (float)VectorRotationMultiplier,
        InertiaDecelerationPerSecond = (float)InertiaDecelerationPerSecond,
        DecelerationExemptionEnabled = DecelerationExemptionEnabled,
        DragDecelerationExemptionDurationRatio = (float)DragDecelerationExemptionDurationRatio,
        TurnDecelerationExemptionDurationRatio = (float)TurnDecelerationExemptionDurationRatio,
        DecelerationExemptionStrength = (float)DecelerationExemptionStrength,
        DragSmoothSeconds = (float)DragSmoothSeconds,
        TurnSmoothSeconds = (float)TurnSmoothSeconds,
        BrakeStrength = (float)BrakeStrength,
        BrakeRampSeconds = (float)BrakeRampSeconds,
        DirectionCorrectionEnabled = DirectionCorrectionEnabled,
        DragCorrectionMaxSeconds = (float)DragCorrectionMaxSeconds,
        DragCorrectionStrength = (float)DragCorrectionStrength,
        TurnCorrectionMaxSeconds = (float)TurnCorrectionMaxSeconds,
        TurnCorrectionStrength = (float)TurnCorrectionStrength
    };

    public async ValueTask DisposeAsync()
    {
        closing = true;
        settingsAnimation?.Cancel();
        settingsAnimation?.Dispose();
        settingsStore.Save(CreateMotionSettings());
        IsEnabled = false;
        Status = "接続時の位置・姿勢に戻しています…";
        await controller.DisposeAsync();
    }

    private sealed class UiProgress(Action<FlightStatus> update) : IProgress<FlightStatus>
    {
        public void Report(FlightStatus value) => Dispatcher.UIThread.Post(() => update(value));
    }
}
