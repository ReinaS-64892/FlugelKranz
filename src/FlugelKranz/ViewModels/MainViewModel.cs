using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlugelKranz.Core;
using MonadoXrApi;

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
    public string ToggleLabel => IsEnabled ? "オフにする" : "オンにする";

    public MainViewModel(string libraryPath)
    {
        controller = new(() => new MonadoFlightRuntime(libraryPath), new UiProgress(Update));
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
