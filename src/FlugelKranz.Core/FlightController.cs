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

public sealed record FlightStatus(bool Enabled, bool Connected, string Message, bool Dragging = false, bool Turning = false);

/// <summary>Owns the runtime on one worker. Off retains the offset; reset and normal shutdown restore it.</summary>
public sealed class FlightController(
    Func<IFlightRuntime> createRuntime,
    IProgress<FlightStatus> progress,
    Func<FlightMotionSettings>? getSettings = null) : IAsyncDisposable
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
            enabled = false;
            releaseVersion++;
            resetRequested = true;
        }
    }

    private async Task RunAsync()
    {
        IFlightRuntime? runtime = null;
        string? error = null;
        try
        {
            lock (gate) progress.Report(new(enabled, false, "ランタイムに接続しています…"));
            runtime = createRuntime();
            var manipulator = new SpaceManipulator(runtime.CurrentOffset);
            long previousTimestamp = Stopwatch.GetTimestamp();
            long observedVersion = -1;
            int tick = 0;
            while (!shutdown.IsCancellationRequested)
            {
                var frame = runtime.ReadPhysical();
                long timestamp = Stopwatch.GetTimestamp();
                float elapsedSeconds = (float)Stopwatch.GetElapsedTime(previousTimestamp, timestamp).TotalSeconds;
                previousTimestamp = timestamp;
                var settings = getSettings?.Invoke() ?? FlightMotionSettings.Default;
                lock (gate)
                {
                    if (observedVersion != releaseVersion)
                    {
                        manipulator.Release();
                        observedVersion = releaseVersion;
                    }
                    if (resetRequested)
                    {
                        runtime.Restore();
                        manipulator.SetOffset(runtime.CurrentOffset);
                        resetRequested = false;
                    }
                    if (enabled)
                    {
                        var offset = manipulator.Update(frame, elapsedSeconds, settings);
                        // Use exact equality here: a tolerance would accumulate un-applied substeps as feedback.
                        if (offset != runtime.CurrentOffset) runtime.Apply(offset);
                    }
                    else manipulator.Release();
                    if (tick++ % 10 == 0)
                    {
                        string message = !enabled ? "オフ — 現在の位置・姿勢を保持しています。"
                            : !frame.HeadTracked ? "HMD のトラッキングを待っています。"
                            : !frame.Left.IsTracked || !frame.Right.IsTracked ? "コントローラーの姿勢・グリップ入力を待っています。"
                            : "オン — グリップを一度離してから操作してください。";
                        progress.Report(new(enabled, true, message, manipulator.IsDragging, manipulator.IsTurning));
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
                progress.Report(new(false, false, error ?? "終了しました。接続時の位置・姿勢へ復元しました。"));
            }
        }
    }

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
