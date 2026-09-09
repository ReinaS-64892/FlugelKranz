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
        var controller = new FlightController(() => runtime, progress);
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

    private static InputFrame Frame(float grip, float x) => new(RigidPose.Identity, true,
        new(new(Quaternion.Identity, new(x, 0, 0)), grip, true), new(RigidPose.Identity, 0, true));
    private static async Task Wait(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
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
        public RigidPose OriginalOffset => RigidPose.Identity;
        public RigidPose CurrentOffset { get { lock (gate) return offset; } }
        public volatile bool Disposed, ThrowOnRead;
        public int Restores;
        public InputFrame ReadPhysical() => ThrowOnRead ? throw new IOException("lost") : Frame;
        public void Apply(RigidPose value) { lock (gate) offset = value; }
        public void Restore() { Restores++; Apply(OriginalOffset); }
        public void Dispose() => Disposed = true;
    }
}
