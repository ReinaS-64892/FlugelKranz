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
    [ObservableProperty] private string leftStatus = "左右の操作入力から Drag";
    [ObservableProperty] private string rightStatus = "左右の操作入力から Turn";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeLabel))]
    [NotifyPropertyChangedFor(nameof(ModeDescription))]
    private FlightMode mode = FlightMode.InfiniteWalking;
    [ObservableProperty] private bool useHeadTurnOrigin;
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
    private double turnAccelerationMultiplier = 0.4;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZAccelerationLabel))]
    private double zAccelerationMultiplier = 2;
    [ObservableProperty] private bool inertiaAccelerationBoostEnabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InertiaAccelerationBoostMaximumLabel))]
    private double inertiaAccelerationBoostMaximumMultiplier = 4;
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
    private double turnDecelerationExemptionDurationRatio = 0.15;
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
    [NotifyPropertyChangedFor(nameof(InfiniteDragSmoothLabel))]
    private double infiniteDragSmoothSeconds;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfiniteTurnSmoothLabel))]
    private double infiniteTurnSmoothSeconds;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InfiniteTurnHeadSmoothLabel))]
    private double infiniteTurnHeadSmoothSeconds;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrakeRampLabel))]
    private double brakeRampSeconds = 0.4;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValveIndexPositionDeadZoneLabel))]
    private double valveIndexPositionDeadZone = 0.3;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValveIndexForceThresholdLabel))]
    private double valveIndexForceThreshold = 0.2;
    [ObservableProperty] private bool isValveIndexDetected;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValveIndexForceStatus))]
    private double leftValveIndexForce;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValveIndexForceStatus))]
    private double rightValveIndexForce;
    public string ToggleLabel => IsEnabled ? "ON" : "OFF";
    public string ModeLabel => Mode == FlightMode.InfiniteWalking ? "I" : "F";
    public string ModeDescription => Mode == FlightMode.InfiniteWalking ? "無限歩行モード" : "自由飛行モード";
    public string DragCutoffLabel => $"Drag: {DragCutoffCentimetresPerSecond:0.0} cm/s";
    public string TurnCutoffLabel => $"Turn: {TurnCutoffDegreesPerSecond:0.0} °/s";
    public string DragAccelerationLabel => $"Drag: {DragAccelerationMultiplier:0.00} 倍";
    public string TurnAccelerationLabel => $"Turn: {TurnAccelerationMultiplier:0.00} 倍";
    public string ZAccelerationLabel => $"Z: {ZAccelerationMultiplier:0.00} 倍";
    public string InertiaAccelerationBoostMaximumLabel =>
        $"最大加速倍率: {InertiaAccelerationBoostMaximumMultiplier:0.00} 倍";
    public string VectorRotationLabel => $"倍率: {VectorRotationMultiplier:0.00}";
    public string InertiaDecelerationLabel => $"{InertiaDecelerationPerSecond:0.00} /秒";
    public string DragDecelerationExemptionDurationLabel =>
        $"Drag 免除時間: {DragDecelerationExemptionDurationRatio:0.00}";
    public string TurnDecelerationExemptionDurationLabel =>
        $"Turn 免除時間: {TurnDecelerationExemptionDurationRatio:0.00}";
    public string DecelerationExemptionStrengthLabel => $"免除割合: {DecelerationExemptionStrength:0.00}";
    public string DragSmoothLabel => $"Drag: {DragSmoothSeconds:0.00} 秒";
    public string TurnSmoothLabel => $"Turn: {TurnSmoothSeconds:0.00} 秒";
    public string InfiniteDragSmoothLabel => $"Drag: {InfiniteDragSmoothSeconds:0.00} 秒";
    public string InfiniteTurnSmoothLabel => $"Turn: {InfiniteTurnSmoothSeconds:0.00} 秒";
    public string InfiniteTurnHeadSmoothLabel => $"Turn Head: {InfiniteTurnHeadSmoothSeconds:0.00} 秒";
    public string BrakeRampLabel => $"適用時間: {BrakeRampSeconds:0.00} 秒";
    public string ValveIndexPositionDeadZoneLabel => $"位置デッドゾーン: {ValveIndexPositionDeadZone:0.00}";
    public string ValveIndexForceThresholdLabel => $"Force 閾値: {ValveIndexForceThreshold:0.00}";
    public string ValveIndexForceStatus =>
        $"Valve Index force — 左: {LeftValveIndexForce:0.00} / 右: {RightValveIndexForce:0.00}";

    public MainViewModel(string libraryPath, string? settingsPath = null)
    {
        settingsStore = new(settingsPath);
        ApplySettings(settingsStore.Load());
        PropertyChanged += SettingsChanged;
        controller = new(
            () => new MonadoFlightRuntime(libraryPath, CreateValveIndexSettings),
            new UiProgress(Update),
            CreateSettings);
        if (File.Exists(libraryPath))
        {
            Mode = FlightMode.InfiniteWalking;
            IsEnabled = true;
            Status = "無限歩行モードで接続・入力を準備しています…";
            controller.SetEnabled(true);
        }
    }

    private void SettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
            nameof(Mode) or nameof(UseHeadTurnOrigin) or
            nameof(InertiaCutoffEnabled) or nameof(DragCutoffCentimetresPerSecond) or
            nameof(TurnCutoffDegreesPerSecond) or nameof(DragAccelerationMultiplier) or
            nameof(TurnAccelerationMultiplier) or nameof(ZAccelerationMultiplier) or
            nameof(InertiaAccelerationBoostEnabled) or nameof(InertiaAccelerationBoostMaximumMultiplier) or
            nameof(VectorRotationMultiplier) or
            nameof(InertiaDecelerationPerSecond) or nameof(DecelerationExemptionEnabled) or
            nameof(DragDecelerationExemptionDurationRatio) or nameof(TurnDecelerationExemptionDurationRatio) or
            nameof(DecelerationExemptionStrength) or nameof(DragSmoothSeconds) or nameof(TurnSmoothSeconds) or
            nameof(InfiniteDragSmoothSeconds) or nameof(InfiniteTurnSmoothSeconds) or
            nameof(InfiniteTurnHeadSmoothSeconds) or
            nameof(ValveIndexPositionDeadZone) or nameof(ValveIndexForceThreshold) or
            nameof(BrakeRampSeconds)))
            return;

        settingsStore.Save(CreateSettings());
    }

    private void ApplySettings(FlugelKranzSettings root)
    {
        root = root.Normalized();
        Mode = root.Mode;
        var settings = root.FreeFlight;
        UseHeadTurnOrigin = settings.TurnOrigin == TurnOrigin.Head;
        InertiaCutoffEnabled = settings.InertiaCutoffEnabled;
        DragCutoffCentimetresPerSecond = Math.Round(settings.DragCutoffMetresPerSecond * 100, 6);
        TurnCutoffDegreesPerSecond = Math.Round(settings.TurnCutoffRadiansPerSecond * 180 / Math.PI, 4);
        DragAccelerationMultiplier = Math.Round(settings.DragAccelerationMultiplier, 6);
        TurnAccelerationMultiplier = Math.Round(settings.TurnAccelerationMultiplier, 6);
        ZAccelerationMultiplier = Math.Round(settings.ZAccelerationMultiplier, 6);
        InertiaAccelerationBoostEnabled = settings.InertiaAccelerationBoostEnabled;
        InertiaAccelerationBoostMaximumMultiplier = Math.Round(settings.InertiaAccelerationBoostMaximumMultiplier, 6);
        VectorRotationMultiplier = Math.Round(settings.VectorRotationMultiplier, 6);
        InertiaDecelerationPerSecond = Math.Round(settings.InertiaDecelerationPerSecond, 6);
        DecelerationExemptionEnabled = settings.DecelerationExemptionEnabled;
        DragDecelerationExemptionDurationRatio = Math.Round(settings.DragDecelerationExemptionDurationRatio, 6);
        TurnDecelerationExemptionDurationRatio = Math.Round(settings.TurnDecelerationExemptionDurationRatio, 6);
        DecelerationExemptionStrength = Math.Round(settings.DecelerationExemptionStrength, 6);
        DragSmoothSeconds = Math.Round(settings.DragSmoothSeconds, 6);
        TurnSmoothSeconds = Math.Round(settings.TurnSmoothSeconds, 6);
        BrakeRampSeconds = Math.Round(settings.BrakeRampSeconds, 6);
        InfiniteDragSmoothSeconds = Math.Round(root.InfiniteWalking.DragSmoothSeconds, 6);
        InfiniteTurnSmoothSeconds = Math.Round(root.InfiniteWalking.TurnSmoothSeconds, 6);
        InfiniteTurnHeadSmoothSeconds = Math.Round(root.InfiniteWalking.TurnHeadSmoothSeconds, 6);
        ValveIndexPositionDeadZone = Math.Round(root.ValveIndex.PositionDeadZone, 6);
        ValveIndexForceThreshold = Math.Round(root.ValveIndex.ForceThreshold, 6);
    }

    private void ResetToDefaults(Action<FlightMotionSettings> apply)
    {
        if (closing) return;
        apply(FlightMotionSettings.Default);
    }

    [RelayCommand] private void ResetTurnOrigin() => ResetToDefaults(s => UseHeadTurnOrigin = s.TurnOrigin == TurnOrigin.Head);
    [RelayCommand] private void ResetInertiaCutoffEnabled() => ResetToDefaults(s => InertiaCutoffEnabled = s.InertiaCutoffEnabled);
    [RelayCommand] private void ResetDragCutoff() => ResetToDefaults(s => DragCutoffCentimetresPerSecond = s.DragCutoffMetresPerSecond * 100);
    [RelayCommand] private void ResetTurnCutoff() => ResetToDefaults(s => TurnCutoffDegreesPerSecond = Math.Round(s.TurnCutoffRadiansPerSecond * 180 / Math.PI, 4));
    [RelayCommand] private void ResetDragAcceleration() => ResetToDefaults(s => DragAccelerationMultiplier = s.DragAccelerationMultiplier);
    [RelayCommand] private void ResetTurnAcceleration() => ResetToDefaults(s => TurnAccelerationMultiplier = s.TurnAccelerationMultiplier);
    [RelayCommand] private void ResetZAcceleration() => ResetToDefaults(s => ZAccelerationMultiplier = s.ZAccelerationMultiplier);
    [RelayCommand] private void ResetInertiaAccelerationBoostEnabled() => ResetToDefaults(s => InertiaAccelerationBoostEnabled = s.InertiaAccelerationBoostEnabled);
    [RelayCommand] private void ResetInertiaAccelerationBoostMaximumMultiplier() => ResetToDefaults(s => InertiaAccelerationBoostMaximumMultiplier = s.InertiaAccelerationBoostMaximumMultiplier);
    [RelayCommand] private void ResetVectorRotation() => ResetToDefaults(s => VectorRotationMultiplier = s.VectorRotationMultiplier);
    [RelayCommand] private void ResetDeceleration() => ResetToDefaults(s => InertiaDecelerationPerSecond = s.InertiaDecelerationPerSecond);
    [RelayCommand] private void ResetExemptionEnabled() => ResetToDefaults(s => DecelerationExemptionEnabled = s.DecelerationExemptionEnabled);
    [RelayCommand] private void ResetDragExemption() => ResetToDefaults(s => DragDecelerationExemptionDurationRatio = s.DragDecelerationExemptionDurationRatio);
    [RelayCommand] private void ResetTurnExemption() => ResetToDefaults(s => TurnDecelerationExemptionDurationRatio = s.TurnDecelerationExemptionDurationRatio);
    [RelayCommand] private void ResetExemptionStrength() => ResetToDefaults(s => DecelerationExemptionStrength = s.DecelerationExemptionStrength);
    [RelayCommand] private void ResetDragSmooth() => ResetToDefaults(s => DragSmoothSeconds = s.DragSmoothSeconds);
    [RelayCommand] private void ResetTurnSmooth() => ResetToDefaults(s => TurnSmoothSeconds = s.TurnSmoothSeconds);
    [RelayCommand] private void ResetBrakeRamp() => ResetToDefaults(s => BrakeRampSeconds = s.BrakeRampSeconds);
    [RelayCommand] private void ResetInfiniteDragSmooth() => InfiniteDragSmoothSeconds = InfiniteWalkingSettings.Default.DragSmoothSeconds;
    [RelayCommand] private void ResetInfiniteTurnSmooth() => InfiniteTurnSmoothSeconds = InfiniteWalkingSettings.Default.TurnSmoothSeconds;
    [RelayCommand] private void ResetInfiniteTurnHeadSmooth() => InfiniteTurnHeadSmoothSeconds = InfiniteWalkingSettings.Default.TurnHeadSmoothSeconds;
    [RelayCommand] private void ResetValveIndexPositionDeadZone() => ValveIndexPositionDeadZone = ValveIndexInputSettings.Default.PositionDeadZone;
    [RelayCommand] private void ResetValveIndexForceThreshold() => ValveIndexForceThreshold = Math.Round(ValveIndexInputSettings.Default.ForceThreshold, 6);
    [RelayCommand] private void ResetMode() => Mode = FlightMode.InfiniteWalking;

    [RelayCommand]
    private void ToggleMode()
    {
        if (closing) return;
        Mode = Mode == FlightMode.InfiniteWalking ? FlightMode.FreeFlight : FlightMode.InfiniteWalking;
        Status = $"{ModeDescription}へ切り替えました。操作入力を離してから使用してください。";
    }

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
        controller.Reset();
    }

    private void Update(FlightStatus state)
    {
        if (closing) { Console.Error.WriteLine(state.Message); return; }
        IsEnabled = state.Enabled;
        IsConnected = state.Connected;
        Mode = state.Mode;
        Status = state.Message;
        LeftStatus = state.Dragging ? "Space Drag — 操作中" : "左右の操作入力から Drag";
        RightStatus = state.Turning ? "Space Turn — 操作中" : "左右の操作入力から Turn";
        IsValveIndexDetected = state.LeftTrackpadForce.HasValue || state.RightTrackpadForce.HasValue;
        LeftValveIndexForce = state.LeftTrackpadForce ?? 0;
        RightValveIndexForce = state.RightTrackpadForce ?? 0;
    }

    private FlugelKranzSettings CreateSettings() => new()
    {
        Mode = Mode,
        FreeFlight = new()
        {
            TurnOrigin = UseHeadTurnOrigin ? TurnOrigin.Head : TurnOrigin.TrackedElementsMidpoint,
            InertiaCutoffEnabled = InertiaCutoffEnabled,
            DragCutoffMetresPerSecond = (float)(DragCutoffCentimetresPerSecond / 100),
            TurnCutoffRadiansPerSecond = (float)(TurnCutoffDegreesPerSecond * Math.PI / 180),
            DragAccelerationMultiplier = (float)DragAccelerationMultiplier,
            TurnAccelerationMultiplier = (float)TurnAccelerationMultiplier,
            ZAccelerationMultiplier = (float)ZAccelerationMultiplier,
            InertiaAccelerationBoostEnabled = InertiaAccelerationBoostEnabled,
            InertiaAccelerationBoostMaximumMultiplier = (float)InertiaAccelerationBoostMaximumMultiplier,
            VectorRotationMultiplier = (float)VectorRotationMultiplier,
            InertiaDecelerationPerSecond = (float)InertiaDecelerationPerSecond,
            DecelerationExemptionEnabled = DecelerationExemptionEnabled,
            DragDecelerationExemptionDurationRatio = (float)DragDecelerationExemptionDurationRatio,
            TurnDecelerationExemptionDurationRatio = (float)TurnDecelerationExemptionDurationRatio,
            DecelerationExemptionStrength = (float)DecelerationExemptionStrength,
            DragSmoothSeconds = (float)DragSmoothSeconds,
            TurnSmoothSeconds = (float)TurnSmoothSeconds,
            BrakeRampSeconds = (float)BrakeRampSeconds
        },
        InfiniteWalking = new()
        {
            DragSmoothSeconds = (float)InfiniteDragSmoothSeconds,
            TurnSmoothSeconds = (float)InfiniteTurnSmoothSeconds,
            TurnHeadSmoothSeconds = (float)InfiniteTurnHeadSmoothSeconds
        },
        ValveIndex = CreateValveIndexSettings()
    };

    private ValveIndexInputSettings CreateValveIndexSettings() => new()
    {
        PositionDeadZone = (float)ValveIndexPositionDeadZone,
        ForceThreshold = (float)ValveIndexForceThreshold
    };

    public async ValueTask DisposeAsync()
    {
        closing = true;
        settingsAnimation?.Cancel();
        settingsAnimation?.Dispose();
        settingsStore.Save(CreateSettings());
        IsEnabled = false;
        Status = "接続時の位置・姿勢に戻しています…";
        await controller.DisposeAsync();
    }

    private sealed class UiProgress(Action<FlightStatus> update) : IProgress<FlightStatus>
    {
        public void Report(FlightStatus value) => Dispatcher.UIThread.Post(() => update(value));
    }
}
