using System.Diagnostics;

namespace FlugelKranz.Core;

public interface IFlightRuntime : IDisposable
{
    RigidPose OriginalOffset { get; }
    RigidPose CurrentOffset { get; }
    InputFrame ReadPhysical();
    void Apply(RigidPose offset);
    void Restore();
}

public sealed record FlightStatus(
    bool Enabled,
    bool Connected,
    string Message,
    bool Dragging = false,
    bool Turning = false,
    FlightMode Mode = FlightMode.InfiniteWalking);

/// <summary>Owns the runtime on one worker. Off retains the offset; reset restores it without disabling the controller.</summary>
public sealed class FlightController(
    Func<IFlightRuntime> createRuntime,
    IProgress<FlightStatus> progress,
    Func<FlugelKranzSettings>? getSettings = null) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private Task? worker;
    private bool enabled, resetRequested, disposed;
    private long releaseVersion;

    public void SetEnabled(bool value)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            enabled = value;
            releaseVersion++;
            if (value && (worker is null || worker.IsCompleted)) worker = Task.Run(RunAsync);
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            releaseVersion++;
            resetRequested = true;
        }
    }

    private async Task RunAsync()
    {
        IFlightRuntime? runtime = null;
        string? error = null;
        FlightMode reportedMode = FlightMode.InfiniteWalking;
        try
        {
            reportedMode = (getSettings?.Invoke() ?? FlugelKranzSettings.Default).Normalized().Mode;
            lock (gate)
                progress.Report(new(enabled, false, "ランタイムに接続しています…", Mode: reportedMode));
            runtime = createRuntime();
            var freeFlight = new FreeFlightManipulator(runtime.CurrentOffset);
            var infiniteWalking = new InfiniteWalkingManipulator(runtime.CurrentOffset);
            long previousTimestamp = Stopwatch.GetTimestamp();
            long observedVersion = -1;
            FlightMode? appliedMode = null;
            InfiniteWalkingTransition? modeTransition = null;
            FlightMode? inputModeOverride = null;
            float modeSwitchSeconds = 0;
            bool modeSwitchTriggered = false;
            int tick = 0;
            while (!shutdown.IsCancellationRequested)
            {
                var frame = runtime.ReadPhysical();
                long timestamp = Stopwatch.GetTimestamp();
                float elapsedSeconds = (float)Stopwatch.GetElapsedTime(previousTimestamp, timestamp).TotalSeconds;
                previousTimestamp = timestamp;
                var settings = (getSettings?.Invoke() ?? FlugelKranzSettings.Default).Normalized();
                if (inputModeOverride is { } requested && settings.Mode == requested)
                    inputModeOverride = null;
                FlightMode selectedMode = inputModeOverride ?? settings.Mode;
                reportedMode = selectedMode;

                bool modeSwitchHeld = ModeSwitchHeld(frame);
                if (modeSwitchHeld)
                {
                    modeSwitchSeconds += elapsedSeconds;
                    if (!modeSwitchTriggered && modeSwitchSeconds >= 1)
                    {
                        selectedMode = selectedMode == FlightMode.InfiniteWalking
                            ? FlightMode.FreeFlight
                            : FlightMode.InfiniteWalking;
                        inputModeOverride = selectedMode;
                        reportedMode = selectedMode;
                        modeSwitchTriggered = true;
                        progress.Report(new(enabled, true, ModeChangedMessage(selectedMode),
                            Mode: selectedMode));
                    }
                }
                else
                {
                    modeSwitchSeconds = 0;
                    modeSwitchTriggered = false;
                }
                lock (gate)
                {
                    if (observedVersion != releaseVersion)
                    {
                        freeFlight.Release();
                        infiniteWalking.Release();
                        observedVersion = releaseVersion;
                    }
                    if (resetRequested)
                    {
                        runtime.Restore();
                        freeFlight.SetOffset(runtime.CurrentOffset);
                        infiniteWalking.SetOffset(runtime.CurrentOffset);
                        modeTransition = null;
                        appliedMode = selectedMode;
                        resetRequested = false;
                    }
                    if (enabled)
                    {
                        if (modeTransition is not null && selectedMode == FlightMode.FreeFlight)
                        {
                            modeTransition = null;
                            appliedMode = FlightMode.FreeFlight;
                            freeFlight.SetOffset(runtime.CurrentOffset);
                            infiniteWalking.SetOffset(runtime.CurrentOffset);
                        }
                        if (modeTransition is null && appliedMode != selectedMode)
                        {
                            if (appliedMode == FlightMode.FreeFlight &&
                                selectedMode == FlightMode.InfiniteWalking)
                            {
                                modeTransition = new(runtime.CurrentOffset, runtime.OriginalOffset);
                                freeFlight.Release();
                                infiniteWalking.Release();
                            }
                            else
                            {
                                freeFlight.SetOffset(runtime.CurrentOffset);
                                infiniteWalking.SetOffset(runtime.CurrentOffset);
                                appliedMode = selectedMode;
                            }
                        }

                        RigidPose offset;
                        if (modeTransition is not null)
                        {
                            offset = modeTransition.Advance(elapsedSeconds);
                            if (modeTransition.IsComplete)
                            {
                                freeFlight.SetOffset(offset);
                                infiniteWalking.SetOffset(offset);
                                appliedMode = FlightMode.InfiniteWalking;
                                modeTransition = null;
                            }
                        }
                        else
                        {
                            offset = selectedMode == FlightMode.FreeFlight
                                ? freeFlight.Update(frame, elapsedSeconds, settings.FreeFlight)
                                : infiniteWalking.Update(frame, elapsedSeconds, settings.InfiniteWalking);
                        }
                        // Use exact equality here: a tolerance would accumulate un-applied substeps as feedback.
                        if (offset != runtime.CurrentOffset) runtime.Apply(offset);
                    }
                    else
                    {
                        modeTransition = null;
                        freeFlight.Release();
                        infiniteWalking.Release();
                    }
                    if (tick++ % 10 == 0)
                    {
                        bool dragging = modeTransition is not null ? false
                            : selectedMode == FlightMode.FreeFlight
                            ? freeFlight.IsDragging
                            : infiniteWalking.IsDragging;
                        bool turning = modeTransition is not null ? false
                            : selectedMode == FlightMode.FreeFlight
                            ? freeFlight.IsTurning
                            : infiniteWalking.IsTurning;
                        string message = !enabled ? "オフ — 現在の位置・姿勢を保持しています。"
                            : modeTransition is not null ? "無限歩行モードへ戻しています…"
                            : !frame.HeadTracked ? "HMD のトラッキングを待っています。"
                            : !frame.Left.IsTracked || !frame.Right.IsTracked ? "コントローラーの姿勢・操作入力を待っています。"
                            : "オン — 操作入力を一度離してから使用してください。";
                        progress.Report(new(enabled, true, message, dragging, turning, selectedMode));
                    }
                }
                await Task.Delay(10, shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception exception) { error = exception.Message; }
        finally
        {
            if (runtime is not null)
            {
                try { runtime.Restore(); }
                catch (Exception exception) { error = $"{error}\n復元に失敗しました: {exception.Message}".Trim(); }
                finally { runtime.Dispose(); }
            }
            lock (gate)
            {
                enabled = false;
                resetRequested = false;
                worker = null;
                if (error is not null) Console.Error.WriteLine(error);
                progress.Report(new(
                    false,
                    false,
                    error ?? "終了しました。接続時の位置・姿勢へ復元しました。",
                    Mode: reportedMode));
            }
        }
    }

    private static bool ModeSwitchHeld(InputFrame frame) =>
        frame.Left.IsTracked && frame.Left.Pose.IsValid &&
        frame.Right.IsTracked && frame.Right.Pose.IsValid &&
        float.IsFinite(frame.Left.ModeSwitch) && frame.Left.ModeSwitch >= 0.65f &&
        float.IsFinite(frame.Right.ModeSwitch) && frame.Right.ModeSwitch >= 0.65f;

    private static string ModeChangedMessage(FlightMode mode) => mode == FlightMode.InfiniteWalking
        ? "無限歩行モードへ切り替えました。操作入力を離してから使用してください。"
        : "自由飛行モードへ切り替えました。操作入力を離してから使用してください。";

    public async ValueTask DisposeAsync()
    {
        Task? pending;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            enabled = false;
            shutdown.Cancel();
            pending = worker;
        }
        if (pending is not null) await pending.ConfigureAwait(false);
        shutdown.Dispose();
    }
}
