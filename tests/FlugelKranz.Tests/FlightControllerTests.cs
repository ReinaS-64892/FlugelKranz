using System.Collections.Concurrent;
using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class FlightControllerTests
{
    [Fact]
    public async Task PacedRuntimeUsesFrameTimeForGesturesAndEndsEveryFrameBeforeWaitingAgain()
    {
        var runtime = new PacedRuntime { Frame = ModeFrame(1), MaximumFrames = 3 };
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => runtime.Disposed);
        Assert.Equal(["wait", "midpoint", "commit", "end", "wait", "midpoint", "commit", "end", "wait", "midpoint", "commit", "end", "wait"], runtime.Calls);
        Assert.Contains(progress.Statuses, status => status.Mode == FlightMode.FreeFlight && status.Message.Contains("切り替え"));
        Assert.Equal(6, runtime.Reads);
    }

    [Fact]
    public async Task APressBeginningBetweenFramesIsObservedByTheAdditionalUpdate()
    {
        PacedRuntime? runtime = null;
        runtime = new PacedRuntime
        {
            MaximumFrames = 2,
            OnMidpoint = () => runtime!.Frame = ModeFrame(1)
        };
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => runtime.Disposed);
        // Midpoint 1 observes the press, start 2 and midpoint 2 accumulate 1.2s.
        // With one read per frame this press would still be below the hold threshold.
        Assert.Contains(progress.Statuses, status => status.Mode == FlightMode.FreeFlight);
        Assert.Equal(4, runtime.Reads);
        Assert.Equal(2, runtime.Calls.Count(call => call == "commit"));
    }

    [Fact]
    public async Task AStaleMidpointIsSkippedButTheFrameIsStillCommittedAndEnded()
    {
        var runtime = new PacedRuntime { MaximumFrames = 1, AdditionalUpdate = false };
        await using var controller = new FlightController(() => runtime, new Recorder());
        controller.SetEnabled(true);
        await Wait(() => runtime.Disposed);
        Assert.Equal(1, runtime.Reads);
        Assert.Equal(["wait", "midpoint", "commit", "end", "wait"], runtime.Calls);
    }

    [Fact]
    public async Task FirstObservedPressDoesNotCountThePrecedingLongIntervalAsHeld()
    {
        var runtime = new PacedRuntime { Frame = ModeFrame(1), MaximumFrames = 1, FrameElapsedSeconds = 1.2f, AdditionalUpdate = false };
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => runtime.Disposed);
        Assert.DoesNotContain(progress.Statuses, status => status.Mode == FlightMode.FreeFlight);
    }

    [Fact]
    public async Task InputFailureStillEndsTheBegunFrameBeforeRestoringAndDisposing()
    {
        var runtime = new PacedRuntime { ThrowOnRead = true };
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => runtime.Disposed);
        Assert.Equal(["wait", "end"], runtime.Calls);
        Assert.True(runtime.Restores > 0);
    }

    [Fact]
    public async Task SecondInputFailureEndsTheFrameWithoutCommittingPartialComputation()
    {
        PacedRuntime? runtime = null;
        runtime = new PacedRuntime { OnMidpoint = () => runtime!.ThrowOnRead = true };
        await using var controller = new FlightController(() => runtime, new Recorder());
        controller.SetEnabled(true);
        await Wait(() => runtime.Disposed);
        Assert.Equal(["wait", "midpoint", "end"], runtime.Calls);
        Assert.Equal(2, runtime.Reads);
        Assert.True(runtime.Restores > 0);
    }

    [Fact]
    public async Task StatusNotificationsFollowElapsedTimeInsteadOfFrameCount()
    {
        var runtime = new PacedRuntime { MaximumFrames = 26, FrameElapsedSeconds = 0.002f };
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => runtime.Disposed);
        Assert.Equal(52, runtime.Reads);
        Assert.Equal(2, progress.Statuses.Count(status => status.Connected));
    }

    [Fact]
    public async Task TrackerReleaseUpdatesRuntimeWhileSpaceIsStationaryAndOffClearsCorrections()
    {
        var runtime = new RotationRuntime();
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));
        var frame = Frame(0, 0) with { Trackers = [new("toe", new(0, 0.05f, 0), true)] };
        await WaitForFrame(runtime, frame);
        frame = frame with { Left = frame.Left with { Turn = 1 } };
        await WaitForFrame(runtime, frame);
        frame = frame with { Left = frame.Left with
        {
            Pose = new(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f), Vector3.Zero)
        } };
        await WaitForFrame(runtime, frame);
        await Wait(() => runtime.Rotations.Count == 1);
        var retained = runtime.CurrentOffset;
        // No further space rotation: only the compensation needs to be updated.
        await WaitForFrame(runtime, frame with { Trackers = [new("toe", new(0, 0.3f, 0), true)] });
        await Wait(() => runtime.Rotations.Count == 0);
        Assert.Equal(retained, runtime.CurrentOffset);
        await WaitForFrame(runtime, frame);
        await Task.Delay(700, TestContext.Current.CancellationToken); // Let the re-planted foot settle below the contact-speed threshold.
        frame = frame with { Left = frame.Left with
        {
            Pose = new(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1), Vector3.Zero)
        } };
        await WaitForFrame(runtime, frame);
        await Wait(() => runtime.Rotations.Count == 1);
        retained = runtime.CurrentOffset;
        controller.SetEnabled(false);
        await Wait(() => runtime.Rotations.Count == 0);
        Assert.Equal(retained, runtime.CurrentOffset);
    }

    [Fact]
    public async Task OffRetainsOffsetResetRestoresAndShutdownDisposes()
    {
        var runtime = new FakeRuntime();
        var progress = new Recorder();
        var controller = new FlightController(() => runtime, progress, FreeFlightSettings);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));
        runtime.Frame = Frame(1, 0);
        await Wait(() => progress.Statuses.Any(s => s.Dragging));
        runtime.Frame = Frame(1, 1);
        await Wait(() => runtime.CurrentOffset.Position.X < -0.9f);
        controller.SetEnabled(false);
        var retained = runtime.CurrentOffset;
        runtime.Frame = Frame(1, 2);
        await Wait(() => progress.Statuses.Any(s => s.Connected && !s.Enabled));
        Assert.Equal(retained, runtime.CurrentOffset);
        controller.Reset();
        await Wait(() => runtime.CurrentOffset == RigidPose.Identity);
        await controller.DisposeAsync();
        Assert.True(runtime.Disposed);
    }

    [Fact]
    public async Task ResetWhileEnabledRestoresOffsetAndRequiresGripRearm()
    {
        var runtime = new FakeRuntime();
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress, FreeFlightSettings);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));

        runtime.Frame = Frame(1, 0);
        await Wait(() => progress.Statuses.Any(s => s.Dragging));
        runtime.Frame = Frame(1, 1);
        await Wait(() => runtime.CurrentOffset.Position.X < -0.9f);

        controller.Reset();
        await Wait(() => runtime.Restores > 0 && runtime.CurrentOffset == RigidPose.Identity);

        // The reset keeps the controller enabled but clears the active grip.
        await WaitForFrame(runtime, Frame(1, 2));
        Assert.Equal(RigidPose.Identity, runtime.CurrentOffset);

        // Releasing and gripping again arms Drag for the next movement.
        await WaitForFrame(runtime, Frame(0, 2));
        await WaitForFrame(runtime, Frame(1, 2));
        await WaitForFrame(runtime, Frame(1, 3));
        await Wait(() => runtime.CurrentOffset.Position.X < -0.9f);
        Assert.Contains(progress.Statuses, s => s.Connected && s.Enabled);
    }

    [Fact]
    public async Task ConnectionFailureReportsOffWithoutThrowingOnUiThread()
    {
        var progress = new Recorder();
        await using var controller = new FlightController(() => throw new InvalidOperationException("no runtime"), progress);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Message == "no runtime"));
        Assert.False(progress.Statuses.Last().Enabled);
    }

    [Fact]
    public async Task RetryConnectionKeepsEnabledUntilRuntimeBecomesAvailable()
    {
        var runtime = new FakeRuntime();
        var progress = new Recorder();
        int attempts = 0;
        await using var controller = new FlightController(
            () => Interlocked.Increment(ref attempts) == 1
                ? throw new InvalidOperationException("not started")
                : runtime,
            progress,
            retryRuntimeConnection: true);

        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));

        Assert.True(attempts >= 2);
        Assert.Contains(progress.Statuses, s => s.Enabled && !s.Connected);
    }

    [Fact]
    public async Task ReadFailureRestoresOwnedOffsetAndDisposes()
    {
        var runtime = new FakeRuntime();
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));
        runtime.ThrowOnRead = true;
        await Wait(() => runtime.Disposed);
        Assert.True(runtime.Restores > 0);
        await Wait(() => progress.Statuses.Any(s => s.Message.Contains("lost")));
    }

    [Fact]
    public async Task BothModeButtonsHeldForOneSecondToggleOnceUntilReleased()
    {
        var runtime = new FakeRuntime();
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));

        runtime.Frame = ModeFrame(1);
        await Wait(() => progress.Statuses.Any(
            s => s.Mode == FlightMode.FreeFlight && s.Message.Contains("切り替え")));
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            progress.Statuses,
            s => s.Mode == FlightMode.InfiniteWalking && s.Message.Contains("切り替え"));

        await WaitForFrame(runtime, ModeFrame(0));
        runtime.Frame = ModeFrame(1);
        await Wait(() => progress.Statuses.Any(
            s => s.Mode == FlightMode.InfiniteWalking && s.Message.Contains("切り替え")));
    }

    [Fact]
    public async Task StaggeredDpadDownPressesToggleWhenBothAreDownAtOneSecondCheck()
    {
        var runtime = new FakeRuntime();
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));

        runtime.Frame = DpadFrame(1, 0);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        runtime.Frame = DpadFrame(1, 1);

        await Wait(() => progress.Statuses.Any(
            s => s.Mode == FlightMode.FreeFlight && s.Message.Contains("切り替え")));
        Assert.Equal(1, runtime.HapticPulses);
    }

    [Fact]
    public async Task OneDpadDownHeldForOneSecondLevelsFreeFlightWithoutChangingMode()
    {
        var runtime = new FakeRuntime();
        var current = new RigidPose(
            Quaternion.CreateFromYawPitchRoll(0.4f, 0.7f, -0.3f),
            new(1, 2, 3));
        runtime.Apply(current);
        var progress = new Recorder();
        await using var controller = new FlightController(
            () => runtime,
            progress,
            FreeFlightSettings);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));
        var held = new InputFrame(
            new(Quaternion.Identity, new(0, 1.7f, 0)),
            true,
            new(RigidPose.Identity, 0, 0, 1, true),
            new(new(Quaternion.Identity, new(0.4f, 1.2f, -0.3f)), 1, 0, 0, true));
        Vector3 turnPivot = held.Right.Pose.Position;
        var expectedTransition = SpaceResetTransition.CreateFreeFlight(
            current,
            held.Head,
            turnPivot);
        var expected = expectedTransition.Advance(1);

        runtime.Frame = held;
        await Wait(() => progress.Statuses.Any(s => s.Message.Contains("水平へ戻しています")));
        await Wait(() => runtime.CurrentOffset.NearlyEquals(expected, 0.001f));

        Assert.DoesNotContain(progress.Statuses, s => s.Message.Contains("切り替え"));
        Assert.All(progress.Statuses, s => Assert.Equal(FlightMode.FreeFlight, s.Mode));
        Assert.True(Vector3.Distance(
            current.Transform(turnPivot),
            runtime.CurrentOffset.Transform(turnPivot)) < 0.001f);
    }

    [Fact]
    public async Task OneDpadDownHeldForOneSecondResetsOnlyInfiniteWalkingHeight()
    {
        var runtime = new FakeRuntime();
        var current = new RigidPose(
            Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, -0.3f),
            new(1, 2, 3));
        runtime.Apply(current);
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));

        runtime.Frame = DpadFrame(0, 1);
        await Wait(() => progress.Statuses.Any(s => s.Message.Contains("高さを戻しています")));
        await Wait(() => MathF.Abs(runtime.CurrentOffset.Position.Y) < 0.001f);

        Assert.True(1 - MathF.Abs(Quaternion.Dot(
            current.Orientation,
            runtime.CurrentOffset.Orientation)) < 0.0001f);
        Assert.Equal(current.Position.X, runtime.CurrentOffset.Position.X);
        Assert.Equal(current.Position.Z, runtime.CurrentOffset.Position.Z);
    }

    [Fact]
    public async Task FreeFlightToInfiniteWalkingLevelsOverOneSecond()
    {
        var runtime = new FakeRuntime();
        var current = new RigidPose(
            Quaternion.CreateFromYawPitchRoll(0.4f, 0.7f, -0.3f),
            new(1, 2, 3));
        runtime.Apply(current);
        var settings = FlugelKranzSettings.Default with { Mode = FlightMode.FreeFlight };
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress, () => settings);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));

        settings = settings with { Mode = FlightMode.InfiniteWalking };
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.InRange(runtime.CurrentOffset.Position.Y, 0.01f, 1.99f);

        var target = InfiniteWalkingTransition.CreateTarget(current, RigidPose.Identity);
        await Wait(() => runtime.CurrentOffset.NearlyEquals(target, 0.001f));
        Assert.Contains(progress.Statuses, s => s.Message.Contains("戻しています"));
    }

    [Fact]
    public async Task ReportsActiveValveIndexForceForBothHands()
    {
        var runtime = new FakeRuntime();
        var progress = new Recorder();
        await using var controller = new FlightController(() => runtime, progress);
        controller.SetEnabled(true);
        await Wait(() => progress.Statuses.Any(s => s.Connected));

        runtime.Frame = Frame(0, 0) with
        {
            Left = new(RigidPose.Identity, 0, 0, 0, true)
            {
                TrackpadForceActive = true,
                TrackpadForce = 0.42f
            },
            Right = new(RigidPose.Identity, 0, 0, 0, true)
            {
                TrackpadForceActive = true,
                TrackpadForce = 0.87f
            }
        };

        await Wait(() => progress.Statuses.Any(s =>
            s.LeftTrackpadForce == 0.42f && s.RightTrackpadForce == 0.87f));
    }

    private static InputFrame Frame(float grip, float x) => new(RigidPose.Identity, true,
        new(new(Quaternion.Identity, new(x, 0, 0)), grip, 0, 0, true),
        new(RigidPose.Identity, 0, 0, 0, true));
    private static FlugelKranzSettings FreeFlightSettings() =>
        FlugelKranzSettings.Default with { Mode = FlightMode.FreeFlight };
    private static InputFrame ModeFrame(float value) => new(
        RigidPose.Identity,
        true,
        new(RigidPose.Identity, 0, 0, value, true),
        new(RigidPose.Identity, 0, 0, value, true));
    private static InputFrame DpadFrame(float left, float right) => new(
        RigidPose.Identity,
        true,
        new(RigidPose.Identity, 0, 0, left, true),
        new(RigidPose.Identity, 0, 0, right, true));
    private static async Task Wait(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private static async Task WaitForFrame(FakeRuntime runtime, InputFrame frame)
    {
        int reads = runtime.Reads;
        runtime.Frame = frame;
        await Wait(() => runtime.Reads > reads + 2);
    }
    private sealed class Recorder : IProgress<FlightStatus>
    {
        public ConcurrentQueue<FlightStatus> Statuses { get; } = new();
        public void Report(FlightStatus value) => Statuses.Enqueue(value);
    }
    private sealed class RotationRuntime : FakeRuntime, ITrackerRotationRuntime
    {
        private IReadOnlyList<TrackerRotationOffset> rotations = [];
        public IReadOnlyList<TrackerRotationOffset> Rotations => Volatile.Read(ref rotations);
        public void Apply(RigidPose offset, IReadOnlyList<TrackerRotationOffset> value)
        {
            Apply(offset);
            Volatile.Write(ref rotations, value.ToArray());
        }
    }

    private sealed class PacedRuntime : FakeRuntime, IFramePacedFlightRuntime
    {
        public List<string> Calls { get; } = [];
        public float FrameElapsedSeconds { get; init; } = 0.6f;
        public int MaximumFrames { get; init; } = 2;
        public bool AdditionalUpdate { get; init; } = true;
        public Action? OnMidpoint { get; init; }
        private int waits;
        public bool WaitForMidFrameUpdate(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add("midpoint");
            OnMidpoint?.Invoke();
            return AdditionalUpdate;
        }
        public bool WaitFrame()
        {
            Calls.Add("wait");
            if (++waits > MaximumFrames) throw new IOException("finished");
            return true;
        }
        public void CommitFrame(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add("commit");
        }
        public void EndFrame() => Calls.Add("end");
    }

    private class FakeRuntime : IFlightRuntime, IHapticFeedback
    {
        private readonly object gate = new();
        private InputFrame frame = FlightControllerTests.Frame(0, 0);
        private RigidPose offset = RigidPose.Identity;
        public InputFrame Frame { get { lock (gate) return frame; } set { lock (gate) frame = value; } }
        public int Reads => Volatile.Read(ref reads);
        public RigidPose OriginalOffset => RigidPose.Identity;
        public RigidPose CurrentOffset { get { lock (gate) return offset; } }
        public volatile bool Disposed, ThrowOnRead;
        public int Restores;
        public int HapticPulses => Volatile.Read(ref hapticPulses);
        private int reads;
        private int hapticPulses;
        public InputFrame ReadPhysical()
        {
            Interlocked.Increment(ref reads);
            return ThrowOnRead ? throw new IOException("lost") : Frame;
        }
        public void Apply(RigidPose value) { lock (gate) offset = value; }
        public void Restore() { Restores++; Apply(OriginalOffset); }
        public void SendHapticPulse(float durationSeconds, float frequencyHz, float amplitude) =>
            Interlocked.Increment(ref hapticPulses);
        public void Dispose() => Disposed = true;
    }
}
