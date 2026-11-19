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
        var loop = new OpenXrFrameLoop(api);
        loop.Begin();
        Assert.Equal(["wait", "begin"], api.Calls);
        Assert.Equal(1_000_000_000, loop.DisplayTime);
        Assert.Equal(11_111_111, loop.DisplayPeriod);
        Assert.Equal(0.011111111f, loop.ElapsedSeconds, 6);
        loop.End();
        loop.End();
        Assert.Equal(["wait", "begin", "end:1000000000"], api.Calls);
    }

    [Fact]
    public void SimulationUsesDisplayTimeDifferencesAcrossRateChangesAndMissedFrames()
    {
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api);
        loop.Begin();
        loop.End();
        api.DisplayTime += 25_000_000;
        api.Period = 8_333_333;
        loop.Begin();
        Assert.Equal(0.025f, loop.ElapsedSeconds, 6);
        Assert.Equal(8_333_333, loop.DisplayPeriod);
        loop.End();
    }

    [Fact]
    public void ASecondWaitCannotOverwriteAnUnfinishedFrame()
    {
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api);
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
        var loop = new OpenXrFrameLoop(api);
        Assert.Throws<InvalidOperationException>(loop.Begin);
        Assert.Equal(["wait"], api.Calls);
    }

    [Fact]
    public void NonIncreasingPredictedDisplayTimesAreRejected()
    {
        var api = new FakeFrameApi();
        var loop = new OpenXrFrameLoop(api);
        loop.Begin();
        loop.End();
        Assert.Throws<InvalidOperationException>(loop.Begin);
    }

    private sealed class FakeFrameApi : IOpenXrFrameApi
    {
        public long DisplayTime = 1_000_000_000, Period = 11_111_111;
        public List<string> Calls { get; } = [];
        public XrFrameState Wait()
        {
            Calls.Add("wait");
            return new() { predictedDisplayTime = DisplayTime, predictedDisplayPeriod = Period, shouldRender = false };
        }
        public void Begin() => Calls.Add("begin");
        public void End(long displayTime) => Calls.Add($"end:{displayTime}");
    }
}
