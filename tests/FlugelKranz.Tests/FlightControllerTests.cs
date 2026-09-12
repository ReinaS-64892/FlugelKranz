using System.Collections.Concurrent;
using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class FlightControllerTests
{
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
    private sealed class FakeRuntime : IFlightRuntime
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
        private int reads;
        public InputFrame ReadPhysical()
        {
            Interlocked.Increment(ref reads);
            return ThrowOnRead ? throw new IOException("lost") : Frame;
        }
        public void Apply(RigidPose value) { lock (gate) offset = value; }
        public void Restore() { Restores++; Apply(OriginalOffset); }
        public void Dispose() => Disposed = true;
    }
}
