using System.Diagnostics;
using System.Numerics;

namespace FlugelKranz.Core;

public interface IFlightRuntime : IDisposable
{
    RigidPose OriginalOffset { get; }
    RigidPose CurrentOffset { get; }
    InputFrame ReadPhysical();
    void Apply(RigidPose offset);
    void Restore();
}

public interface IReferenceSpaceOffsetProvider
{
    RigidPose ReferenceSpaceOffset { get; }
}

public interface IHapticFeedback
{
    void SendHapticPulse(float durationSeconds, float frequencyHz, float amplitude);
}

public sealed record FlightStatus(
    bool Enabled,
    bool Connected,
    string Message,
    bool Dragging = false,
    bool Turning = false,
    FlightMode Mode = FlightMode.InfiniteWalking,
    float? LeftTrackpadForce = null,
    float? RightTrackpadForce = null,
    RigidPose? ReferenceSpaceOffset = null,
    Vector3? RecentReferenceSpaceMovement = null);

/// <summary>Owns the runtime on one worker. Off retains the offset; reset restores it without disabling the controller.</summary>
public sealed class FlightController(
    Func<IFlightRuntime> createRuntime,
    IProgress<FlightStatus> progress,
    Func<FlugelKranzSettings>? getSettings = null,
    bool retryRuntimeConnection = false) : IAsyncDisposable
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
            while (!shutdown.IsCancellationRequested)
            {
                try
                {
                    runtime = createRuntime();
                    break;
                }
                catch (Exception exception) when (retryRuntimeConnection)
                {
                    lock (gate)
                    {
                        if (!enabled)
                            return;
                    }

                    progress.Report(new(
                        true,
                        false,
                        $"Monado / WiVRn を待っています。再接続時に OpenXR の設定を再探索します。({exception.Message})",
                        Mode: reportedMode));
                    await Task.Delay(TimeSpan.FromSeconds(1), shutdown.Token).ConfigureAwait(false);
                }
            }

            if (runtime is null)
                return;
            var session = new FlightSession(runtime, progress,
                (getSettings?.Invoke() ?? FlugelKranzSettings.Default).Normalized());
            long previousTimestamp = Stopwatch.GetTimestamp();
            long observedVersion = -1;
            while (!shutdown.IsCancellationRequested)
            {
                var frame = runtime.ReadPhysical();
                long timestamp = Stopwatch.GetTimestamp();
                float elapsedSeconds = (float)Stopwatch.GetElapsedTime(previousTimestamp, timestamp).TotalSeconds;
                previousTimestamp = timestamp;
                var settings = (getSettings?.Invoke() ?? FlugelKranzSettings.Default).Normalized();
                lock (gate)
                {
                    if (observedVersion != releaseVersion)
                    {
                        session.Release();
                        observedVersion = releaseVersion;
                    }
                    if (resetRequested)
                    {
                        session.Reset();
                        resetRequested = false;
                    }
                    session.Update(frame, elapsedSeconds, settings, enabled);
                    reportedMode = session.Mode;
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
