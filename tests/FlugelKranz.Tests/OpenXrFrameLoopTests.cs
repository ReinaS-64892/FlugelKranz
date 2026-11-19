using Evergine.Bindings.OpenXR;
using FlugelKranz.OpenXR;
using Xunit;

namespace FlugelKranz.Tests;

public class OpenXrFrameLoopTests
{
    [Fact]
    public void InvisibleFramesStillWaitBeginAndEndAtThePredictedDisplayTime()
    {
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api, new FakeClock());
        loop.Begin();
        Assert.Equal(["wait", "begin"], api.Calls);
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
    public void ApplicationDeadlineIsHalfAPeriodAfterTheActualWaitReturn(long period, long expected)
    {
        var clock = new FakeClock();
        var loop = new OpenXrFrameLoop(new FakeFrameApi { Period = period }, clock);
        loop.Begin();
        loop.CaptureInputTime();
        loop.WaitForApply(CancellationToken.None);
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
        loop.WaitForApply(CancellationToken.None);
        Assert.Equal(905_555_555, Assert.Single(clock.Deadlines));
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
            loop.WaitForApply(CancellationToken.None);
            Assert.Equal(wakeTime + period / 2, clock.Now);
            loop.End();
        }
        Assert.Equal(20, clock.Deadlines.Count);
    }

    [Fact]
    public void BeginProcessingTimeDoesNotShiftTheWriteDeadline()
    {
        var clock = new FakeClock();
        var api = new FakeFrameApi { OnBegin = () => clock.Now += 3_000_000 };
        var loop = new OpenXrFrameLoop(api, clock);
        loop.Begin();
        loop.CaptureInputTime();
        loop.WaitForApply(CancellationToken.None);
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
        loop.WaitForApply(CancellationToken.None);
        Assert.Equal(912_000_000, Assert.Single(clock.Deadlines));
        loop.End();
    }

    [Fact]
    public void OneLateFrameDoesNotStretchTheWritePhaseAndSustainedRateChangesAreFollowed()
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

    private sealed class FakeClock : IOpenXrClock
    {
        public long Now { get; set; } = 900_000_000;
        public List<long> Deadlines { get; } = [];
        public void WaitUntil(long time, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Deadlines.Add(time);
            Now = Math.Max(Now, time);
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
