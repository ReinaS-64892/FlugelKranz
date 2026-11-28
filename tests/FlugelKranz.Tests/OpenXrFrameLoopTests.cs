using Evergine.Bindings.OpenXR;
using FlugelKranz.OpenXR;
using FlugelKranz.Core;
using System.Numerics;
using Xunit;

namespace FlugelKranz.Tests;

public class OpenXrFrameLoopTests
{
    [Fact]
    public void FocusRecoveryStartsWithZeroElapsedTimeInsteadOfCountingTheInactiveGap()
    {
        var api = new FakeFrameApi();
        var clock = new FakeClock();
        var loop = new OpenXrFrameLoop(api, clock);
        loop.Begin();
        loop.CaptureInputTime();
        loop.End();
        loop.ResetInputTime();
        clock.Now += 2_000_000_000;
        api.DisplayTime += 2_000_000_000;
        loop.Begin();
        loop.CaptureInputTime();
        Assert.Equal(0, loop.ElapsedSeconds);
        loop.End();
    }

    [Fact]
    public void InvisibleFramesStillWaitBeginAndEndAtThePredictedDisplayTime()
    {
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api, new FakeClock());
        loop.Begin();
        Assert.Equal(["wait", "begin", "end:1000000000"], api.Calls);
        Assert.Equal(1_000_000_000, loop.DisplayTime);
        Assert.Equal(11_111_111, loop.DisplayPeriod);
        Assert.Equal(900_000_000, loop.CaptureInputTime());
        Assert.Equal(0, loop.ElapsedSeconds);
        loop.End();
        loop.End();
        Assert.Equal(["wait", "begin", "end:1000000000"], api.Calls);
    }

    [Fact]
    public void SimulationUsesActualInputTimesDespitePredictionJumpsAndRateChanges()
    {
        var api = new FakeFrameApi();
        var clock = new FakeClock();
        var loop = new OpenXrFrameLoop(api, clock);
        loop.Begin();
        Assert.Equal(clock.Now, loop.CaptureInputTime());
        loop.End();
        api.DisplayTime += 25_000_000;
        api.Period = 8_333_333;
        clock.Now += 10_000_000;
        loop.Begin();
        Assert.Equal(clock.Now, loop.CaptureInputTime());
        Assert.Equal(0.010f, loop.ElapsedSeconds, 6);
        Assert.Equal(8_333_333, loop.DisplayPeriod);
        loop.End();
    }

    [Theory]
    [InlineData(11_111_111, 905_555_555)]
    [InlineData(8_333_333, 904_166_666)]
    public void ComputationDeadlineIsHalfAPeriodAfterTheActualWaitReturn(long period, long expected)
    {
        var clock = new FakeClock();
        var loop = new OpenXrFrameLoop(new FakeFrameApi { Period = period }, clock);
        loop.Begin();
        loop.CaptureInputTime();
        Assert.True(loop.WaitForMidFrameUpdate(CancellationToken.None));
        Assert.Equal(expected, Assert.Single(clock.Deadlines));
        loop.End();
    }

    [Fact]
    public void ADeadlineMissDoesNotAddAnotherPeriodOrChangeTheTarget()
    {
        var clock = new FakeClock();
        var loop = new OpenXrFrameLoop(new FakeFrameApi(), clock);
        loop.Begin();
        clock.Now = 2_000_000_000; // Computation overran the fixed write deadline.
        Assert.False(loop.WaitForMidFrameUpdate(CancellationToken.None));
        Assert.Empty(clock.Deadlines);
        Assert.Equal(2_000_000_000, clock.Now);
        loop.End();
    }

    [Theory]
    [InlineData(33_333_333)]
    [InlineData(100_000_000)]
    [InlineData(1_000_000_000)]
    public void WirelessPredictionLeadDoesNotReduceTheUpdateRate(long predictionLead)
    {
        const long period = 11_111_111;
        var clock = new FakeClock();
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api, clock);
        for (int i = 0; i < 20; i++)
        {
            long wakeTime = 900_000_000 + i * period;
            clock.Now = wakeTime;
            api.DisplayTime = wakeTime + predictionLead;
            loop.Begin();
            loop.CaptureInputTime();
            Assert.True(loop.WaitForMidFrameUpdate(CancellationToken.None));
            Assert.Equal(wakeTime + period / 2, clock.Now);
            loop.End();
        }
        Assert.Equal(20, clock.Deadlines.Count);
    }

    [Fact]
    public void BeginProcessingTimeDoesNotShiftTheComputationDeadline()
    {
        var clock = new FakeClock();
        var api = new FakeFrameApi { OnBegin = () => clock.Now += 3_000_000 };
        var loop = new OpenXrFrameLoop(api, clock);
        loop.Begin();
        loop.CaptureInputTime();
        Assert.True(loop.WaitForMidFrameUpdate(CancellationToken.None));
        Assert.Equal(905_555_555, Assert.Single(clock.Deadlines));
        loop.End();
    }

    [Fact]
    public void TheSecondFrameUsesTheMeasuredCadenceInsteadOfTheReportedPeriod()
    {
        var clock = new FakeClock();
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api, clock);
        loop.Begin();
        Assert.Equal(api.Period, loop.UpdatePeriod);
        loop.End();
        clock.Now += 8_000_000;
        api.DisplayTime += 100_000_000; // Prediction lead changed, not the update cadence.
        loop.Begin();
        Assert.Equal(8_000_000, loop.UpdatePeriod);
        Assert.True(loop.WaitForMidFrameUpdate(CancellationToken.None));
        Assert.Equal(912_000_000, Assert.Single(clock.Deadlines));
        loop.End();
    }

    [Fact]
    public void OneLateFrameDoesNotStretchTheComputationPhaseAndSustainedRateChangesAreFollowed()
    {
        var clock = new FakeClock();
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api, clock);
        void Next(long interval)
        {
            clock.Now += interval;
            api.DisplayTime += 100_000_000;
            loop.Begin();
            loop.End();
        }
        loop.Begin();
        loop.End();
        for (int i = 0; i < 5; i++) Next(11_111_111);
        Next(50_000_000);
        Assert.Equal(11_111_111, loop.UpdatePeriod);
        for (int i = 0; i < 3; i++) Next(8_333_333);
        Assert.Equal(8_333_333, loop.UpdatePeriod);
    }

    [Fact]
    public void ASecondWaitCannotOverwriteAnUnfinishedFrame()
    {
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api, new FakeClock());
        loop.Begin();
        Assert.Throws<InvalidOperationException>(loop.Begin);
        loop.End();
        Assert.Equal(["wait", "begin", "end:1000000000"], api.Calls);
    }

    [Theory]
    [InlineData(0, 11_111_111)]
    [InlineData(1_000_000_000, 0)]
    [InlineData(-1, 11_111_111)]
    public void InvalidRuntimeTimingFailsInsteadOfRunningAnUnpacedLoop(long time, long period)
    {
        var api = new FakeFrameApi { DisplayTime = time, Period = period };
        var loop = new OpenXrFrameLoop(api, new FakeClock());
        Assert.Throws<InvalidOperationException>(loop.Begin);
        Assert.Equal(["wait"], api.Calls);
    }

    [Fact]
    public void NonIncreasingPredictedDisplayTimesAreRejected()
    {
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api, new FakeClock());
        loop.Begin();
        loop.End();
        Assert.Throws<InvalidOperationException>(loop.Begin);
    }

    [Theory]
    [InlineData(13_888_888)]
    [InlineData(11_111_111)]
    [InlineData(8_333_333)]
    public void TwoInputSamplesShareOneFrameAndUseActualHalfPeriodIntervals(long period)
    {
        var api = new FakeFrameApi { Period = period };
        var clock = new FakeClock();
        var loop = new OpenXrFrameLoop(api, clock);
        loop.Begin();
        long first = loop.CaptureInputTime();
        Assert.True(loop.WaitForMidFrameUpdate(CancellationToken.None));
        Assert.Equal(first + period / 2, loop.CaptureInputTime());
        Assert.Equal(period / 2 / 1_000_000_000f, loop.ElapsedSeconds);
        Assert.False(loop.WaitForMidFrameUpdate(CancellationToken.None));
        loop.End();
        Assert.Equal(["wait", "begin", "end:1000000000"], api.Calls);
    }

    [Theory]
    [InlineData(6_000_000, true)]
    [InlineData(11_111_111, false)]
    [InlineData(50_000_000, false)]
    public void LateComputationNeverAddsACatchUpBurst(long delay, bool expected)
    {
        var clock = new FakeClock();
        var loop = new OpenXrFrameLoop(new FakeFrameApi(), clock);
        loop.Begin();
        clock.Now += delay;
        Assert.Equal(expected, loop.WaitForMidFrameUpdate(CancellationToken.None));
        Assert.False(loop.WaitForMidFrameUpdate(CancellationToken.None));
        Assert.Equal(900_000_000 + delay, clock.Now);
        loop.End();
    }

    [Fact]
    public void OversleepSkipsTheExtraSampleAndCancellationStillAllowsEndingTheFrame()
    {
        var clock = new FakeClock { Oversleep = 30_000_000 };
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api, clock);
        loop.Begin();
        Assert.False(loop.WaitForMidFrameUpdate(CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => loop.WaitForMidFrameUpdate(cancellation.Token));
        loop.End();
        Assert.Equal("end:1000000000", api.Calls[^1]);
    }

    [Fact]
    public void FrameEndsBeforeImmediatePublicationAndMidpointMovementWaitsForTheNextFrame()
    {
        var clock = new FakeClock();
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api, clock);
        var applied = RigidPose.Identity;
        int publications = 0;
        var writer = new FrameOffsetWriter(() => applied, (offset, _) =>
        {
            Assert.StartsWith("end:", api.Calls[^1]);
            applied = offset;
            publications++;
        });
        loop.Begin();
        writer.Begin();
        Assert.Equal(["wait", "begin", "end:1000000000"], api.Calls);
        loop.CaptureInputTime();
        writer.Apply(writer.CurrentOffset with { Position = Vector3.UnitX }, []);
        writer.Commit(CancellationToken.None);
        Assert.Equal(900_000_000, clock.Now); // Publication has no half-period delay.
        Assert.Empty(clock.Deadlines);
        Assert.Equal(1, publications);
        Assert.True(loop.WaitForMidFrameUpdate(CancellationToken.None));
        loop.CaptureInputTime();
        writer.Apply(writer.CurrentOffset with { Position = 2 * Vector3.UnitX }, []);
        writer.End(preservePending: true);
        loop.End();
        Assert.Equal(Vector3.UnitX, applied.Position);
        Assert.Equal(1, publications);
        clock.Now = 911_111_111;
        api.DisplayTime += api.Period;
        loop.Begin();
        writer.Begin();
        Assert.Equal(2 * Vector3.UnitX, writer.CurrentOffset.Position);
        writer.Apply(writer.CurrentOffset with { Position = writer.CurrentOffset.Position + Vector3.UnitX }, []);
        writer.Commit(CancellationToken.None);
        Assert.Equal(2, publications);
        Assert.Equal(3 * Vector3.UnitX, applied.Position);
        writer.End();
        loop.End();
    }

    private sealed class FakeClock : IOpenXrClock
    {
        public long Now { get; set; } = 900_000_000;
        public long Oversleep { get; init; }
        public List<long> Deadlines { get; } = [];
        public void WaitUntil(long time, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Deadlines.Add(time);
            Now = Math.Max(Now, time) + Oversleep;
        }
    }

    private sealed class FakeFrameApi : IOpenXrFrameApi
    {
        public long DisplayTime = 1_000_000_000, Period = 11_111_111;
        public Action? OnBegin;
        public List<string> Calls { get; } = [];
        public XrFrameState Wait()
        {
            Calls.Add("wait");
            return new() { predictedDisplayTime = DisplayTime, predictedDisplayPeriod = Period, shouldRender = false };
        }
        public void Begin()
        {
            Calls.Add("begin");
            OnBegin?.Invoke();
        }
        public void End(long displayTime) => Calls.Add($"end:{displayTime}");
    }
}
