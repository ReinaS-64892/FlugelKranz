using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlugelKranz.Core;
using FlugelKranz.OpenXR;

namespace FlugelKranz.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly FlightController controller;
    private bool closing;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool isEnabled;
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
    private double turnCutoffDegreesPerSecond = 90;
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
    [NotifyPropertyChangedFor(nameof(DecelerationExemptionDurationLabel))]
    private double decelerationExemptionDurationRatio = 0.7;
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
    public string ToggleLabel => IsEnabled ? "オフにする" : "オンにする";
    public string DragCutoffLabel => $"Drag: {DragCutoffCentimetresPerSecond:0.0} cm/s";
    public string TurnCutoffLabel => $"Turn: {TurnCutoffDegreesPerSecond:0.0} °/s";
    public string DragAccelerationLabel => $"Drag: {DragAccelerationMultiplier:0.00} 倍";
    public string TurnAccelerationLabel => $"Turn: {TurnAccelerationMultiplier:0.00} 倍";
    public string VectorRotationLabel => $"{VectorRotationMultiplier:0.00}";
    public string InertiaDecelerationLabel => $"{InertiaDecelerationPerSecond:0.00} /秒";
    public string DecelerationExemptionDurationLabel => $"免除時間: {DecelerationExemptionDurationRatio:0.00}";
    public string DecelerationExemptionStrengthLabel => $"免除割合: {DecelerationExemptionStrength:0.00}";
    public string DragSmoothLabel => $"Drag: {DragSmoothSeconds:0.00} 秒";
    public string TurnSmoothLabel => $"Turn: {TurnSmoothSeconds:0.00} 秒";
    public string BrakeLabel => $"{BrakeStrength:0.00}";
    public string DragCorrectionTimeLabel => $"Drag 最大時間: {DragCorrectionMaxSeconds:0.0} 秒";
    public string DragCorrectionStrengthLabel => $"Drag 強度: {DragCorrectionStrength:0.00}";
    public string TurnCorrectionTimeLabel => $"Turn 最大時間: {TurnCorrectionMaxSeconds:0.0} 秒";
    public string TurnCorrectionStrengthLabel => $"Turn 強度: {TurnCorrectionStrength:0.00}";

    public MainViewModel(string libraryPath)
    {
        controller = new(
            () => new MonadoFlightRuntime(libraryPath),
            new UiProgress(Update),
            CreateMotionSettings);
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
        DecelerationExemptionDurationRatio = (float)DecelerationExemptionDurationRatio,
        DecelerationExemptionStrength = (float)DecelerationExemptionStrength,
        DragSmoothSeconds = (float)DragSmoothSeconds,
        TurnSmoothSeconds = (float)TurnSmoothSeconds,
        BrakeStrength = (float)BrakeStrength,
        DirectionCorrectionEnabled = DirectionCorrectionEnabled,
        DragCorrectionMaxSeconds = (float)DragCorrectionMaxSeconds,
        DragCorrectionStrength = (float)DragCorrectionStrength,
        TurnCorrectionMaxSeconds = (float)TurnCorrectionMaxSeconds,
        TurnCorrectionStrength = (float)TurnCorrectionStrength
    };

    public async ValueTask DisposeAsync()
    {
        closing = true;
        IsEnabled = false;
        Status = "接続時の位置・姿勢に戻しています…";
        await controller.DisposeAsync();
    }

    private sealed class UiProgress(Action<FlightStatus> update) : IProgress<FlightStatus>
    {
        public void Report(FlightStatus value) => Dispatcher.UIThread.Post(() => update(value));
    }
}
